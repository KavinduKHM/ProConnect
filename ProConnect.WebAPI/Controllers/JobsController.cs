using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Dtos;
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
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<NotificationHub> _hubContext;

        public JobsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
        }

        // POST: api/jobs
        [HttpPost]
        [Authorize(Roles = "Customer")] // Only customers can create jobs
        public async Task<IActionResult> CreateJob([FromBody] CreateJobDto dto)
        {
            // Get the logged-in user
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Find the customer profile
            var customer = await _context.CustomerProfiles
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (customer == null)
                return BadRequest("Customer profile not found. Please complete your registration.");

            // Validate ServiceCategory
            var category = await _context.ServiceCategories.FindAsync(dto.ServiceCategoryId);
            if (category == null)
                return BadRequest("Invalid service category.");

            var job = new Job
            {
                Title = dto.Title,
                Description = dto.Description,
                ImageUrl = dto.ImageUrl,
                ServiceCategoryId = dto.ServiceCategoryId,
                CustomerId = customer.Id, // CustomerProfile.Id (string)
                Location = dto.Location,
                BudgetMin = dto.BudgetMin,
                BudgetMax = dto.BudgetMax,
                PreferredDate = dto.PreferredDate,
                IsUrgent = dto.IsUrgent,
                Status = "Open",
                CreatedAt = DateTime.UtcNow
            };

            _context.Jobs.Add(job);
            await _context.SaveChangesAsync();

            var notificationTitle = "New Job Posted";
            var notificationMessage = $"New job posted: {job.Title} by {customer.User?.FullName ?? "Unknown"}";
            var notificationActionUrl = $"/jobs/{job.Id}";

            var vendorUserIds = await _context.VendorProfiles.Select(v => v.UserId).ToListAsync();
            var notifications = vendorUserIds.Select(id => new Notification
            {
                UserId = id,
                Title = notificationTitle,
                Message = notificationMessage,
                ActionUrl = notificationActionUrl,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            }).ToList();
            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[SignalR] Broadcasting NewJobPosted for Job ID: {job.Id} to Vendors group");
            
            // Send the first notification DTO (all have same content except UserId)
            var notificationDto = new NotificationDto
            {
                Id = notifications.FirstOrDefault()?.Id ?? 0,
                Title = notificationTitle,
                Message = notificationMessage,
                ActionUrl = notificationActionUrl,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };
            
            await _hubContext.Clients.Group("Vendors").SendAsync("NewNotification", notificationDto);
            Console.WriteLine("[SignalR] Broadcast NewNotification sent");

            // Return the created job
            var response = await MapToJobResponse(job);
            return CreatedAtAction(nameof(GetJob), new { id = job.Id }, response);
        }

        // GET: api/jobs
        [HttpGet]
        public async Task<IActionResult> GetJobs([FromQuery] string? status = null, [FromQuery] int? categoryId = null)
        {
            var query = _context.Jobs
                .Include(j => j.ServiceCategory)
                .Include(j => j.Customer)
                .Include(j => j.Bids)
                .AsQueryable();

            // Filter by status if provided (default: only "Open" jobs)
            if (!string.IsNullOrEmpty(status))
                query = query.Where(j => j.Status == status);
            else
                query = query.Where(j => j.Status == "Open"); // Show open jobs by default

            if (categoryId.HasValue)
                query = query.Where(j => j.ServiceCategoryId == categoryId.Value);

            var jobs = await query
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();

            var responses = new List<JobResponseDto>();
            foreach (var job in jobs)
                responses.Add(await MapToJobResponse(job));

            return Ok(responses);
        }

        // GET: api/jobs/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetJob(int id)
        {
            var job = await _context.Jobs
                .Include(j => j.ServiceCategory)
                .Include(j => j.Customer)
                .Include(j => j.AssignedVendor)
                .Include(j => j.Bids)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (job == null)
                return NotFound("Job not found.");

            var response = await MapToJobResponse(job);
            return Ok(response);
        }

        // POST: api/Jobs/{jobId}/bids/{bidId}/accept
        [HttpPost("{jobId}/bids/{bidId}/accept")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> AcceptBid(int jobId, int bidId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var job = await _context.Jobs
                .Include(j => j.Customer)
                .Include(j => j.Bids)
                .FirstOrDefaultAsync(j => j.Id == jobId);
            
            if (job == null)
                return NotFound("Job not found.");

            // Verify the user owns this job
            if (job.Customer.UserId != userId)
                return Forbid("You do not own this job.");

            // Verify job is still open
            if (job.Status != "Open")
                return BadRequest("This job is no longer open for bids.");

            var bid = await _context.JobBids
                .Include(b => b.Vendor)
                .FirstOrDefaultAsync(b => b.Id == bidId && b.JobId == jobId);
            
            if (bid == null)
                return NotFound("Bid not found.");

            if (bid.Status != "Pending")
                return BadRequest("This bid has already been processed.");

            // Accept the selected bid
            bid.Status = "Accepted";
            
            // Assign vendor to the job
            job.VendorProfileId = bid.VendorProfileId;
            job.Status = "Assigned";
            
            // Reject all other pending bids for this job
            var otherBids = await _context.JobBids
                .Where(b => b.JobId == jobId && b.Id != bidId && b.Status == "Pending")
                .ToListAsync();
            
            foreach (var otherBid in otherBids)
            {
                otherBid.Status = "Rejected";
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Bid accepted and vendor assigned successfully." });
        }

        // GET: api/Jobs/my-jobs
        [HttpGet("my-jobs")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> GetMyJobs()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Find the customer profile
            var customer = await _context.CustomerProfiles
                .FirstOrDefaultAsync(c => c.UserId == userId);
            
            if (customer == null)
                return NotFound("Customer profile not found.");

            var jobs = await _context.Jobs
                .Include(j => j.ServiceCategory)
                .Include(j => j.Bids)
                .Where(j => j.CustomerId == customer.Id)
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();

            var responses = new List<JobResponseDto>();
            foreach (var job in jobs)
                responses.Add(await MapToJobResponse(job));

            return Ok(responses);
        }

        // GET: api/Jobs/my-bids
        [HttpGet("my-bids")]
        [Authorize(Roles = "Vendor")]
        public async Task<IActionResult> GetMyBids()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Find the vendor profile
            var vendor = await _context.VendorProfiles
                .FirstOrDefaultAsync(v => v.UserId == userId);
            
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
                .ToListAsync();

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
        [HttpGet("assigned")]
        [Authorize(Roles = "Vendor")]
        public async Task<IActionResult> GetAssignedJobs()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var vendor = await _context.VendorProfiles
                .FirstOrDefaultAsync(v => v.UserId == userId);
            
            if (vendor == null)
                return NotFound("Vendor profile not found.");

            var jobs = await _context.Jobs
                .Include(j => j.ServiceCategory)
                .Include(j => j.Customer)
                .Where(j => j.VendorProfileId == vendor.Id && j.Status == "Assigned")
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();

            var responses = new List<JobResponseDto>();
            foreach (var job in jobs)
                responses.Add(await MapToJobResponse(job));

            return Ok(responses);
        }

        // POST: api/Jobs/{jobId}/bids/{bidId}/reject
        [HttpPost("{jobId}/bids/{bidId}/reject")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> RejectBid(int jobId, int bidId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var job = await _context.Jobs
                .Include(j => j.Customer)
                .FirstOrDefaultAsync(j => j.Id == jobId);
            
            if (job == null)
                return NotFound("Job not found.");

            // Verify the user owns this job
            if (job.Customer.UserId != userId)
                return Forbid("You do not own this job.");

            var bid = await _context.JobBids
                .FirstOrDefaultAsync(b => b.Id == bidId && b.JobId == jobId);
            
            if (bid == null)
                return NotFound("Bid not found.");

            if (bid.Status != "Pending")
                return BadRequest("This bid has already been processed.");

            // Reject the bid
            bid.Status = "Rejected";

            await _context.SaveChangesAsync();

            return Ok(new { message = "Bid rejected successfully." });
        }

        // POST: api/jobs/{id}/bids
        [HttpPost("{id}/bids")]
        [Authorize(Roles = "Vendor")] // Only vendors can bid
        public async Task<IActionResult> PlaceBid(int id, [FromBody] CreateBidDto dto)
        {
            // Get logged-in user
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Find vendor profile
            var vendor = await _context.VendorProfiles.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
                return BadRequest("Vendor profile not found. Please complete your registration.");

            // Find job
            var job = await _context.Jobs.FindAsync(id);
            if (job == null)
                return NotFound("Job not found.");

            // Check if job is open
            if (job.Status != "Open")
                return BadRequest("Job is no longer open for bids.");

            // Check if vendor already bid
            var existingBid = await _context.JobBids
                .FirstOrDefaultAsync(b => b.JobId == id && b.VendorProfileId == vendor.Id);
            if (existingBid != null)
                return BadRequest("You have already placed a bid on this job.");

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
            await _context.SaveChangesAsync();

            var jobWithCustomer = await _context.Jobs
                .Include(j => j.Customer)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (jobWithCustomer != null)
            {
                // Get the customer's UserId (the AspNetUsers Id)
                var customerUserId = jobWithCustomer.Customer.UserId; // This is the Identity user Id
                var vendorWithUser = await _context.VendorProfiles
                    .Include(v => v.User)
                    .FirstOrDefaultAsync(v => v.Id == vendor.Id);

                var notification = new Notification
                {
                    UserId = customerUserId,
                    Title = "New Bid Placed",
                    Message = $"New bid on \"{jobWithCustomer.Title}\": ${bid.BidAmount} by {vendorWithUser?.User?.FullName ?? "A vendor"}",
                    ActionUrl = $"/jobs/{jobWithCustomer.Id}",
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                };
                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                Console.WriteLine($"[SignalR] Broadcasting NewNotification to Customer User ID: {customerUserId}");
                var notificationDto = new NotificationDto
                {
                    Id = notification.Id,
                    Title = notification.Title,
                    Message = notification.Message,
                    ActionUrl = notification.ActionUrl,
                    CreatedAt = notification.CreatedAt,
                    IsRead = notification.IsRead
                };
                await _hubContext.Clients.User(customerUserId).SendAsync("NewNotification", notificationDto);
                Console.WriteLine("[SignalR] Broadcast NewNotification sent");
            }
            else 
            {
                Console.WriteLine("[SignalR] jobWithCustomer is null, cannot broadcast bid");
            }


            return Ok(new { message = "Bid placed successfully.", bidId = bid.Id });
        }

        // GET: api/jobs/{id}/bids
        [HttpGet("{id}/bids")]
        public async Task<IActionResult> GetBidsForJob(int id)
        {
            var job = await _context.Jobs
                .Include(j => j.Customer)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (job == null)
                return NotFound("Job not found.");

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Only allow the customer who owns the job to see bids
            if (job.Customer.UserId != userId)
                return Forbid("You are not the owner of this job.");

            var bids = await _context.JobBids
                .Include(b => b.Vendor)
                    .ThenInclude(v => v.User)
                .Where(b => b.JobId == id)
                .ToListAsync();

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

        // Helper method to map Job to JobResponseDto
        private async Task<JobResponseDto> MapToJobResponse(Job job)
        {
            // Get customer name
            var customerUser = await _userManager.FindByIdAsync(job.Customer.UserId);
            var customerName = customerUser?.FullName ?? "Unknown";

            // Get assigned vendor name if any
            string? assignedVendorName = null;
            if (job.AssignedVendor != null)
            {
                var vendorUser = await _userManager.FindByIdAsync(job.AssignedVendor.UserId);
                assignedVendorName = vendorUser?.FullName ?? "Unknown";
            }

            return new JobResponseDto
            {
                Id = job.Id,
                Title = job.Title,
                Description = job.Description,
                ImageUrl = job.ImageUrl,
                ServiceCategoryName = job.ServiceCategory?.Name ?? "Unknown",
                CustomerName = customerName,
                Location = job.Location,
                BudgetMin = job.BudgetMin,
                BudgetMax = job.BudgetMax,
                PreferredDate = job.PreferredDate,
                IsUrgent = job.IsUrgent,
                Status = job.Status,
                CreatedAt = job.CreatedAt,
                AssignedVendorName = assignedVendorName,
                BidCount = job.Bids?.Count ?? 0
            };
        }
    }
}