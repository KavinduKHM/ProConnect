namespace ProConnect.WebAPI.Dtos
{
    public class ImageAnalysisDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? SuggestedCategory { get; set; }
        public bool IsUrgent { get; set; }
        public decimal? EstimatedBudgetMin { get; set; }
        public decimal? EstimatedBudgetMax { get; set; }

        /// <summary>
        /// False when the photo shows nothing a tradesperson could act on. Stops the form being
        /// auto-filled with invented details from a picture of someone's cat.
        /// </summary>
        public bool IsRelevant { get; set; } = true;

        /// <summary>
        /// Where the uploaded photo was stored. The client passes this straight back as
        /// CreateJobDto.ImageUrl so the vendor can see what they are bidding on.
        /// </summary>
        public string? ImageUrl { get; set; }
    }
}
