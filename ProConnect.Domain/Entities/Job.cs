namespace ProConnect.Domain.Entities
{
    public class Job
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }

        // Relationships - now using string foreign keys
        public int ServiceCategoryId { get; set; }
        public ServiceCategory ServiceCategory { get; set; } = null!;

        public string CustomerId { get; set; } = string.Empty; // Matches CustomerProfile.Id
        public CustomerProfile Customer { get; set; } = null!;

        public string? VendorProfileId { get; set; } // Null until assigned - matches VendorProfile.Id
        public VendorProfile? AssignedVendor { get; set; }

        // Job details
        public string? Location { get; set; }
        public decimal BudgetMin { get; set; }
        public decimal BudgetMax { get; set; }
        public DateTime PreferredDate { get; set; }
        public bool IsUrgent { get; set; } = false;

        public string Status { get; set; } = "Open"; // Open, Bidding, Assigned, InProgress, Completed, Cancelled

        /// <summary>
        /// 768-dim embedding of title+description, stored as a JSON array.
        /// Used for semantic search; null when the AI was unavailable at post time.
        /// </summary>
        public string? Embedding { get; set; }

        /// <summary>The customer's own words, kept when we translate the post into English.</summary>
        public string? OriginalDescription { get; set; }
        public string? OriginalLanguage { get; set; }

        /// <summary>Photo the vendor uploads on completion, and the AI's read of it against the original.</summary>
        public string? CompletionImageUrl { get; set; }
        public string? CompletionVerdict { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Navigation properties
        public ICollection<JobBid> Bids { get; set; } = new List<JobBid>();
        public Booking? Booking { get; set; }
        public Review? Review { get; set; }
    }
}