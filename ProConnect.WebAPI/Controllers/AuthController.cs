using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Helpers;

namespace ProConnect.WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _configuration = configuration;
        }

        // Models for registration and login
        public class RegisterModel
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Role { get; set; } = "Customer"; // Customer, Vendor
            public string? CompanyName { get; set; } // Required for Vendor
            public string? PhoneNumber { get; set; }
            public string? Address { get; set; }

            /// <summary>Vendor only: free-text skills, e.g. "leak repair, pipe fitting". Drives job matching.</summary>
            public string? Skills { get; set; }

            /// <summary>Vendor only: the service categories they work in. Drives job matching.</summary>
            public List<int>? ServiceCategoryIds { get; set; }
        }

        public class LoginModel
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        // POST: api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            // Validate role
            if (model.Role != "Customer" && model.Role != "Vendor")
            {
                return BadRequest(new { message = "Role must be either 'Customer' or 'Vendor'" });
            }

            // Check if user exists
            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                return BadRequest(new { message = "User with this email already exists" });
            }

            // Create the user
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                IsVendor = (model.Role == "Vendor"),
                PhoneNumber = model.PhoneNumber
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                return BadRequest(new { message = "Registration failed", errors = result.Errors });
            }

            // Add to role
            await _userManager.AddToRoleAsync(user, model.Role);

            // Create VendorProfile if role is Vendor
            if (model.Role == "Vendor")
            {
                if (string.IsNullOrWhiteSpace(model.CompanyName))
                {
                    // Delete the user if no company name provided
                    await _userManager.DeleteAsync(user);
                    return BadRequest(new { message = "Company name is required for Vendor registration" });
                }

                var vendorProfile = new VendorProfile
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = user.Id,
                    CompanyName = model.CompanyName,
                    PhoneNumber = model.PhoneNumber,
                    Address = model.Address,
                    Skills = model.Skills,
                    IsVerified = false,
                    IsAvailable = true,
                    CreatedAt = DateTime.UtcNow
                };

                // The categories a vendor works in are what job matching filters on first.
                if (model.ServiceCategoryIds is { Count: > 0 })
                {
                    var categories = await _context.ServiceCategories
                        .Where(c => model.ServiceCategoryIds.Contains(c.Id))
                        .ToListAsync();

                    foreach (var serviceCategory in categories)
                    {
                        vendorProfile.ServiceCategories.Add(serviceCategory);
                    }
                }

                _context.VendorProfiles.Add(vendorProfile);
                await _context.SaveChangesAsync();
            }
            else
            {
                // Create CustomerProfile
                var customerProfile = new CustomerProfile
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = user.Id,
                    PhoneNumber = model.PhoneNumber,
                    Address = model.Address,
                    CreatedAt = DateTime.UtcNow
                };

                _context.CustomerProfiles.Add(customerProfile);
                await _context.SaveChangesAsync();
            }

            // Generate JWT token
            var token = JwtHelper.GenerateToken(
                user.Id,
                user.Email!,
                model.Role,
                _configuration
            );

            return Ok(new
            {
                message = "Registration successful",
                token,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.FullName,
                    user.IsVendor,
                    user.PhoneNumber
                }
            });
        }

        // POST: api/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !await _userManager.CheckPasswordAsync(user, model.Password))
            {
                return Unauthorized(new { message = "Invalid email or password" });
            }

            // Get user roles
            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "Customer";

            // Generate JWT token
            var token = JwtHelper.GenerateToken(
                user.Id,
                user.Email!,
                role,
                _configuration
            );

            return Ok(new
            {
                message = "Login successful",
                token,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.FullName,
                    user.IsVendor,
                    user.PhoneNumber,
                    Role = role
                }
            });
        }

        // GET: api/auth/test
        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new { message = "Auth API is working!" });
        }
    }
}