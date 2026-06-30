using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Aluki.Runtime.Memory.Chat;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.YouTubeLinks;

internal sealed class FoundryYouTubeClassificationProvider(
    IChatModelRouter chatRouter,
    ILogger<FoundryYouTubeClassificationProvider> logger) : IYouTubeClassificationProvider
{
    private static readonly YouTubeClassificationResult LowConfidenceFallback = new(
        Category: null, Tags: [], Summary: null,
        ConfidenceLabel: YouTubeLinkConfidence.Low,
        CategoryUncertain: true, TagsUncertain: true, SummaryUncertain: true);

    private const string SystemPrompt =
        """
        You classify YouTube videos based on title and description.
        Respond ONLY with a JSON object (no markdown, no explanation):
        {
          "category": "<single category string or null>",
          "tags": ["<tag1>", "<tag2>"],
          "summary": "<1-2 sentence summary or null>",
          "confidence": "<high|medium|low>"
        }
        Use "high" when title+description are clear and informative.
        Use "medium" when partial information is available.
        Use "low" when title or description are missing or uninformative.
        """;

    public async Task<YouTubeClassificationResult> ClassifyAsync(
        string videoId, string? title, string? description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
            return LowConfidenceFallback;

        var userPrompt = $"Title: {title ?? "(none)"}\nDescription: {description ?? "(none)"}";

        try
        {
            using var llmCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var raw = await chatRouter.CompleteAsync(SystemPrompt, userPrompt, llmCts.Token);
            ct.ThrowIfCancellationRequested();
            return ParseResult(raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTube classification LLM call failed. video_id={VideoId}", videoId);
            return LowConfidenceFallback;
        }
    }

    private static YouTubeClassificationResult ParseResult(string raw)
    {
        try
        {
            // Strip optional markdown code fences
            var json = raw.Trim();
            if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
            if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")].TrimEnd();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var category = root.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.String
                ? cat.GetString() : null;
            var summary = root.TryGetProperty("summary", out var sum) && sum.ValueKind == JsonValueKind.String
                ? sum.GetString() : null;
            var confidenceRaw = root.TryGetProperty("confidence", out var conf) ? conf.GetString() : null;
            var confidence = confidenceRaw?.ToLowerInvariant() switch
            {
                "high" => YouTubeLinkConfidence.High,
                "medium" => YouTubeLinkConfidence.Medium,
                _ => YouTubeLinkConfidence.Low
            };

            var tags = Array.Empty<string>();
            if (root.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
                tags = [.. tagsEl.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)];

            return new YouTubeClassificationResult(
                Category: category,
                Tags: tags,
                Summary: summary,
                ConfidenceLabel: confidence,
                CategoryUncertain: category is null,
                TagsUncertain: tags.Length == 0,
                SummaryUncertain: summary is null);
        }
        catch
        {
            return LowConfidenceFallback;
        }
    }
}
