namespace Aluki.Runtime.Abstractions.Skills.YouTubeLinks;

public sealed record YouTubeClassificationResult(
    string? Category,
    string[] Tags,
    string? Summary,
    string ConfidenceLabel,
    bool CategoryUncertain,
    bool TagsUncertain,
    bool SummaryUncertain,
    decimal? ConfidenceScore = null);

public interface IYouTubeClassificationProvider
{
    Task<YouTubeClassificationResult> ClassifyAsync(
        string videoId,
        string? title,
        string? description,
        CancellationToken ct);
}
