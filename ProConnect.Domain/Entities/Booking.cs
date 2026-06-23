namespace ProConnect.Domain.Entities
{
    public class Booking
    {
        public int Id { get; set; }

        public int JobId { get; set; }
        public Job Job { get; set; } = null!;

        public string CustomerId { get; set; } = string.Empty; // Matches CustomerProfile.Id
        public CustomerProfile Customer { get; set; } = null!;

        public string VendorProfileId { get; set; } = string.Empty; // Matches VendorProfile.Id
        public VendorProfile Vendor { get; set; } = null!;

        public DateTime ScheduledDate { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public string Status { get; set; } = "Scheduled"; // Scheduled, InProgress, Completed, Cancelled, NoShow

        public string? Notes { get; set; }
        public decimal TotalPrice { get; set; }

        public bool IsPaid { get; set; } = false;
        public string? PaymentTransactionId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}