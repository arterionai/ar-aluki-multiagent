using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.YouTubeLinks;

internal sealed class OEmbedYouTubeMetadataProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<OEmbedYouTubeMetadataProvider> logger) : ISecondaryYouTubeMetadataProvider
{
    public async Task<YouTubeMetadataResult?> FetchAsync(string videoId, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("youtube-oembed");
            var url = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}&format=json";

            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("OEmbed returned {Status} for video_id={VideoId}", (int)response.StatusCode, videoId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var channel = root.TryGetProperty("author_name", out var a) ? a.GetString() : null;

            return new YouTubeMetadataResult(
                Title: title,
                DescriptionSnippet: null,
                ChannelName: channel,
                PublishedAt: null,
                IsPartial: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "OEmbed fetch failed. video_id={VideoId}", videoId);
            return null;
        }
    }
}
