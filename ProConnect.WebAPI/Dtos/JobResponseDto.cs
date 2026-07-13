namespace ProConnect.WebAPI.Dtos
{
    public class JobResponseDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string ServiceCategoryName { get; set; } = string.Empty;
        public int ServiceCategoryId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? Location { get; set; }
        public decimal BudgetMin { get; set; }
        public decimal BudgetMax { get; set; }
        public DateTime PreferredDate { get; set; }
        public bool IsUrgent { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? AssignedVendorName { get; set; }
        public string? AssignedVendorCompany { get; set; }
        public int BidCount { get; set; }

        // Resolved against the caller, so the UI never has to guess ownership by comparing names.
        public bool IsOwner { get; set; }
        public bool IsAssignedVendor { get; set; }

        // Lifecycle state the UI needs in order to decide which action to offer.
        public int? BookingId { get; set; }
        public string? BookingStatus { get; set; }
        public bool HasReview { get; set; }

        // Set when the post was written in another language and translated for vendors.
        public string? OriginalDescription { get; set; }
        public string? OriginalLanguage { get; set; }

        // The vendor's proof-of-completion photo and the AI's read of it.
        public string? CompletionImageUrl { get; set; }
        public string? CompletionVerdict { get; set; }
    }
}
