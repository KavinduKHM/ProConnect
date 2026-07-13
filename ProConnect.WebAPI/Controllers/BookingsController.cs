using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Dtos;
using ProConnect.WebAPI.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace ProConnect.WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BookingsController : ControllerBase
    {
        private const long MaxImageBytes = 5 * 1024 * 1024;

        private readonly ApplicationDbContext _context;
        private readonly NotificationService _notifications;
        private readonly ImageStorageService _imageStorage;
        private readonly AiService _aiService;
        private readonly ILogger<BookingsController> _logger;

        public BookingsController(
            ApplicationDbContext context,
            NotificationService notifications,
            ImageStorageService imageStorage,
            AiService aiService,
            ILogger<BookingsController> logger)
        {
            _context = context;
            _notifications = notifications;
            _imageStorage = imageStorage;
            _aiService = aiService;
            _logger = logger;
        }

        // GET: api/bookings
        // Returns the caller's bookings, whichever side of the job they are on.
        [HttpGet]
        public async Task<IActionResult> GetMyBookings(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var bookings = await BaseQuery()
                .Where(b => b.Customer.UserId == userId || b.Vendor.UserId == userId)
                .OrderByDescending(b => b.ScheduledDate)
                .ToListAsync(cancellationToken);

            return Ok(bookings.Select(MapToDto).ToList());
        }

        // GET: api/bookings/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetBooking(int id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var booking = await BaseQuery().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (booking == null)
                return NotFound(new { message = "Booking not found." });

            if (booking.Customer.UserId != userId && booking.Vendor.UserId != userId)
                return Forbid();

            return Ok(MapToDto(booking));
        }

        // POST: api/bookings/{id}/start
        // The assigned vendor marks the work as under way.
        [HttpPost("{id}/start")]
        [Authorize(Roles = "Vendor")]
        public async Task<IActionResult> StartBooking(int id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var booking = await BaseQuery().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (booking == null)
                return NotFound(new { message = "Booking not found." });

            if (booking.Vendor.UserId != userId)
                return Forbid();

            if (booking.Status != "Scheduled")
                return BadRequest(new { message = $"This booking cannot be started because it is {booking.Status}." });

            booking.Status = "InProgress";
            booking.StartTime = DateTime.UtcNow;
            booking.Job.Status = "InProgress";

            await _context.SaveChangesAsync(cancellationToken);

            await _notifications.NotifyUserAsync(
                booking.Customer.UserId,
                "Work Started",
                $"{booking.Vendor.CompanyName} has started work on \"{booking.Job.Title}\".",
                $"/jobs/{booking.JobId}",
                cancellationToken);

            return Ok(MapToDto(booking));
        }

        // POST: api/bookings/{id}/complete
        // The assigned vendor marks the work as done. This is what unlocks the review.
        // An optional "after" photo is compared against the customer's original by the AI.
        [HttpPost("{id}/complete")]
        [Authorize(Roles = "Vendor")]
        [RequestSizeLimit(MaxImageBytes)]
        public async Task<IActionResult> CompleteBooking(
            int id,
            IFormFile? completionPhoto,
            CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var booking = await BaseQuery().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (booking == null)
                return NotFound(new { message = "Booking not found." });

            if (booking.Vendor.UserId != userId)
                return Forbid();

            if (booking.Status is not ("Scheduled" or "InProgress"))
                return BadRequest(new { message = $"This booking cannot be completed because it is {booking.Status}." });

            if (completionPhoto is { Length: > 0 })
            {
                if (string.IsNullOrEmpty(completionPhoto.ContentType) ||
                    !completionPhoto.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "The completion photo must be an image." });
                }

                await AttachCompletionPhotoAsync(booking.Job, completionPhoto, cancellationToken);
            }

            var now = DateTime.UtcNow;
            booking.Status = "Completed";
            booking.EndTime = now;
            booking.CompletedAt = now;
            booking.Job.Status = "Completed";
            booking.Job.CompletedAt = now;

            await _context.SaveChangesAsync(cancellationToken);

            await _notifications.NotifyUserAsync(
                booking.Customer.UserId,
                "Job Completed",
                $"\"{booking.Job.Title}\" is complete. Leave {booking.Vendor.CompanyName} a review.",
                $"/jobs/{booking.JobId}",
                cancellationToken);

            return Ok(MapToDto(booking));
        }

        /// <summary>
        /// Stores the "after" photo and, when we still have the customer's "before" photo, asks the AI
        /// what changed between them. Best-effort: a failed comparison must not block completion.
        /// </summary>
        private async Task AttachCompletionPhotoAsync(Job job, IFormFile photo, CancellationToken cancellationToken)
        {
            job.CompletionImageUrl = await _imageStorage.SaveAsync(photo, cancellationToken);

            var beforeImage = await _imageStorage.ReadAsync(job.ImageUrl, cancellationToken);
            if (beforeImage == null || !_aiService.IsConfigured)
            {
                return; // no original to compare against
            }

            try
            {
                using var afterStream = new MemoryStream();
                await photo.CopyToAsync(afterStream, cancellationToken);

                var check = await _aiService.VerifyCompletionAsync(
                    job.Title,
                    job.Description,
                    beforeImage,
                    _imageStorage.MimeTypeFor(job.ImageUrl),
                    afterStream.ToArray(),
                    photo.ContentType,
                    cancellationToken);

                job.CompletionVerdict = $"{check.Verdict}: {check.Summary}";
            }
            catch (AiUnavailableException ex)
            {
                _logger.LogWarning(ex, "Could not verify the completion photo for job {JobId}.", job.Id);
            }
        }

        // POST: api/bookings/{id}/cancel
        // Either side can call off work that has not been completed.
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelBooking(int id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var booking = await BaseQuery().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (booking == null)
                return NotFound(new { message = "Booking not found." });

            var isCustomer = booking.Customer.UserId == userId;
            var isVendor = booking.Vendor.UserId == userId;
            if (!isCustomer && !isVendor)
                return Forbid();

            if (booking.Status == "Completed")
                return BadRequest(new { message = "A completed booking cannot be cancelled." });

            if (booking.Status == "Cancelled")
                return BadRequest(new { message = "This booking is already cancelled." });

            booking.Status = "Cancelled";
            booking.Job.Status = "Cancelled";

            await _context.SaveChangesAsync(cancellationToken);

            // Tell the other party, not the person who just clicked cancel.
            var recipientId = isCustomer ? booking.Vendor.UserId : booking.Customer.UserId;
            await _notifications.NotifyUserAsync(
                recipientId,
                "Booking Cancelled",
                $"The booking for \"{booking.Job.Title}\" was cancelled.",
                $"/jobs/{booking.JobId}",
                cancellationToken);

            return Ok(MapToDto(booking));
        }

        private IQueryable<Booking> BaseQuery() =>
            _context.Bookings
                .Include(b => b.Job)
                    .ThenInclude(j => j.Review)
                .Include(b => b.Customer)
                    .ThenInclude(c => c.User)
                .Include(b => b.Vendor)
                    .ThenInclude(v => v.User);

        private static BookingDto MapToDto(Booking b) => new()
        {
            Id = b.Id,
            JobId = b.JobId,
            JobTitle = b.Job?.Title ?? string.Empty,
            CustomerName = b.Customer?.User?.FullName ?? "Unknown",
            VendorName = b.Vendor?.User?.FullName ?? "Unknown",
            VendorCompany = b.Vendor?.CompanyName ?? "Unknown",
            ScheduledDate = b.ScheduledDate,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            Status = b.Status,
            TotalPrice = b.TotalPrice,
            IsPaid = b.IsPaid,
            Notes = b.Notes,
            CreatedAt = b.CreatedAt,
            CompletedAt = b.CompletedAt,
            HasReview = b.Job?.Review != null
        };
    }
}
