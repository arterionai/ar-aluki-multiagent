using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Xunit;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Contract tests for SB-008B YouTube Link Save and Classification — pure-logic
/// utilities (video ID extraction, canonical URL formatting, URL extraction from
/// text). These tests exercise only stateless helpers in Aluki.Runtime.Abstractions
/// and do not require a database or any real service.
/// </summary>
[Trait("Category", "Contract")]
public sealed class YouTubeLinkCaptureContractTests
{
    // ── TryExtractVideoId ─────────────────────────────────────────────────────

    [Fact]
    public void Extracts_video_id_from_standard_watch_url()
    {
        var id = YouTubeUrlCanonicalizer.TryExtractVideoId(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.Equal("dQw4w9WgXcQ", id);
    }

    [Fact]
    public void Extracts_video_id_from_short_url()
    {
        var id = YouTubeUrlCanonicalizer.TryExtractVideoId(
            "https://youtu.be/dQw4w9WgXcQ");

        Assert.Equal("dQw4w9WgXcQ", id);
    }

    [Fact]
    public void Extracts_video_id_from_shorts_url()
    {
        var id = YouTubeUrlCanonicalizer.TryExtractVideoId(
            "https://www.youtube.com/shorts/dQw4w9WgXcQ");

        Assert.Equal("dQw4w9WgXcQ", id);
    }

    [Fact]
    public void Extracts_video_id_from_embed_url()
    {
        var id = YouTubeUrlCanonicalizer.TryExtractVideoId(
            "https://www.youtube.com/embed/dQw4w9WgXcQ");

        Assert.Equal("dQw4w9WgXcQ", id);
    }

    [Fact]
    public void Returns_null_for_non_youtube_url()
    {
        var id = YouTubeUrlCanonicalizer.TryExtractVideoId("https://example.com");

        Assert.Null(id);
    }

    [Fact]
    public void Returns_null_for_malformed_url()
    {
        var id = YouTubeUrlCanonicalizer.TryExtractVideoId("not a url");

        Assert.Null(id);
    }

    [Fact]
    public void Returns_null_for_channel_url()
    {
        // Channel URLs have no video ID — the path segment after /channel/ is
        // a channel ID, not an 11-char video ID.
        var id = YouTubeUrlCanonicalizer.TryExtractVideoId(
            "https://www.youtube.com/channel/UCxyz");

        Assert.Null(id);
    }

    // ── ToCanonicalUrl ────────────────────────────────────────────────────────

    [Fact]
    public void ToCanonicalUrl_produces_expected_format()
    {
        var url = YouTubeUrlCanonicalizer.ToCanonicalUrl("dQw4w9WgXcQ");

        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", url);
    }

    // ── ExtractYoutubeUrls ────────────────────────────────────────────────────

    [Fact]
    public void ExtractYoutubeUrls_finds_one_url_in_text()
    {
        var urls = YouTubeUrlCanonicalizer.ExtractYoutubeUrls(
            "Check this out https://www.youtube.com/watch?v=dQw4w9WgXcQ today");

        Assert.Single(urls);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", urls[0]);
    }

    [Fact]
    public void ExtractYoutubeUrls_finds_multiple_urls()
    {
        var urls = YouTubeUrlCanonicalizer.ExtractYoutubeUrls(
            "First https://youtu.be/dQw4w9WgXcQ second https://www.youtube.com/watch?v=9bZkp7q19f0");

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://youtu.be/dQw4w9WgXcQ", urls);
        Assert.Contains("https://www.youtube.com/watch?v=9bZkp7q19f0", urls);
    }

    [Fact]
    public void ExtractYoutubeUrls_ignores_non_youtube_urls()
    {
        var urls = YouTubeUrlCanonicalizer.ExtractYoutubeUrls(
            "Visit https://example.com and https://vimeo.com/123456789");

        Assert.Empty(urls);
    }

    [Fact]
    public void ExtractYoutubeUrls_returns_empty_for_plain_text()
    {
        var urls = YouTubeUrlCanonicalizer.ExtractYoutubeUrls(
            "No links in this message at all");

        Assert.Empty(urls);
    }
}
