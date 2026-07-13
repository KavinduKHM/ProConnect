using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Dtos;

namespace ProConnect.WebAPI.Services
{
    /// <summary>
    /// Picks the vendors who should hear about a job. Narrows the field in SQL (category, skills,
    /// availability), then lets the AI rank what is left. Falls back to a deterministic ranking so a
    /// job posting never fails just because the AI is down.
    /// </summary>
    public class VendorMatchingService
    {
        /// <summary>Ceiling on how many vendors we hand to the model — keeps the prompt (and cost) bounded.</summary>
        private const int MaxCandidates = 25;

        private readonly ApplicationDbContext _context;
        private readonly AiService _aiService;
        private readonly ILogger<VendorMatchingService> _logger;

        public VendorMatchingService(
            ApplicationDbContext context,
            AiService aiService,
            ILogger<VendorMatchingService> logger)
        {
            _context = context;
            _aiService = aiService;
            _logger = logger;
        }

        public async Task<List<VendorMatchDto>> MatchAsync(Job job, int take, CancellationToken cancellationToken = default)
        {
            var candidates = await FindCandidatesAsync(job, cancellationToken);
            if (candidates.Count == 0)
            {
                return new List<VendorMatchDto>();
            }

            try
            {
                if (_aiService.IsConfigured)
                {
                    var matches = await _aiService.MatchVendorsAsync(
                        job.Title,
                        job.Description,
                        job.ServiceCategory?.Name ?? "General",
                        job.Location,
                        candidates,
                        take,
                        cancellationToken);

                    if (matches.Count > 0)
                    {
                        return matches;
                    }
                }
            }
            catch (AiUnavailableException ex)
            {
                _logger.LogWarning(ex, "AI matching unavailable for job {JobId}; falling back to rating order.", job.Id);
            }

            return FallbackRanking(candidates, take);
        }

        /// <summary>
        /// The shortlist the AI gets to judge: available vendors who either work in this category or
        /// have not declared any category yet (so vendors who signed up before skills existed are not shut out).
        /// </summary>
        private async Task<List<VendorMatchDto>> FindCandidatesAsync(Job job, CancellationToken cancellationToken)
        {
            var vendors = await _context.VendorProfiles
                .Include(v => v.ServiceCategories)
                .Where(v => v.IsAvailable)
                .Where(v => !v.ServiceCategories.Any() ||
                            v.ServiceCategories.Any(c => c.Id == job.ServiceCategoryId))
                .OrderByDescending(v => v.ServiceCategories.Any(c => c.Id == job.ServiceCategoryId))
                .ThenByDescending(v => v.AverageRating)
                .ThenByDescending(v => v.TotalReviews)
                .Take(MaxCandidates)
                .ToListAsync(cancellationToken);

            return vendors.Select(ToDto).ToList();
        }

        /// <summary>Rating-ordered ranking, used when the AI cannot be reached.</summary>
        private static List<VendorMatchDto> FallbackRanking(List<VendorMatchDto> candidates, int take) =>
            candidates
                .OrderByDescending(c => c.AverageRating)
                .ThenByDescending(c => c.TotalReviews)
                .Take(take)
                .Select(c =>
                {
                    c.Score = 0;
                    c.Reason = "Ranked by rating (AI matching unavailable).";
                    return c;
                })
                .ToList();

        private static VendorMatchDto ToDto(VendorProfile v) => new()
        {
            VendorProfileId = v.Id,
            CompanyName = v.CompanyName,
            Skills = v.Skills,
            AverageRating = v.AverageRating,
            TotalReviews = v.TotalReviews,
            ReputationSummary = v.ReputationSummary
        };

        /// <summary>The Identity user ids behind a set of matches, for notifications.</summary>
        public async Task<List<string>> ResolveUserIdsAsync(
            IEnumerable<VendorMatchDto> matches,
            CancellationToken cancellationToken = default)
        {
            var ids = matches.Select(m => m.VendorProfileId).ToList();
            return await _context.VendorProfiles
                .Where(v => ids.Contains(v.Id))
                .Select(v => v.UserId)
                .ToListAsync(cancellationToken);
        }
    }
}
