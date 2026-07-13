using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Dtos;
using ProConnect.WebAPI.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using ProConnect.WebAPI.Hubs;

namespace ProConnect.WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // All endpoints require authentication
    public class JobsController : ControllerBase
    {
        /// <summary>How many matched vendors get told about a new job.</summary>
        private const int VendorsToNotify = 5;

        /// <summary>Below this cosine similarity a job is not really about the query. Tuned against real embeddings.</summary>
        private const double SemanticMinimumScore = 0.72;

        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly NotificationService _notifications;
        private readonly ContentAiService _content;
        private readonly VendorMatchingService _matching;
        private readonly AiService _aiService;
        private readonly ILogger<JobsController> _logger;

        public JobsController(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hubContext,
            NotificationService notifications,
            ContentAiService content,
            VendorMatchingService matching,
            AiService aiService,
            ILogger<JobsController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _notifications = notifications;
            _content = content;
            _matching = matching;
            _aiService = aiService;
            _logger = logger;
        }

        // POST: api/jobs
        [HttpPost]
        [Authorize(Roles = "Customer")] // Only customers can create jobs
        public async Task<IActionResult> CreateJob([FromBody] CreateJobDto dto, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var customer = await _context.CustomerProfiles
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
            if (customer == null)
                return BadRequest("Customer profile not found. Please complete your registration.");

            var category = await _context.ServiceCategories.FindAsync(new object?[] { dto.ServiceCategoryId }, cancellationToken);
            if (category == null)
                return BadRequest("Invalid service category.");

            // One call does both: screen the post, and render it in English if it was not.
            var triage = await _content.TriageAsync($"{dto.Title}\n\n{dto.Description}", "job posting", cancellationToken);
            if (!triage.Allowed)
                return BadRequest(new { message = triage.Reason });

            var job = new Job
            {
                Title = dto.Title,
                Description = dto.Description,
                ImageUrl = dto.ImageUrl,
                ServiceCategoryId = dto.ServiceCategoryId,
                CustomerId = customer.Id,
                Location = dto.Location,
                BudgetMin = dto.BudgetMin,
                BudgetMax = dto.BudgetMax,
                PreferredDate = dto.PreferredDate,
                IsUrgent = dto.IsUrgent,
                Status = "Open",
                CreatedAt = DateTime.UtcNow
            };

            // Post in Sinhala or Tamil and vendors still read it in English; the original is kept.
            // The triage call above already did the translation, so this costs nothing extra.
            if (!triage.IsEnglish && !string.IsNullOrWhiteSpace(triage.English))
            {
                job.OriginalDescription = dto.Description;
                job.OriginalLanguage = triage.Language;

                // Triage saw "title\n\ndescription"; keep only the description half.
                var english = triage.English;
                var split = english.IndexOf("\n\n", StringComparison.Ordinal);
                job.Description = split >= 0 ? english[(split + 2)..].Trim() : english.Trim();
            }

            // Embedding powers semantic search. A null one just means this job won't match semantically.
            job.Embedding = await _content.EmbedJobAsync(job.Title, job.Description, cancellationToken);

            _context.Jobs.Add(job);
            await _context.SaveChangesAsync(cancellationToken);

            job.ServiceCategory = category;
            await NotifyMatchedVendorsAsync(job, customer.User?.FullName ?? "A customer", cancellationToken);

            var created = await LoadJobAsync(job.Id, cancellationToken);
            var caller = await ResolveCallerAsync(cancellationToken);
            return CreatedAtAction(nameof(GetJob), new { id = job.Id }, MapToJobResponse(created!, caller));
        }

        // GET: api/jobs
        // Filter, sort and page the job board.
        [HttpGet]
        public async Task<IActionResult> GetJobs([FromQuery] JobQueryDto query, CancellationToken cancellationToken)
        {
            var jobs = JobQuery();

            // "All" means every status; anything else filters; omitting it shows the open board.
            if (string.IsNullOrWhiteSpace(query.Status))
                jobs = jobs.Where(j => j.Status == "Open");
            else if (!string.Equals(query.Status, "All", StringComparison.OrdinalIgnoreCase))
                jobs = jobs.Where(j => j.Status == query.Status);

            if (query.CategoryId.HasValue)
                jobs = jobs.Where(j => j.ServiceCategoryId == query.CategoryId.Value);

            if (query.IsUrgent.HasValue)
                jobs = jobs.Where(j => j.IsUrgent == query.IsUrgent.Value);

            // A job matches the budget window if its range overlaps the requested one.
            if (query.MinBudget.HasValue)
                jobs = jobs.Where(j => j.BudgetMax >= query.MinBudget.Value);

            if (query.MaxBudget.HasValue)
                jobs = jobs.Where(j => j.BudgetMin <= query.MaxBudget.Value);

            if (!string.IsNullOrWhiteSpace(query.Location))
            {
                var location = $"%{Escape(query.Location)}%";
                jobs = jobs.Where(j => j.Location != null && EF.Functions.Like(j.Location, location, EscapeChar));
            }

            // Semantic search ranks by meaning, so it replaces both the keyword filter and the sort.
            if (query.Semantic && !string.IsNullOrWhiteSpace(query.Search))
            {
                var semantic = await SemanticSearchAsync(jobs, query, cancellationToken);
                if (semantic != null)
                {
                    return Ok(semantic);
                }
                // The AI was unreachable: fall through to keyword search rather than returning nothing.
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = $"%{Escape(query.Search)}%";
                jobs = jobs.Where(j =>
                    EF.Functions.Like(j.Title, term, EscapeChar) ||
                    EF.Functions.Like(j.Description, term, EscapeChar) ||
                    (j.Location != null && EF.Functions.Like(j.Location, term, EscapeChar)));
            }

            jobs = query.SortBy?.ToLowerInvariant() switch
            {
                "oldest" => jobs.OrderBy(j => j.CreatedAt),
                "budgethigh" => jobs.OrderByDescending(j => j.BudgetMax),
                "budgetlow" => jobs.OrderBy(j => j.BudgetMin),
                "mostbids" => jobs.OrderByDescending(j => j.Bids.Count).ThenByDescending(j => j.CreatedAt),
                _ => jobs.OrderByDescending(j => j.IsUrgent).ThenByDescending(j => j.CreatedAt)
            };

            var totalCount = await jobs.CountAsync(cancellationToken);

            var page = await jobs
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            var caller = await ResolveCallerAsync(cancellationToken);

            return Ok(new PagedResult<JobResponseDto>
            {
                Items = page.Select(j => MapToJobResponse(j, caller)).ToList(),
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = totalCount
            });
        }

        // GET: api/jobs/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetJob(int id, CancellationToken cancellationToken)
        {
            var job = await LoadJobAsync(id, cancellationToken);
            if (job == null)
                return NotFound("Job not found.");

            var caller = await ResolveCallerAsync(cancellationToken);
            return Ok(MapToJobResponse(job, caller));
        }

        // POST: api/Jobs/{jobId}/bids/{bidId}/accept
        // Accepting a bid assigns the vendor AND opens the booking that carries the work to completion.
        [HttpPost("{jobId}/bids/{bidId}/accept")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> AcceptBid(int jobId, int bidId, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var job = await _context.Jobs
                .Include(j => j.Customer)
                .Include(j => j.Bids)
                .Include(j => j.Booking)
                .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

            if (job == null)
                return NotFound("Job not found.");

            if (job.Customer.UserId != userId)
                return Forbid();

            if (job.Status != "Open")
                return BadRequest(new { message = "This job is no longer open for bids." });

            var bid = await _context.JobBids
                .Include(b => b.Vendor)
                .FirstOrDefaultAsync(b => b.Id == bidId && b.JobId == jobId, cancellationToken);

            if (bid == null)
                return NotFound("Bid not found.");

            if (bid.Status != "Pending")
                return BadRequest(new { message = "This bid has already been processed." });

            bid.Status = "Accepted";
            job.VendorProfileId = bid.VendorProfileId;
            job.Status = "Assigned";

            // Every other pending bid loses.
            var otherBids = await _context.JobBids
                .Where(b => b.JobId == jobId && b.Id != bidId && b.Status == "Pending")
                .ToListAsync(cancellationToken);

            foreach (var otherBid in otherBids)
                otherBid.Status = "Rejected";

            // The booking is the unit of work from here on: schedule, start, complete, review.
            if (job.Booking == null)
            {
                _context.Bookings.Add(new Booking
                {
                    JobId = job.Id,
                    CustomerId = job.CustomerId,
                    VendorProfileId = bid.VendorProfileId,
                    ScheduledDate = job.PreferredDate,
                    Status = "Scheduled",
                    TotalPrice = bid.BidAmount,
                    IsPaid = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            await _notifications.NotifyUserAsync(
                bid.Vendor.UserId,
                "Bid Accepted",
                $"Your bid on \"{job.Title}\" was accepted. The job is now booked.",
                $"/jobs/{job.Id}",
                cancellationToken);

            return Ok(new { message = "Bid accepted, vendor assigned and booking created." });
        }

        // GET: api/Jobs/my-jobs
        [HttpGet("my-jobs")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> GetMyJobs(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var customer = await _context.CustomerProfiles
                .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

            if (customer == null)
                return NotFound("Customer profile not found.");

            var jobs = await JobQuery()
                .Where(j => j.CustomerId == customer.Id)
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync(cancellationToken);

            var caller = await ResolveCallerAsync(cancellationToken);
            return Ok(jobs.Select(j => MapToJobResponse(j, caller)).ToList());
        }

        // GET: api/Jobs/my-bids
        [HttpGet("my-bids")]
        [Authorize(Roles = "Vendor")]
        public async Task<IActionResult> GetMyBids(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var vendor = await _context.VendorProfiles
                .FirstOrDefaultAsync(v => v.UserId == userId, cancellationToken);

            if (vendor == null)
                return NotFound("Vendor profile not found.");

            var bids = await _context.JobBids
                .Include(b => b.Job)
                    .ThenInclude(j => j.ServiceCategory)
                .Include(b => b.Job)
                    .ThenInclude(j => j.Customer)
                        .ThenInclude(c => c.User)
                .Where(b => b.VendorProfileId == vendor.Id)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync(cancellationToken);

            var responses = bids.Select(b => new
            {
                bidId = b.Id,
                jobId = b.JobId,
                jobTitle = b.Job.Title,
                jobStatus = b.Job.Status,
                serviceCategory = b.Job.ServiceCategory.Name,
                customerName = b.Job.Customer.User.FullName,
                bidAmount = b.BidAmount,
                proposalMessage = b.ProposalMessage,
                estimatedDays = b.EstimatedDays,
                bidStatus = b.Status,
                createdAt = b.CreatedAt,
                expiresAt = b.ExpiresAt
            }).ToList();

            return Ok(responses);
        }

        // GET: api/Jobs/assigned
        // Everything the vendor is on the hook for: assigned, in progress, and recently completed.
        [HttpGet("assigned")]
        [Authorize(Roles = "Vendor")]
        public async Task<IActionResult> GetAssignedJobs(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var vendor = await _context.VendorProfiles
                .FirstOrDefaultAsync(v => v.UserId == userId, cancellationToken);

            if (vendor == null)
                return NotFound("Vendor profile not found.");

            var jobs = await JobQuery()
                .Where(j => j.VendorProfileId == vendor.Id &&
                            (j.Status == "Assigned" || j.Status == "InProgress" || j.Status == "Completed"))
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync(cancellationToken);

            var caller = await ResolveCallerAsync(cancellationToken);
            return Ok(jobs.Select(j => MapToJobResponse(j, caller)).ToList());
        }

        // POST: api/Jobs/{jobId}/bids/{bidId}/reject
        [HttpPost("{jobId}/bids/{bidId}/reject")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> RejectBid(int jobId, int bidId, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var job = await _context.Jobs
                .Include(j => j.Customer)
                .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

            if (job == null)
                return NotFound("Job not found.");

            if (job.Customer.UserId != userId)
                return Forbid();

            var bid = await _context.JobBids
                .FirstOrDefaultAsync(b => b.Id == bidId && b.JobId == jobId, cancellationToken);

            if (bid == null)
                return NotFound("Bid not found.");

            if (bid.Status != "Pending")
                return BadRequest(new { message = "This bid has already been processed." });

            bid.Status = "Rejected";
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Bid rejected successfully." });
        }

        // POST: api/jobs/{id}/bids
        [HttpPost("{id}/bids")]
        [Authorize(Roles = "Vendor")] // Only vendors can bid
        public async Task<IActionResult> PlaceBid(int id, [FromBody] CreateBidDto dto, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var vendor = await _context.VendorProfiles
                .Include(v => v.User)
                .FirstOrDefaultAsync(v => v.UserId == userId, cancellationToken);
            if (vendor == null)
                return BadRequest("Vendor profile not found. Please complete your registration.");

            var job = await _context.Jobs
                .Include(j => j.Customer)
                .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
            if (job == null)
                return NotFound("Job not found.");

            if (job.Status != "Open")
                return BadRequest(new { message = "Job is no longer open for bids." });

            var existingBid = await _context.JobBids
                .FirstOrDefaultAsync(b => b.JobId == id && b.VendorProfileId == vendor.Id, cancellationToken);
            if (existingBid != null)
                return BadRequest(new { message = "You have already placed a bid on this job." });

            var bid = new JobBid
            {
                JobId = id,
                VendorProfileId = vendor.Id,
                BidAmount = dto.BidAmount,
                ProposalMessage = dto.ProposalMessage,
                EstimatedDays = dto.EstimatedDays,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(2)
            };

            _context.JobBids.Add(bid);
            await _context.SaveChangesAsync(cancellationToken);

            await _notifications.NotifyUserAsync(
                job.Customer.UserId,
                "New Bid Placed",
                $"New bid on \"{job.Title}\": ${bid.BidAmount} by {vendor.User?.FullName ?? "A vendor"}",
                $"/jobs/{job.Id}",
                cancellationToken);

            return Ok(new { message = "Bid placed successfully.", bidId = bid.Id });
        }

        // GET: api/jobs/{id}/bids
        [HttpGet("{id}/bids")]
        public async Task<IActionResult> GetBidsForJob(int id, CancellationToken cancellationToken)
        {
            var job = await _context.Jobs
                .Include(j => j.Customer)
                .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

            if (job == null)
                return NotFound("Job not found.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (job.Customer.UserId != userId)
                return Forbid();

            var bids = await _context.JobBids
                .Include(b => b.Vendor)
                    .ThenInclude(v => v.User)
                .Where(b => b.JobId == id)
                .ToListAsync(cancellationToken);

            var response = bids.Select(b => new BidResponseDto
            {
                Id = b.Id,
                BidAmount = b.BidAmount,
                ProposalMessage = b.ProposalMessage,
                EstimatedDays = b.EstimatedDays,
                Status = b.Status,
                VendorName = b.Vendor?.User?.FullName ?? "Unknown",
                VendorCompany = b.Vendor?.CompanyName ?? "Unknown",
                CreatedAt = b.CreatedAt
            }).ToList();

            return Ok(response);
        }

        // GET: api/jobs/{id}/recommended-vendors
        // The vendors the AI thinks fit this job, for the customer who posted it.
        [HttpGet("{id}/recommended-vendors")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> GetRecommendedVendors(int id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var job = await _context.Jobs
                .Include(j => j.Customer)
                .Include(j => j.ServiceCategory)
                .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

            if (job == null)
                return NotFound(new { message = "Job not found." });

            if (job.Customer.UserId != userId)
                return Forbid();

            var matches = await _matching.MatchAsync(job, VendorsToNotify, cancellationToken);
            return Ok(matches);
        }

        // GET: api/jobs/{id}/rank-bids
        // Weighs the bids on price, timeline, proposal and vendor reputation.
        [HttpGet("{id}/rank-bids")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> RankBids(int id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var job = await _context.Jobs
                .Include(j => j.Customer)
                .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

            if (job == null)
                return NotFound(new { message = "Job not found." });

            if (job.Customer.UserId != userId)
                return Forbid();

            var bids = await _context.JobBids
                .Include(b => b.Vendor)
                .Where(b => b.JobId == id && b.Status == "Pending")
                .ToListAsync(cancellationToken);

            if (bids.Count == 0)
                return BadRequest(new { message = "There are no open bids to compare yet." });

            try
            {
                var ranking = await _aiService.RankBidsAsync(
                    job.Title,
                    job.Description,
                    job.BudgetMin,
                    job.BudgetMax,
                    bids.Select(b => (
                        b.Id,
                        b.BidAmount,
                        b.EstimatedDays,
                        b.ProposalMessage,
                        b.Vendor?.CompanyName ?? "Unknown",
                        b.Vendor?.AverageRating ?? 0,
                        b.Vendor?.TotalReviews ?? 0)).ToList(),
                    cancellationToken);

                return Ok(ranking);
            }
            catch (AiUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Ranks the query against stored job embeddings in memory. SQLite has no vector type, and at
        /// this scale a full scan is cheaper than the machinery a real vector store would need.
        /// Returns null when the query could not be embedded, so the caller can fall back to keywords.
        /// </summary>
        private async Task<PagedResult<JobResponseDto>?> SemanticSearchAsync(
            IQueryable<Job> filtered,
            JobQueryDto query,
            CancellationToken cancellationToken)
        {
            var queryVector = await _content.EmbedQueryAsync(query.Search!, cancellationToken);
            if (queryVector == null)
            {
                return null;
            }

            var candidates = await filtered
                .Where(j => j.Embedding != null)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return null; // nothing embedded yet — keyword search will do better than an empty list
            }

            var scored = candidates
                .Select(job => new
                {
                    Job = job,
                    Score = ContentAiService.Deserialize(job.Embedding) is { } vector
                        ? AiService.CosineSimilarity(queryVector, vector)
                        : 0
                })
                .Where(x => x.Score > SemanticMinimumScore)
                .OrderByDescending(x => x.Score)
                .ToList();

            var caller = await ResolveCallerAsync(cancellationToken);

            return new PagedResult<JobResponseDto>
            {
                Items = scored
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .Select(x => MapToJobResponse(x.Job, caller))
                    .ToList(),
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = scored.Count
            };
        }

        /// <summary>
        /// Tells the vendors who actually fit, rather than blasting every vendor in the database.
        /// Notification failure must never take the job posting down with it.
        /// </summary>
        private async Task NotifyMatchedVendorsAsync(Job job, string customerName, CancellationToken cancellationToken)
        {
            try
            {
                var matches = await _matching.MatchAsync(job, VendorsToNotify, cancellationToken);
                var userIds = await _matching.ResolveUserIdsAsync(matches, cancellationToken);

                if (userIds.Count == 0)
                {
                    return;
                }

                var title = "Job Matched to You";
                var message = $"New {job.ServiceCategory?.Name ?? "job"} job matches your skills: {job.Title} (posted by {customerName})";

                foreach (var userId in userIds)
                {
                    await _notifications.NotifyUserAsync(userId, title, message, $"/jobs/{job.Id}", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not notify matched vendors for job {JobId}.", job.Id);
            }
        }

        /// <summary>Everything MapToJobResponse needs, loaded up front so we don't query per row.</summary>
        private IQueryable<Job> JobQuery() =>
            _context.Jobs
                .Include(j => j.ServiceCategory)
                .Include(j => j.Customer)
                    .ThenInclude(c => c.User)
                .Include(j => j.AssignedVendor)
                    .ThenInclude(v => v!.User)
                .Include(j => j.Bids)
                .Include(j => j.Booking)
                .Include(j => j.Review);

        private Task<Job?> LoadJobAsync(int id, CancellationToken cancellationToken) =>
            JobQuery().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        /// <summary>The caller's profile ids, so each job can say whether it belongs to them.</summary>
        private async Task<(string? CustomerProfileId, string? VendorProfileId)> ResolveCallerAsync(
            CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return (null, null);

            var customerId = await _context.CustomerProfiles
                .Where(c => c.UserId == userId)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var vendorId = await _context.VendorProfiles
                .Where(v => v.UserId == userId)
                .Select(v => v.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return (customerId, vendorId);
        }

        private static JobResponseDto MapToJobResponse(Job job, (string? CustomerProfileId, string? VendorProfileId) caller)
        {
            return new JobResponseDto
            {
                Id = job.Id,
                Title = job.Title,
                Description = job.Description,
                ImageUrl = job.ImageUrl,
                ServiceCategoryName = job.ServiceCategory?.Name ?? "Unknown",
                ServiceCategoryId = job.ServiceCategoryId,
                CustomerName = job.Customer?.User?.FullName ?? "Unknown",
                Location = job.Location,
                BudgetMin = job.BudgetMin,
                BudgetMax = job.BudgetMax,
                PreferredDate = job.PreferredDate,
                IsUrgent = job.IsUrgent,
                Status = job.Status,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                AssignedVendorName = job.AssignedVendor?.User?.FullName,
                AssignedVendorCompany = job.AssignedVendor?.CompanyName,
                BidCount = job.Bids?.Count ?? 0,
                IsOwner = caller.CustomerProfileId != null && job.CustomerId == caller.CustomerProfileId,
                IsAssignedVendor = caller.VendorProfileId != null && job.VendorProfileId == caller.VendorProfileId,
                BookingId = job.Booking?.Id,
                BookingStatus = job.Booking?.Status,
                HasReview = job.Review != null,
                OriginalDescription = job.OriginalDescription,
                OriginalLanguage = job.OriginalLanguage,
                CompletionImageUrl = job.CompletionImageUrl,
                CompletionVerdict = job.CompletionVerdict
            };
        }

        /// <summary>Keeps a user's % or _ from acting as a LIKE wildcard (SQLite needs an ESCAPE clause).</summary>
        private const string EscapeChar = "\\";

        private static string Escape(string term) => term
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }
}
