using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Dtos;
using System.Security.Claims;

namespace ProConnect.WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // All endpoints require authentication
    public class JobsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public JobsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
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
            var customer = await _context.CustomerProfiles.FirstOrDefaultAsync(c => c.UserId == userId);
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
                .Where(b => b.JobId == id)
                .ToListAsync();

            var response = bids.Select(b => new BidResponseDto
            {
                Id = b.Id,
                BidAmount = b.BidAmount,
                ProposalMessage = b.ProposalMessage,
                EstimatedDays = b.EstimatedDays,
                Status = b.Status,
                VendorName = b.Vendor.User.FullName,
                VendorCompany = b.Vendor.CompanyName,
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