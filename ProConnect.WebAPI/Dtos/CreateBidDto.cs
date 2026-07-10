namespace ProConnect.WebAPI.Dtos
{
    public class CreateBidDto
    {
        public decimal BidAmount { get; set; }
        public string? ProposalMessage { get; set; }
        public int EstimatedDays { get; set; }
    }
}