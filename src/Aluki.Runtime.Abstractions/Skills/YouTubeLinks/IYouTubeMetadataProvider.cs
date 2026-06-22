namespace Aluki.Runtime.Abstractions.Skills.YouTubeLinks;

public sealed record YouTubeMetadataResult(
    string? Title,
    string? DescriptionSnippet,
    string? ChannelName,
    DateTimeOffset? PublishedAt,
    bool IsPartial = false,
    string? ErrorCode = null);

public interface IPrimaryYouTubeMetadataProvider
{
    Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct);
}

public interface ISecondaryYouTubeMetadataProvider
{
    Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct);
}
