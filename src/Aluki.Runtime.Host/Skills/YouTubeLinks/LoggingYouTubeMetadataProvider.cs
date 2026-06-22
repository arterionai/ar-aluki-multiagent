using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.YouTubeLinks;

/// <summary>
/// Stub primary metadata provider — logs the attempt and returns null (provider not configured).
/// Replace with a real YouTube Data API v3 client when credentials are available.
/// </summary>
public sealed class LoggingPrimaryYouTubeMetadataProvider : IPrimaryYouTubeMetadataProvider
{
    private readonly ILogger<LoggingPrimaryYouTubeMetadataProvider> _logger;

    public LoggingPrimaryYouTubeMetadataProvider(ILogger<LoggingPrimaryYouTubeMetadataProvider> logger)
        => _logger = logger;

    public Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct)
    {
        _logger.LogDebug(
            "Primary YouTube metadata provider is not configured. Skipping fetch for videoId={VideoId}.",
            videoId);
        return Task.FromResult<YouTubeMetadataResult?>(null);
    }
}

/// <summary>
/// Stub secondary metadata provider — logs the attempt and returns null (provider not configured).
/// Replace with an OEmbed or scrape-based fallback when ready.
/// </summary>
public sealed class LoggingSecondaryYouTubeMetadataProvider : ISecondaryYouTubeMetadataProvider
{
    private readonly ILogger<LoggingSecondaryYouTubeMetadataProvider> _logger;

    public LoggingSecondaryYouTubeMetadataProvider(ILogger<LoggingSecondaryYouTubeMetadataProvider> logger)
        => _logger = logger;

    public Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct)
    {
        _logger.LogDebug(
            "Secondary YouTube metadata provider is not configured. Skipping fetch for videoId={VideoId}.",
            videoId);
        return Task.FromResult<YouTubeMetadataResult?>(null);
    }
}
