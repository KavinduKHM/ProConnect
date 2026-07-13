using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Dtos;
using ProConnect.WebAPI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace ProConnect.WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ReviewsController : ControllerBase
    {
        /// <summary>How many recent reviews feed the AI reputation blurb.</summary>
        private const int SummaryWindow = 20;

        /// <summary>
        /// Re-summarize only once this many new reviews have landed since the last blurb. Without this
        /// a vendor with 200 reviews would pay for 200 summaries.
        /// </summary>
        private const int SummaryStaleAfter = 3;

        private readonly ApplicationDbContext _context;
        private readonly AiService _aiService;
        private readonly ContentAiService _content;
        private readonly NotificationService _notifications;
        private readonly ILogger<ReviewsController> _logger;

        public ReviewsController(
            ApplicationDbContext context,
            AiService aiService,
            ContentAiService content,
            NotificationService notifications,
            ILogger<ReviewsController> logger)
        {
            _context = context;
            _aiService = aiService;
            _content = content;
            _notifications = notifications;
            _logger = logger;
        }

        // POST: api/reviews
        // The customer rates the vendor once the job is complete.
        [HttpPost]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> CreateReview([FromBody] CreateReviewDto dto, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var job = await _context.Jobs
                .Include(j => j.Customer)
                .Include(j => j.AssignedVendor)
                .Include(j => j.Review)
                .FirstOrDefaultAsync(j => j.Id == dto.JobId, cancellationToken);

            if (job == null)
                return NotFound(new { message = "Job not found." });

            if (job.Customer.UserId != userId)
                return Forbid();

            if (job.Status != "Completed")
                return BadRequest(new { message = "You can only review a job once it has been completed." });

            if (job.Review != null)
                return BadRequest(new { message = "You have already reviewed this job." });

            if (string.IsNullOrEmpty(job.VendorProfileId) || job.AssignedVendor == null)
                return BadRequest(new { message = "This job has no assigned vendor to review." });

            // Screen the comment. A harsh review is fine; abuse and spam are not.
            if (!string.IsNullOrWhiteSpace(dto.Comment))
            {
                var moderation = await _content.ScreenAsync(dto.Comment, "review of a tradesperson", cancellationToken);
                if (!moderation.Allowed)
                    return BadRequest(new { message = moderation.Reason });
            }

            var review = new Review
            {
                JobId = job.Id,
                ReviewerId = job.CustomerId,
                VendorProfileId = job.VendorProfileId,
                Rating = dto.Rating,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync(cancellationToken);

            await RecalculateVendorRatingAsync(job.AssignedVendor, cancellationToken);

            await _notifications.NotifyUserAsync(
                job.AssignedVendor.UserId,
                "New Review",
                $"You received a {review.Rating}-star review for \"{job.Title}\".",
                $"/jobs/{job.Id}",
                cancellationToken);

            return Ok(await MapToDtoAsync(review.Id, cancellationToken));
        }

        // GET: api/reviews/vendor/{vendorProfileId}
        // Public-facing reputation: the rollup plus the reviews behind it.
        [HttpGet("vendor/{vendorProfileId}")]
        public async Task<IActionResult> GetVendorReviews(string vendorProfileId, CancellationToken cancellationToken)
        {
            var vendor = await _context.VendorProfiles
                .FirstOrDefaultAsync(v => v.Id == vendorProfileId, cancellationToken);

            if (vendor == null)
                return NotFound(new { message = "Vendor not found." });

            var reviews = await ReviewQuery()
                .Where(r => r.VendorProfileId == vendorProfileId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(cancellationToken);

            return Ok(new VendorRatingDto
            {
                VendorProfileId = vendor.Id,
                CompanyName = vendor.CompanyName,
                AverageRating = vendor.AverageRating,
                TotalReviews = vendor.TotalReviews,
                ReputationSummary = vendor.ReputationSummary,
                Reviews = reviews.Select(MapToDto).ToList()
            });
        }

        // GET: api/reviews/job/{jobId}
        [HttpGet("job/{jobId}")]
        public async Task<IActionResult> GetJobReview(int jobId, CancellationToken cancellationToken)
        {
            var review = await ReviewQuery().FirstOrDefaultAsync(r => r.JobId == jobId, cancellationToken);
            if (review == null)
                return NotFound(new { message = "This job has not been reviewed." });

            return Ok(MapToDto(review));
        }

        /// <summary>
        /// Recomputes the vendor's rating rollup from every review they hold, then refreshes the
        /// AI blurb. The blurb is best-effort: an AI outage must not cost the customer their review.
        /// </summary>
        private async Task RecalculateVendorRatingAsync(VendorProfile vendor, CancellationToken cancellationToken)
        {
            var ratings = await _context.Reviews
                .Where(r => r.VendorProfileId == vendor.Id)
                .Select(r => new { r.Rating, r.Comment, r.CreatedAt })
                .ToListAsync(cancellationToken);

            vendor.TotalReviews = ratings.Count;
            vendor.AverageRating = ratings.Count == 0
                ? 0
                : Math.Round(ratings.Average(r => r.Rating), 2);

            // The blurb only needs refreshing when enough has changed to move the story.
            var newSinceSummary = vendor.TotalReviews - vendor.ReputationSummaryReviewCount;
            var summaryIsStale = string.IsNullOrEmpty(vendor.ReputationSummary) || newSinceSummary >= SummaryStaleAfter;

            try
            {
                if (_aiService.IsConfigured && ratings.Count > 0 && summaryIsStale)
                {
                    var recent = ratings
                        .OrderByDescending(r => r.CreatedAt)
                        .Take(SummaryWindow)
                        .Select(r => (r.Rating, r.Comment));

                    vendor.ReputationSummary = await _aiService.SummarizeReviewsAsync(
                        vendor.CompanyName, recent, cancellationToken);
                    vendor.ReputationSummaryReviewCount = vendor.TotalReviews;
                }
            }
            catch (AiUnavailableException ex)
            {
                // Keep the numbers, drop the blurb. The next review will try again.
                _logger.LogWarning(ex, "Could not refresh the reputation summary for vendor {VendorId}.", vendor.Id);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        private IQueryable<Review> ReviewQuery() =>
            _context.Reviews
                .Include(r => r.Job)
                .Include(r => r.Vendor)
                .Include(r => r.Reviewer)
                    .ThenInclude(c => c.User);

        private async Task<ReviewDto> MapToDtoAsync(int reviewId, CancellationToken cancellationToken)
        {
            var review = await ReviewQuery().FirstAsync(r => r.Id == reviewId, cancellationToken);
            return MapToDto(review);
        }

        private static ReviewDto MapToDto(Review r) => new()
        {
            Id = r.Id,
            JobId = r.JobId,
            JobTitle = r.Job?.Title ?? string.Empty,
            ReviewerName = r.Reviewer?.User?.FullName ?? "Unknown",
            VendorProfileId = r.VendorProfileId,
            VendorCompany = r.Vendor?.CompanyName ?? "Unknown",
            Rating = r.Rating,
            Comment = r.Comment,
            CreatedAt = r.CreatedAt
        };
    }
}
