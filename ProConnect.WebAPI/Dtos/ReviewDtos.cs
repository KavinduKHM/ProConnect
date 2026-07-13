using System;
using System.ComponentModel.DataAnnotations;

namespace ProConnect.WebAPI.Dtos
{
    public class CreateReviewDto
    {
        [Required]
        public int JobId { get; set; }

        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }
    }

    public class ReviewDto
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public string JobTitle { get; set; } = string.Empty;
        public string ReviewerName { get; set; } = string.Empty;
        public string VendorProfileId { get; set; } = string.Empty;
        public string VendorCompany { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Rating rollup for a vendor, including the AI-written reputation blurb.</summary>
    public class VendorRatingDto
    {
        public string VendorProfileId { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public double AverageRating { get; set; }
        public int TotalReviews { get; set; }
        public string? ReputationSummary { get; set; }
        public List<ReviewDto> Reviews { get; set; } = new();
    }
}
