namespace ProConnect.Domain.Entities
{
    public class Review
    {
        public int Id { get; set; }

        public int JobId { get; set; }
        public Job Job { get; set; } = null!;

        public string ReviewerId { get; set; } = string.Empty; // Matches CustomerProfile.Id
        public CustomerProfile Reviewer { get; set; } = null!;

        public string VendorProfileId { get; set; } = string.Empty; // Matches VendorProfile.Id
        public VendorProfile Vendor { get; set; } = null!;

        public int Rating { get; set; } // 1 to 5
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public string? AiSummary { get; set; }
    }
}