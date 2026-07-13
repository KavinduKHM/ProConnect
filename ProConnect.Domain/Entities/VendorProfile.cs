using Microsoft.AspNetCore.Identity;

namespace ProConnect.Domain.Entities
{
    public class VendorProfile
    {
        // Use string Id matching ApplicationUser.Id
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public string CompanyName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Website { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public bool IsVerified { get; set; } = false;
        public double AverageRating { get; set; } = 0;
        public int TotalReviews { get; set; } = 0;

        /// <summary>Free-text skills the vendor declares at registration, e.g. "leak repair, pipe fitting, water heaters".</summary>
        public string? Skills { get; set; }

        // One-line AI blurb distilled from this vendor's reviews.
        public string? ReputationSummary { get; set; }

        /// <summary>Review count the summary was built from, so we don't re-summarize on every single review.</summary>
        public int ReputationSummaryReviewCount { get; set; } = 0;

        public bool IsAvailable { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        // Navigation properties
        public ICollection<ServiceCategory> ServiceCategories { get; set; } = new List<ServiceCategory>();
        public ICollection<Job> Jobs { get; set; } = new List<Job>();
        public ICollection<JobBid> Bids { get; set; } = new List<JobBid>();
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public ICollection<Review> ReviewsReceived { get; set; } = new List<Review>();
    }
}