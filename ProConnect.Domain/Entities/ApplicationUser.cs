using Microsoft.AspNetCore.Identity;

namespace ProConnect.Domain.Entities
{
    public class ApplicationUser : IdentityUser
    {
        // Add custom properties here
        public string? FullName { get; set; }
        public string? CompanyName { get; set; } // For vendors
        public bool IsVendor { get; set; } // True = Vendor, False = Customer
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}