using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.YouTubeLinks;

internal sealed class YouTubeDataApiMetadataProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<YouTubeDataApiMetadataProvider> logger) : IPrimaryYouTubeMetadataProvider
{
    private const string ApiBase = "https://www.googleapis.com/youtube/v3/videos";
    private const int MaxDescriptionLength = 500;

    public async Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct)
    {
        var apiKey = configuration["YouTube:DataApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogDebug("YouTube:DataApiKey not configured. Skipping primary fetch for video_id={VideoId}", videoId);
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient("youtube-data-api");
            var url = $"{ApiBase}?part=snippet&id={Uri.EscapeDataString(videoId)}&key={Uri.EscapeDataString(apiKey)}";

            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("YouTube Data API returned {Status} for video_id={VideoId}", (int)response.StatusCode, videoId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            {
                logger.LogDebug("YouTube Data API returned no items for video_id={VideoId}", videoId);
                return null;
            }

            var snippet = items[0].GetProperty("snippet");

            var title = snippet.TryGetProperty("title", out var t) ? t.GetString() : null;
            var description = snippet.TryGetProperty("description", out var d) ? d.GetString() : null;
            var channel = snippet.TryGetProperty("channelTitle", out var c) ? c.GetString() : null;
            DateTimeOffset? publishedAt = null;
            if (snippet.TryGetProperty("publishedAt", out var p) && DateTimeOffset.TryParse(p.GetString(), out var parsed))
                publishedAt = parsed;

            var descriptionSnippet = description?.Length > MaxDescriptionLength
                ? description[..MaxDescriptionLength]
                : description;

            return new YouTubeMetadataResult(
                Title: title,
                DescriptionSnippet: descriptionSnippet,
                ChannelName: channel,
                PublishedAt: publishedAt,
                IsPartial: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "YouTube Data API fetch failed. video_id={VideoId}", videoId);
            return null;
        }
    }
}
