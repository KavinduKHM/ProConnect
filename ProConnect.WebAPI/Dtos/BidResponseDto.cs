namespace ProConnect.WebAPI.Dtos
{
    public class BidResponseDto
    {
        public int Id { get; set; }
        public decimal BidAmount { get; set; }
        public string? ProposalMessage { get; set; }
        public int EstimatedDays { get; set; }
        public string Status { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string VendorCompany { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}