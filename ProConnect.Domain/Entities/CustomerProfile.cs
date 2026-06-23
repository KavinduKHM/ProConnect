using Microsoft.AspNetCore.Identity;

namespace ProConnect.Domain.Entities
{
    public class CustomerProfile
    {
        // Use string Id matching ApplicationUser.Id
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation properties
        public ICollection<Job> Jobs { get; set; } = new List<Job>();
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public ICollection<Review> ReviewsGiven { get; set; } = new List<Review>();
    }
}