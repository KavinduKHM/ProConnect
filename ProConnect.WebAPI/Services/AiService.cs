using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProConnect.WebAPI.Dtos;

namespace ProConnect.WebAPI.Services
{
    /// <summary>Shape Gemini returns for vendor matching; only score and reason are trusted.</summary>
    internal class VendorMatchListDto
    {
        public List<RawVendorMatch> Matches { get; set; } = new();

        internal class RawVendorMatch
        {
            public string? VendorProfileId { get; set; }
            public int Score { get; set; }
            public string? Reason { get; set; }
        }
    }

    public class AiService
    {
        private const string DefaultModel = "gemini-2.5-flash";
        private const string EmbeddingModel = "gemini-embedding-001";

        /// <summary>Smaller than the 3072 default: plenty for search, far cheaper to store per job.</summary>
        private const int EmbeddingDimensions = 768;

        private static readonly JsonSerializerOptions ParseOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        private readonly HttpClient _httpClient;
        private readonly ILogger<AiService> _logger;
        private readonly string? _apiKey;
        private readonly string _model;

        public AiService(HttpClient httpClient, IConfiguration configuration, ILogger<AiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["AiSettings:GeminiApiKey"];
            var configuredModel = configuration["AiSettings:GeminiModel"];
            _model = string.IsNullOrWhiteSpace(configuredModel) ? DefaultModel : configuredModel;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        // ---------------------------------------------------------------- features

        public async Task<ImageAnalysisDto> AnalyzeImageAsync(
            byte[] imageBytes,
            string? mimeType,
            CancellationToken cancellationToken = default)
        {
            const string prompt = @"
                You are a home service assistant. Analyze the image and return a JSON object with:
                - isRelevant: boolean. TRUE only if the image shows a real place, object or problem a
                  tradesperson could work on. FALSE for abstract images, screenshots, memes, people,
                  pets, or anything with no actionable home-service issue.
                - title: short title (max 60 chars)
                - description: detailed description of the issue (max 200 chars)
                - suggestedCategory: one of [Plumbing, Electrical, Painting, Transport, Carpentry, Cleaning, HVAC, Gardening]
                - isUrgent: boolean (true if it looks like an emergency)
                - estimatedBudgetMin: number (minimum reasonable cost)
                - estimatedBudgetMax: number (maximum reasonable cost)
                If isRelevant is false, set the other fields to empty strings or zero and do not invent an issue.
                Only return valid JSON, no extra text.
            ";

            var parts = new object[]
            {
                new { text = prompt },
                new
                {
                    inline_data = new
                    {
                        mime_type = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType,
                        data = Convert.ToBase64String(imageBytes)
                    }
                }
            };

            return await GenerateJsonAsync<ImageAnalysisDto>(parts, "analysis", cancellationToken);
        }

        /// <summary>Rewrites a rough job description into something a vendor can act on.</summary>
        public async Task<string> ImproveDescriptionAsync(
            string description,
            string? title,
            string? category,
            CancellationToken cancellationToken = default)
        {
            var prompt = $@"
                You are helping a homeowner write a clear job request for a tradesperson.
                Rewrite the description below so it is professional, specific and easy to bid on.
                Keep every fact the user gave. Do not invent details, prices or dates.
                Keep it under 200 words and return plain prose only - no JSON, no markdown, no preamble.

                Job title: {title ?? "(not given)"}
                Category: {category ?? "(not given)"}
                Description: {description}
            ";

            var text = await GenerateAsync(new object[] { new { text = prompt } }, jsonMode: false, cancellationToken);
            return text.Trim();
        }

        /// <summary>Checks a bid against the job's own budget and scope.</summary>
        public async Task<BidEvaluationDto> EvaluateBidAsync(
            string jobTitle,
            string jobDescription,
            string category,
            decimal budgetMin,
            decimal budgetMax,
            decimal bidAmount,
            int estimatedDays,
            CancellationToken cancellationToken = default)
        {
            var prompt = $@"
                You are a pricing expert for home services. A vendor wants to bid on this job.

                Title: {jobTitle}
                Category: {category}
                Description: {jobDescription}
                Customer budget range: {budgetMin} to {budgetMax}
                Vendor's bid: {bidAmount} over {estimatedDays} day(s)

                Judge whether the bid is reasonable for the work described.
                Return a JSON object with:
                - verdict: exactly one of ""TooLow"", ""Fair"", ""TooHigh""
                - reason: one short sentence (max 140 chars) explaining the verdict to the vendor
                - suggestedMin: number, the low end of a fair price for this work
                - suggestedMax: number, the high end of a fair price for this work
                Treat a bid far under the customer's budget as TooLow only if the work plausibly costs more.
                Only return valid JSON, no extra text.
            ";

            var evaluation = await GenerateJsonAsync<BidEvaluationDto>(
                new object[] { new { text = prompt } }, "bid evaluation", cancellationToken);

            // Never let a hallucinated verdict through to the UI.
            if (evaluation.Verdict is not ("TooLow" or "Fair" or "TooHigh"))
            {
                evaluation.Verdict = "Fair";
            }
            return evaluation;
        }

        /// <summary>Distils a vendor's reviews into a one-line reputation blurb.</summary>
        public async Task<string> SummarizeReviewsAsync(
            string companyName,
            IEnumerable<(int Rating, string? Comment)> reviews,
            CancellationToken cancellationToken = default)
        {
            var lines = reviews
                .Select(r => $"- {r.Rating}/5: {(string.IsNullOrWhiteSpace(r.Comment) ? "(no comment)" : r.Comment)}")
                .ToList();

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            var prompt = $@"
                Summarize the reputation of the home-service vendor ""{companyName}"" from these customer reviews.
                Write ONE sentence, max 120 characters, in a neutral factual tone.
                Mention the strongest recurring theme (good or bad). Do not invent anything not in the reviews.
                Return plain text only - no JSON, no markdown, no quotes.

                Reviews:
                {string.Join("\n", lines)}
            ";

            var text = await GenerateAsync(new object[] { new { text = prompt } }, jsonMode: false, cancellationToken);
            return text.Trim().Trim('"');
        }

        /// <summary>
        /// Estimates a budget, anchored in what jobs like this actually completed for on the
        /// platform. Pass the real prices from Bookings so the model is not guessing from thin air.
        /// </summary>
        public async Task<BudgetEstimateDto> EstimateBudgetAsync(
            string title,
            string description,
            string category,
            IReadOnlyList<decimal> historicalPrices,
            CancellationToken cancellationToken = default)
        {
            var history = historicalPrices.Count == 0
                ? "No completed jobs in this category yet. Use general market knowledge and say so."
                : $"Actual completed prices for {category} jobs on this platform: " +
                  $"{string.Join(", ", historicalPrices.Select(p => p.ToString("0.##")))}. " +
                  $"These are real transactions - weight them heavily.";

            var prompt = $@"
                You are a pricing analyst for a home-services marketplace.

                Job title: {title}
                Category: {category}
                Description: {description}

                {history}

                Return a JSON object with:
                - estimatedMin: number, the low end of a realistic budget
                - estimatedMax: number, the high end of a realistic budget
                - rationale: one sentence (max 160 chars) telling the customer how you arrived at this,
                  referring to the platform's own completed prices when they were given.
                Only return valid JSON, no extra text.
            ";

            var estimate = await GenerateJsonAsync<BudgetEstimateDto>(
                new object[] { new { text = prompt } }, "budget estimate", cancellationToken);
            estimate.BasedOnJobs = historicalPrices.Count;
            return estimate;
        }

        /// <summary>Drafts a vendor's proposal from their rough notes, grounded in the job.</summary>
        public async Task<string> WriteProposalAsync(
            string jobTitle,
            string jobDescription,
            string category,
            string vendorNotes,
            string? vendorSkills,
            decimal? bidAmount,
            int? estimatedDays,
            CancellationToken cancellationToken = default)
        {
            var prompt = $@"
                You are helping a tradesperson write a bid proposal to win a job.

                The job: {jobTitle} ({category})
                Job description: {jobDescription}
                The vendor's skills: {vendorSkills ?? "(not given)"}
                The vendor's rough notes: {vendorNotes}
                {(bidAmount.HasValue ? $"Their price: {bidAmount}" : "")}
                {(estimatedDays.HasValue ? $"Their timeline: {estimatedDays} day(s)" : "")}

                Write a persuasive, professional proposal addressed to the customer.
                Use only what the vendor told you - never invent qualifications, certifications or experience.
                Keep it under 120 words. Return plain prose only: no JSON, no markdown, no preamble.
            ";

            var text = await GenerateAsync(new object[] { new { text = prompt } }, jsonMode: false, cancellationToken);
            return text.Trim();
        }

        /// <summary>Ranks candidate vendors against a job. Candidates are pre-filtered by the caller.</summary>
        public async Task<List<VendorMatchDto>> MatchVendorsAsync(
            string jobTitle,
            string jobDescription,
            string category,
            string? location,
            IReadOnlyList<VendorMatchDto> candidates,
            int take,
            CancellationToken cancellationToken = default)
        {
            if (candidates.Count == 0)
            {
                return new List<VendorMatchDto>();
            }

            var roster = string.Join("\n", candidates.Select(c =>
                $"- id: {c.VendorProfileId} | company: {c.CompanyName} | skills: {c.Skills ?? "(none listed)"} " +
                $"| rating: {c.AverageRating} from {c.TotalReviews} reviews"));

            var prompt = $@"
                Match tradespeople to a job. Score each candidate 0-100 on how well they fit.

                The job: {jobTitle} ({category})
                Location: {location ?? "(not given)"}
                Description: {jobDescription}

                Candidates:
                {roster}

                Weigh their listed skills against what the job actually needs. A high rating is a
                tie-breaker, not a substitute for having the right skills. A vendor with no relevant
                skill for this job should score low even if well rated.

                Return a JSON object with a single key ""matches"", an array of at most {take} objects,
                best first, each with:
                - vendorProfileId: the id exactly as given above
                - score: integer 0-100
                - reason: one short sentence (max 100 chars) on why they fit this specific job
                Only include candidates that plausibly fit. Only return valid JSON.
            ";

            var result = await GenerateJsonAsync<VendorMatchListDto>(
                new object[] { new { text = prompt } }, "vendor matches", cancellationToken);

            // Re-attach the trusted DB values; only score and reason come from the model.
            var byId = candidates.ToDictionary(c => c.VendorProfileId);
            var matches = new List<VendorMatchDto>();
            foreach (var match in result.Matches)
            {
                if (match.VendorProfileId != null && byId.TryGetValue(match.VendorProfileId, out var vendor))
                {
                    vendor.Score = Math.Clamp(match.Score, 0, 100);
                    vendor.Reason = match.Reason ?? string.Empty;
                    matches.Add(vendor);
                }
            }

            return matches.OrderByDescending(m => m.Score).Take(take).ToList();
        }

        /// <summary>Ranks the bids on a job so the customer can weigh price against reputation.</summary>
        public async Task<BidRankingDto> RankBidsAsync(
            string jobTitle,
            string jobDescription,
            decimal budgetMin,
            decimal budgetMax,
            IReadOnlyList<(int BidId, decimal Amount, int Days, string? Proposal, string Company, double Rating, int Reviews)> bids,
            CancellationToken cancellationToken = default)
        {
            var lines = string.Join("\n", bids.Select(b =>
                $"- bidId: {b.BidId} | ${b.Amount} | {b.Days} day(s) | {b.Company} | rating {b.Rating} from {b.Reviews} reviews " +
                $"| proposal: {(string.IsNullOrWhiteSpace(b.Proposal) ? "(none)" : b.Proposal)}"));

            var prompt = $@"
                Help a customer choose between bids on their job.

                Job: {jobTitle}
                Description: {jobDescription}
                Their budget: {budgetMin} to {budgetMax}

                Bids:
                {lines}

                Weigh price, timeline, the quality of the proposal, and the vendor's rating together.
                The cheapest bid is not automatically the best; an unrated vendor is a risk, not a fault.

                Return a JSON object with:
                - recommendedBidId: the bidId you would pick
                - summary: one sentence (max 160 chars) explaining the pick to the customer
                - ranking: array of every bid, best first, each with bidId, rank (1 = best),
                  and reason (max 100 chars)
                Only return valid JSON.
            ";

            var ranking = await GenerateJsonAsync<BidRankingDto>(
                new object[] { new { text = prompt } }, "bid ranking", cancellationToken);

            // Drop anything the model invented; only bids that really exist may appear.
            var validIds = bids.Select(b => b.BidId).ToHashSet();
            ranking.Ranking = ranking.Ranking.Where(r => validIds.Contains(r.BidId)).ToList();
            if (ranking.RecommendedBidId.HasValue && !validIds.Contains(ranking.RecommendedBidId.Value))
            {
                ranking.RecommendedBidId = null;
            }

            return ranking;
        }

        /// <summary>Screens user-supplied free text before it is persisted.</summary>
        public async Task<ModerationDto> ModerateAsync(
            string text,
            string kind,
            CancellationToken cancellationToken = default)
        {
            var prompt = $@"
                You are moderating user content on a home-services marketplace.
                The text below is a {kind}.

                Text: ""{text}""

                Flag it only if it genuinely breaks one of these rules:
                - Abusive: harassment, hate, threats, slurs
                - Spam: advertising, links, or content unrelated to the job or service
                - ContactDetails: phone numbers, emails or messaging handles that would take the deal
                  off-platform (a street address on a job post is fine and expected)
                - Nonsense: gibberish with no meaning

                Ordinary criticism, blunt language and a negative review are all ALLOWED.
                When in doubt, allow it.

                Return a JSON object with:
                - allowed: boolean
                - category: one of ""Clean"", ""Abusive"", ""Spam"", ""ContactDetails"", ""Nonsense""
                - reason: if not allowed, one short sentence the user will see explaining what to fix. Else "".
                Only return valid JSON.
            ";

            return await GenerateJsonAsync<ModerationDto>(
                new object[] { new { text = prompt } }, "moderation result", cancellationToken);
        }

        /// <summary>
        /// Moderates and translates in a single call. Posting a job used to cost two round trips for
        /// what is one read of the same text — this halves it, which matters a lot on a metered quota.
        /// </summary>
        public async Task<ContentTriageDto> TriageAsync(
            string text,
            string kind,
            CancellationToken cancellationToken = default)
        {
            var prompt = $@"
                You are processing user content on a home-services marketplace. The text is a {kind}.

                Text: ""{text}""

                Do TWO things.

                1. MODERATE. Flag it only if it genuinely breaks one of these rules:
                   - Abusive: harassment, hate, threats, slurs
                   - Spam: advertising, links, or content unrelated to the job or service
                   - ContactDetails: phone numbers, emails or messaging handles that would take the deal
                     off-platform (a street address on a job post is fine and expected)
                   - Nonsense: gibberish with no meaning
                   Ordinary criticism, blunt language and negative feedback are ALLOWED.
                   When in doubt, allow it.

                2. TRANSLATE. Detect the language and render the text in English, faithfully, adding nothing.

                Return a JSON object with:
                - allowed: boolean
                - category: one of ""Clean"", ""Abusive"", ""Spam"", ""ContactDetails"", ""Nonsense""
                - reason: if not allowed, one short sentence the user will see explaining what to fix. Else "".
                - language: English name of the detected language (e.g. ""English"", ""Sinhala"", ""Tamil"")
                - isEnglish: boolean, true if the text was already English
                - english: the text in English, unchanged if it was already English
                Only return valid JSON.
            ";

            return await GenerateJsonAsync<ContentTriageDto>(
                new object[] { new { text = prompt } }, "content check", cancellationToken);
        }

        /// <summary>Detects the language of a job post and renders it in English.</summary>
        public async Task<TranslationDto> TranslateToEnglishAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            var prompt = $@"
                Detect the language of the text below and translate it into English.

                Text: ""{text}""

                Return a JSON object with:
                - language: the English name of the detected language (e.g. ""English"", ""Sinhala"", ""Tamil"")
                - isEnglish: boolean, true if the text was already English
                - english: the text in English. If it was already English, return it unchanged.
                Translate faithfully. Do not add, remove or embellish anything.
                Only return valid JSON.
            ";

            return await GenerateJsonAsync<TranslationDto>(
                new object[] { new { text = prompt } }, "translation", cancellationToken);
        }

