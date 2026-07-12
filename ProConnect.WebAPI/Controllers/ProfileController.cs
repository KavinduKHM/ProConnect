using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using System.Security.Claims;

namespace ProConnect.WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public ProfileController(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        // GET: api/Profile/me
        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "Customer";

            var profile = new
            {
                user.Id,
                user.Email,
                user.FullName,
                user.PhoneNumber,
                user.IsVendor,
                Role = role,
                CreatedAt = user.CreatedAt
            };

            if (user.IsVendor)
            {
                var vendor = await _context.VendorProfiles.FirstOrDefaultAsync(v => v.UserId == userId);
                if (vendor != null)
                {
                    return Ok(new
                    {
                        user = profile,
                        vendor = new
                        {
                            vendor.Id,
                            vendor.CompanyName,
                            vendor.Description,
                            vendor.Website,
                            vendor.Address,
                            vendor.ProfilePictureUrl,
                            vendor.IsVerified,
                            vendor.AverageRating,
                            vendor.TotalReviews,
                            vendor.IsAvailable,
                            vendor.CreatedAt
                        }
                    });
                }
            }
            else
            {
                var customer = await _context.CustomerProfiles.FirstOrDefaultAsync(c => c.UserId == userId);
                if (customer != null)
                {
                    return Ok(new
                    {
                        user = profile,
                        customer = new
                        {
                            customer.Id,
                            customer.Address,
                            customer.ProfilePictureUrl,
                            customer.CreatedAt
                        }
                    });
                }
            }

            // Fallback (just user info)
            return Ok(new { user = profile });
        }

        // PUT: api/Profile
        [HttpPut]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            // Update basic user info
            user.FullName = request.FullName ?? user.FullName;
            user.PhoneNumber = request.PhoneNumber ?? user.PhoneNumber;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            // Update role-specific profile
            if (user.IsVendor)
            {
                var vendor = await _context.VendorProfiles.FirstOrDefaultAsync(v => v.UserId == userId);
                if (vendor != null)
                {
                    if (request.CompanyName != null) vendor.CompanyName = request.CompanyName;
                    if (request.Description != null) vendor.Description = request.Description;
                    if (request.Website != null) vendor.Website = request.Website;
                    if (request.Address != null) vendor.Address = request.Address;
                    if (request.IsAvailable.HasValue) vendor.IsAvailable = request.IsAvailable.Value;
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                var customer = await _context.CustomerProfiles.FirstOrDefaultAsync(c => c.UserId == userId);
                if (customer != null)
                {
                    if (request.Address != null) customer.Address = request.Address;
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new { message = "Profile updated successfully." });
        }
    }

    public class UpdateProfileRequest
    {
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CompanyName { get; set; }  // Vendor only
        public string? Description { get; set; }   // Vendor only
        public string? Website { get; set; }       // Vendor only
        public string? Address { get; set; }
        public bool? IsAvailable { get; set; }     // Vendor only
    }
}