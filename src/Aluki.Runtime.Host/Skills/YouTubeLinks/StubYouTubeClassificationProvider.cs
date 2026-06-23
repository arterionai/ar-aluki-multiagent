using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;

namespace Aluki.Runtime.Host.Skills.YouTubeLinks;

/// <summary>
/// Stub classification provider — returns low-confidence results until a
/// real Foundry-based classifier is wired up.
/// </summary>
public sealed class StubYouTubeClassificationProvider : IYouTubeClassificationProvider
{
    public Task<YouTubeClassificationResult> ClassifyAsync(
        string videoId, string? title, string? description, CancellationToken ct)
        => Task.FromResult(new YouTubeClassificationResult(
            Category: null,
            Tags: [],
            Summary: null,
            ConfidenceLabel: YouTubeLinkConfidence.Low,
            CategoryUncertain: true,
            TagsUncertain: true,
            SummaryUncertain: true,
            ConfidenceScore: null));
}
