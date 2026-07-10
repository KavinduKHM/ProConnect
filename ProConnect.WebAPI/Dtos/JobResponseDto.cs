namespace ProConnect.WebAPI.Dtos
{
    public class JobResponseDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string ServiceCategoryName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? Location { get; set; }
        public decimal BudgetMin { get; set; }
        public decimal BudgetMax { get; set; }
        public DateTime PreferredDate { get; set; }
        public bool IsUrgent { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? AssignedVendorName { get; set; }
        public int BidCount { get; set; }
    }
}