using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProConnect.WebAPI.Dtos;

namespace ProConnect.WebAPI.Services
{
    /// <summary>
    /// The AI touches that run silently inside ordinary requests: moderation, translation, embedding.
    ///
    /// Every method here FAILS OPEN. A customer must still be able to post a job when Gemini is down,
    /// so an AI outage degrades the feature (no translation, no semantic match, no screening) rather
    /// than rejecting the user's work. Anything the user explicitly asked the AI to do belongs in
    /// AiController instead, where failures are reported to them.
    /// </summary>
    public class ContentAiService
    {
        private readonly AiService _aiService;
        private readonly ILogger<ContentAiService> _logger;

        public ContentAiService(AiService aiService, ILogger<ContentAiService> logger)
        {
            _aiService = aiService;
            _logger = logger;
        }

        /// <summary>
        /// Moderates AND translates in one call — the two questions read the same text, so asking
        /// twice would double what a job post costs against the Gemini quota.
        /// Fails open: an outage means "allowed, untranslated".
        /// </summary>
        public async Task<ContentTriageDto> TriageAsync(string text, string kind, CancellationToken cancellationToken = default)
        {
            if (!_aiService.IsConfigured || string.IsNullOrWhiteSpace(text))
            {
                return new ContentTriageDto { Allowed = true, IsEnglish = true, English = text };
            }

            try
            {
                return await _aiService.TriageAsync(text, kind, cancellationToken);
            }
            catch (AiUnavailableException ex)
            {
                _logger.LogWarning(ex, "Content check unavailable; letting the {Kind} through untranslated.", kind);
                return new ContentTriageDto { Allowed = true, IsEnglish = true, English = text };
            }
        }

        /// <summary>Screens user text. Allows it through if the AI cannot be reached.</summary>
        public async Task<ModerationDto> ScreenAsync(string text, string kind, CancellationToken cancellationToken = default)
        {
            if (!_aiService.IsConfigured || string.IsNullOrWhiteSpace(text))
            {
                return new ModerationDto { Allowed = true };
            }

            try
            {
                return await _aiService.ModerateAsync(text, kind, cancellationToken);
            }
            catch (AiUnavailableException ex)
            {
                _logger.LogWarning(ex, "Moderation unavailable; allowing the {Kind} through.", kind);
                return new ModerationDto { Allowed = true };
            }
        }

        /// <summary>Detects and translates non-English text. Returns null if it could not be done.</summary>
        public async Task<TranslationDto?> TranslateIfNeededAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!_aiService.IsConfigured || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return await _aiService.TranslateToEnglishAsync(text, cancellationToken);
            }
            catch (AiUnavailableException ex)
            {
                _logger.LogWarning(ex, "Translation unavailable; keeping the text as written.");
                return null;
            }
        }

        /// <summary>Embeds a job for semantic search, serialized for storage. Null when unavailable.</summary>
        public async Task<string?> EmbedJobAsync(string title, string description, CancellationToken cancellationToken = default)
        {
            var vector = await _aiService.EmbedAsync($"{title}. {description}", cancellationToken);
            return vector == null ? null : JsonSerializer.Serialize(vector);
        }

        /// <summary>Embeds a search query. Null when unavailable, which sends the caller back to keyword search.</summary>
        public Task<float[]?> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
            _aiService.EmbedAsync(query, cancellationToken);

        /// <summary>Reads a stored embedding back. Null if absent or corrupt.</summary>
        public static float[]? Deserialize(string? embedding)
        {
            if (string.IsNullOrWhiteSpace(embedding))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<float[]>(embedding);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
