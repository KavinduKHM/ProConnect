using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProConnect.WebAPI.Dtos
{
    // ---------------------------------------------------------------- description rewrite

    public class ImproveDescriptionRequestDto
    {
        [Required]
        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        /// <summary>Optional context so the rewrite stays on-topic.</summary>
        public string? Title { get; set; }
        public string? Category { get; set; }
    }

    public class ImproveDescriptionDto
    {
        public string ImprovedDescription { get; set; } = string.Empty;
    }

    // ---------------------------------------------------------------- bid evaluation

    public class EvaluateBidRequestDto
    {
        [Required]
        public int JobId { get; set; }

        [Range(0.01, 1_000_000)]
        public decimal BidAmount { get; set; }

        public int EstimatedDays { get; set; }
    }

    /// <summary>Sanity check of a bid against the AI's own read of the job.</summary>
    public class BidEvaluationDto
    {
        /// <summary>TooLow | Fair | TooHigh</summary>
        public string Verdict { get; set; } = "Fair";

        public string Reason { get; set; } = string.Empty;

        public decimal? SuggestedMin { get; set; }
        public decimal? SuggestedMax { get; set; }
    }

    // ---------------------------------------------------------------- grounded budget estimate

    public class EstimateBudgetRequestDto
    {
        [Required]
        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        public string? Title { get; set; }

        [Required]
        public int ServiceCategoryId { get; set; }
    }

    /// <summary>A budget range anchored in what jobs like this actually cost on the platform.</summary>
    public class BudgetEstimateDto
    {
        public decimal EstimatedMin { get; set; }
        public decimal EstimatedMax { get; set; }
        public string Rationale { get; set; } = string.Empty;

        /// <summary>How many completed bookings backed the estimate; 0 means the AI had no history to lean on.</summary>
        public int BasedOnJobs { get; set; }
    }

    // ---------------------------------------------------------------- proposal writer

    public class WriteProposalRequestDto
    {
        [Required]
        public int JobId { get; set; }

        /// <summary>The vendor's rough notes / bullet points.</summary>
        [Required]
        [MaxLength(1500)]
        public string Notes { get; set; } = string.Empty;

        public decimal? BidAmount { get; set; }
        public int? EstimatedDays { get; set; }
    }

    public class ProposalDto
    {
        public string Proposal { get; set; } = string.Empty;
    }

    // ---------------------------------------------------------------- vendor matching

    /// <summary>A vendor the AI thinks fits the job, with its reasoning.</summary>
    public class VendorMatchDto
    {
        public string VendorProfileId { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string? Skills { get; set; }
        public double AverageRating { get; set; }
        public int TotalReviews { get; set; }
        public string? ReputationSummary { get; set; }

        /// <summary>0-100 fit score.</summary>
        public int Score { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    // ---------------------------------------------------------------- bid ranking

    public class RankedBidDto
    {
        public int BidId { get; set; }
        public int Rank { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class BidRankingDto
    {
        public int? RecommendedBidId { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<RankedBidDto> Ranking { get; set; } = new();
    }

    // ---------------------------------------------------------------- moderation

    /// <summary>Verdict on a piece of user-supplied free text.</summary>
    public class ModerationDto
    {
        public bool Allowed { get; set; } = true;

        /// <summary>Clean | Abusive | Spam | ContactDetails | Nonsense</summary>
        public string Category { get; set; } = "Clean";

        public string Reason { get; set; } = string.Empty;
    }

    // ---------------------------------------------------------------- triage (moderation + translation)

    /// <summary>
    /// Moderation and translation in one answer. Both read the same text, so asking the model twice
    /// would double the cost of posting a job for no benefit.
    /// </summary>
    public class ContentTriageDto
    {
        public bool Allowed { get; set; } = true;

        /// <summary>Clean | Abusive | Spam | ContactDetails | Nonsense</summary>
        public string Category { get; set; } = "Clean";

        /// <summary>Shown to the user when the content is rejected.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>English name of the detected language, e.g. "English", "Sinhala", "Tamil".</summary>
        public string Language { get; set; } = "English";

        public bool IsEnglish { get; set; } = true;

        /// <summary>The text in English; equal to the input when it was already English.</summary>
        public string English { get; set; } = string.Empty;
    }

    // ---------------------------------------------------------------- translation

    public class TranslationDto
    {
        /// <summary>English name of the detected language, e.g. "English", "Sinhala", "Tamil".</summary>
        public string Language { get; set; } = "English";

        public bool IsEnglish { get; set; } = true;

        /// <summary>The text in English. Equal to the input when it was already English.</summary>
        public string English { get; set; } = string.Empty;
    }

    // ---------------------------------------------------------------- completion verification

    /// <summary>The AI's read of the vendor's "after" photo against the customer's original.</summary>
    public class CompletionCheckDto
    {
        /// <summary>Resolved | Unclear | NotResolved</summary>
        public string Verdict { get; set; } = "Unclear";

        public string Summary { get; set; } = string.Empty;
    }
}