        /// <summary>Compares the vendor's "after" photo against the customer's original "before" photo.</summary>
        public async Task<CompletionCheckDto> VerifyCompletionAsync(
            string jobTitle,
            string jobDescription,
            byte[] beforeImage,
            string? beforeMime,
            byte[] afterImage,
            string? afterMime,
            CancellationToken cancellationToken = default)
        {
            var prompt = $@"
                A tradesperson says they have finished this job. Compare the two photos.

                Job: {jobTitle}
                What the customer asked for: {jobDescription}

                The FIRST image is the problem the customer reported (before).
                The SECOND image is what the vendor submitted as proof of completion (after).

                Return a JSON object with:
                - verdict: one of ""Resolved"", ""Unclear"", ""NotResolved""
                - summary: one sentence (max 160 chars) describing what changed between the photos.
                Be careful: say ""Unclear"" if the photos show different places or you cannot tell.
                Do not accuse anyone of fraud; you are an aid to the customer, not a judge.
                Only return valid JSON.
            ";

            var parts = new object[]
            {
                new { text = prompt },
                new
                {
                    inline_data = new
                    {
                        mime_type = string.IsNullOrWhiteSpace(beforeMime) ? "image/jpeg" : beforeMime,
                        data = Convert.ToBase64String(beforeImage)
                    }
                },
                new
                {
                    inline_data = new
                    {
                        mime_type = string.IsNullOrWhiteSpace(afterMime) ? "image/jpeg" : afterMime,
                        data = Convert.ToBase64String(afterImage)
                    }
                }
            };

            var check = await GenerateJsonAsync<CompletionCheckDto>(parts, "completion check", cancellationToken);
            if (check.Verdict is not ("Resolved" or "Unclear" or "NotResolved"))
            {
                check.Verdict = "Unclear";
            }
            return check;
        }

