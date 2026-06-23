namespace ProConnect.Domain.Entities
{
    public class JobBid
    {
        public int Id { get; set; }

        public int JobId { get; set; }
        public Job Job { get; set; } = null!;

        public string VendorProfileId { get; set; } = string.Empty; // Matches VendorProfile.Id
        public VendorProfile Vendor { get; set; } = null!;

        public decimal BidAmount { get; set; }
        public string? ProposalMessage { get; set; }
        public int EstimatedDays { get; set; }

        public string Status { get; set; } = "Pending"; // Pending, Accepted, Rejected, Withdrawn

        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}