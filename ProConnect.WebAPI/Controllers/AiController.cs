using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProConnect.WebAPI;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Dtos;
using ProConnect.WebAPI.Services;

namespace ProConnect.WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Ai)] // these endpoints spend Gemini quota
    public class AiController : ControllerBase
    {
        private const long MaxImageBytes = 5 * 1024 * 1024; // keep in step with the client-side limit

        /// <summary>How many recent completed prices anchor a budget estimate.</summary>
        private const int PriceHistoryWindow = 20;

        private readonly AiService _aiService;
        private readonly ImageStorageService _imageStorage;
        private readonly ApplicationDbContext _context;

        public AiController(AiService aiService, ImageStorageService imageStorage, ApplicationDbContext context)
        {
            _aiService = aiService;
            _imageStorage = imageStorage;
            _context = context;
        }

        // POST: api/ai/analyze-image
        [HttpPost("analyze-image")]
        [RequestSizeLimit(MaxImageBytes)]
        public async Task<IActionResult> AnalyzeImage(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            if (file.Length > MaxImageBytes)
                return BadRequest(new { message = "Image exceeds the 5 MB limit." });

            if (string.IsNullOrEmpty(file.ContentType) ||
                !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Only image files can be analyzed." });
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, cancellationToken);

            try
            {
                var analysis = await _aiService.AnalyzeImageAsync(
                    memoryStream.ToArray(), file.ContentType, cancellationToken);

                // Keep the photo: the vendor needs to see what they are bidding on.
                analysis.ImageUrl = await _imageStorage.SaveAsync(file, cancellationToken);

                return Ok(analysis);
            }
            catch (AiUnavailableException ex)
            {
                // The provider failed, not the caller: report it as a gateway error with a usable message.
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        // POST: api/ai/improve-description
        [HttpPost("improve-description")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> ImproveDescription(
            [FromBody] ImproveDescriptionRequestDto dto,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(dto.Description))
                return BadRequest(new { message = "Write a rough description first, then let the AI polish it." });

            try
            {
                var improved = await _aiService.ImproveDescriptionAsync(
                    dto.Description, dto.Title, dto.Category, cancellationToken);

                return Ok(new ImproveDescriptionDto { ImprovedDescription = improved });
            }
            catch (AiUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        // POST: api/ai/estimate-budget
        // Anchored in what jobs in this category actually completed for on the platform.
        [HttpPost("estimate-budget")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> EstimateBudget(
            [FromBody] EstimateBudgetRequestDto dto,
            CancellationToken cancellationToken)
        {
            var category = await _context.ServiceCategories
                .FirstOrDefaultAsync(c => c.Id == dto.ServiceCategoryId, cancellationToken);

            if (category == null)
                return BadRequest(new { message = "Pick a service category first." });

            // Real money, from real completed bookings in this category.
            var history = await _context.Bookings
                .Where(b => b.Status == "Completed" && b.Job.ServiceCategoryId == dto.ServiceCategoryId)
                .OrderByDescending(b => b.CompletedAt)
                .Select(b => b.TotalPrice)
                .Take(PriceHistoryWindow)
                .ToListAsync(cancellationToken);

            try
            {
                var estimate = await _aiService.EstimateBudgetAsync(
                    dto.Title ?? string.Empty, dto.Description, category.Name, history, cancellationToken);

                return Ok(estimate);
            }
            catch (AiUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        // POST: api/ai/write-proposal
        [HttpPost("write-proposal")]
        [Authorize(Roles = "Vendor")]
        public async Task<IActionResult> WriteProposal(
            [FromBody] WriteProposalRequestDto dto,
            CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var job = await _context.Jobs
                .Include(j => j.ServiceCategory)
                .FirstOrDefaultAsync(j => j.Id == dto.JobId, cancellationToken);

            if (job == null)
                return NotFound(new { message = "Job not found." });

            var vendor = await _context.VendorProfiles
                .FirstOrDefaultAsync(v => v.UserId == userId, cancellationToken);

            try
            {
                var proposal = await _aiService.WriteProposalAsync(
                    job.Title,
                    job.Description,
                    job.ServiceCategory?.Name ?? "General",
                    dto.Notes,
                    vendor?.Skills,
                    dto.BidAmount,
                    dto.EstimatedDays,
                    cancellationToken);

                return Ok(new ProposalDto { Proposal = proposal });
            }
            catch (AiUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        // POST: api/ai/evaluate-bid
        [HttpPost("evaluate-bid")]
        [Authorize(Roles = "Vendor")]
        public async Task<IActionResult> EvaluateBid(
            [FromBody] EvaluateBidRequestDto dto,
            CancellationToken cancellationToken)
        {
            var job = await _context.Jobs
                .Include(j => j.ServiceCategory)
                .FirstOrDefaultAsync(j => j.Id == dto.JobId, cancellationToken);

            if (job == null)
                return NotFound(new { message = "Job not found." });

            try
            {
                var evaluation = await _aiService.EvaluateBidAsync(
                    job.Title,
                    job.Description,
                    job.ServiceCategory?.Name ?? "General",
                    job.BudgetMin,
                    job.BudgetMax,
                    dto.BidAmount,
                    dto.EstimatedDays,
                    cancellationToken);

                return Ok(evaluation);
            }
            catch (AiUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }
    }
}