        // ---------------------------------------------------------------- embeddings

        /// <summary>Embeds text for semantic search. Returns null if the AI is unavailable.</summary>
        public async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var body = new
            {
                model = $"models/{EmbeddingModel}",
                content = new { parts = new[] { new { text } } },
                taskType = "SEMANTIC_SIMILARITY",
                outputDimensionality = EmbeddingDimensions
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{EmbeddingModel}:embedContent";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", _apiKey);

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Embedding request failed ({Status}): {Body}", (int)response.StatusCode, payload);
                    return null;
                }

                using var doc = JsonDocument.Parse(payload);
                if (!doc.RootElement.TryGetProperty("embedding", out var embedding) ||
                    !embedding.TryGetProperty("values", out var values) ||
                    values.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                return values.EnumerateArray().Select(v => v.GetSingle()).ToArray();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // Semantic search is an enhancement: if it fails, the caller falls back to keyword search.
                _logger.LogWarning(ex, "Could not embed text for semantic search.");
                return null;
            }
        }

        /// <summary>Cosine similarity, for ranking embeddings in memory.</summary>
        public static double CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length || a.Length == 0)
            {
                return 0;
            }

            double dot = 0, normA = 0, normB = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            if (normA == 0 || normB == 0)
            {
                return 0;
            }

            return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        // ---------------------------------------------------------------- transport

        /// <summary>Single place where we talk to Gemini: one error path for every feature.</summary>
        private async Task<string> GenerateAsync(object[] parts, bool jsonMode, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                throw new AiUnavailableException(
                    "AI is not configured. Set AiSettings:GeminiApiKey to a valid Gemini API key.");
            }

            object requestBody = jsonMode
                ? new
                {
                    contents = new[] { new { parts } },
                    // Ask for raw JSON so the reply never arrives wrapped in a markdown fence.
                    generationConfig = new { response_mime_type = "application/json" }
                }
                : new
                {
                    contents = new[] { new { parts } }
                };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", _apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "Could not reach the Gemini API.");
                throw new AiUnavailableException("Could not reach the AI service. Check your network connection and try again.");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Gemini request failed with {StatusCode}: {Body}", (int)response.StatusCode, body);
                    throw new AiUnavailableException(DescribeError(body, response.StatusCode));
                }

                var text = ExtractText(body);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogError("Gemini returned no usable content: {Body}", body);
                    throw new AiUnavailableException("The AI did not return a usable response. Please try again.");
                }

                return text;
            }
        }

        /// <summary>Turns a Gemini error payload into a message worth showing the user.</summary>
        private string DescribeError(string body, System.Net.HttpStatusCode statusCode)
        {
            string? detail = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message))
                {
                    detail = message.GetString();
                }
            }
            catch (JsonException)
            {
                // Not JSON; fall through to the generic message below.
            }

            return statusCode switch
            {
                System.Net.HttpStatusCode.NotFound =>
                    $"The AI model '{_model}' is not available for this API key. {detail}".Trim(),
                System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized =>
                    $"The Gemini API key was rejected. {detail}".Trim(),
                System.Net.HttpStatusCode.TooManyRequests =>
                    "The AI service is rate limited right now. Please try again in a moment.",
                _ => detail ?? $"The AI service returned an error ({(int)statusCode})."
            };
        }

        /// <summary>Pulls candidates[0].content.parts[*].text out without assuming the shape exists.</summary>
        private static string? ExtractText(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return null;
            }

            if (!candidates[0].TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }

            return null;
        }

        /// <summary>
        /// Generates JSON and parses it, resampling once if the model emits something unparseable.
        /// Gemini occasionally drops a stray token into otherwise valid JSON; because generation is
        /// stochastic, a second attempt almost always comes back clean, and one bad token should not
        /// cost the user the whole result.
        /// </summary>
        private async Task<T> GenerateJsonAsync<T>(object[] parts, string what, CancellationToken cancellationToken)
            where T : class
        {
            const int attempts = 2;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                var text = await GenerateAsync(parts, jsonMode: true, cancellationToken);
                var parsed = TryParse<T>(text);
                if (parsed is not null)
                {
                    return parsed;
                }

                _logger.LogWarning(
                    "Unparseable {What} from the AI on attempt {Attempt}/{Attempts}: {Text}",
                    what, attempt, attempts, text);
            }

            throw new AiUnavailableException($"The AI returned an unreadable {what}. Please try again.");
        }

        private static T? TryParse<T>(string text) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(Unwrap(text), ParseOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Strips a ```json fence / stray prose, leaving the JSON object itself.</summary>
        private static string Unwrap(string text)
        {
            var trimmed = text.Trim();
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
        }
    }
}
