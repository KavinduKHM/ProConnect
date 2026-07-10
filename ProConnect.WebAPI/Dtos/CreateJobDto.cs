namespace ProConnect.WebAPI.Dtos
{
    public class CreateJobDto
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public int ServiceCategoryId { get; set; }
        public string? Location { get; set; }
        public decimal BudgetMin { get; set; }
        public decimal BudgetMax { get; set; }
        public DateTime PreferredDate { get; set; }
        public bool IsUrgent { get; set; } = false;
    }
}