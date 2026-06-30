using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class LinkCanonicalizationTests
{
    // ── IsLinkSaveIntent ──────────────────────────────────────────────────────

    [Fact]
    public void IsLinkSaveIntent_UrlWithLabel_returns_true()
    {
        // Label text + URL, no question mark → link save
        Assert.True(LinkCanonicalization.IsLinkSaveIntent(
            "donde ir en Houston https://www.instagram.com/reel/abc123/"));
    }

    [Fact]
    public void IsLinkSaveIntent_UrlOnly_returns_true()
    {
        // URL alone, no surrounding text → link save
        Assert.True(LinkCanonicalization.IsLinkSaveIntent("https://example.com/page"));
    }

    [Fact]
    public void IsLinkSaveIntent_UrlWithEnglishQuestion_returns_false()
    {
        // Has a '?' → treat as conversational
        Assert.False(LinkCanonicalization.IsLinkSaveIntent(
            "what do you think of this? https://example.com/restaurant"));
    }

    [Fact]
    public void IsLinkSaveIntent_UrlWithSpanishQuestion_returns_false()
    {
        // Has '¿' → treat as conversational
        Assert.False(LinkCanonicalization.IsLinkSaveIntent(
            "¿qué piensas de este restaurante? https://example.com/lugar"));
    }

    [Fact]
    public void IsLinkSaveIntent_NoUrl_returns_false()
    {
        Assert.False(LinkCanonicalization.IsLinkSaveIntent("donde ir en Houston"));
    }

    [Fact]
    public void IsLinkSaveIntent_NullText_returns_false()
    {
        Assert.False(LinkCanonicalization.IsLinkSaveIntent(null));
    }

    [Fact]
    public void IsLinkSaveIntent_EmptyText_returns_false()
    {
        Assert.False(LinkCanonicalization.IsLinkSaveIntent("   "));
    }

    [Fact]
    public void IsLinkSaveIntent_QuestionInLabel_returns_false()
    {
        // Question mark in label, even without '¿', means conversational
        Assert.False(LinkCanonicalization.IsLinkSaveIntent(
            "is this a good place? https://example.com/review"));
    }

    // ── ExtractFirstUrl ───────────────────────────────────────────────────────

    [Fact]
    public void ExtractFirstUrl_TextWithUrl_returns_url()
    {
        var url = LinkCanonicalization.ExtractFirstUrl("checa esto https://example.com/page");
        Assert.Equal("https://example.com/page", url);
    }

    [Fact]
    public void ExtractFirstUrl_NoUrl_returns_null()
    {
        Assert.Null(LinkCanonicalization.ExtractFirstUrl("no links here"));
    }

    [Fact]
    public void ExtractFirstUrl_MultipleUrls_returns_first()
    {
        var url = LinkCanonicalization.ExtractFirstUrl(
            "https://first.com https://second.com");
        Assert.Equal("https://first.com", url);
    }

    [Fact]
    public void ExtractFirstUrl_Null_returns_null()
    {
        Assert.Null(LinkCanonicalization.ExtractFirstUrl(null));
    }

    // ── ExtractLabelText ──────────────────────────────────────────────────────

    [Fact]
    public void ExtractLabelText_UrlAtEnd_returns_label()
    {
        var label = LinkCanonicalization.ExtractLabelText(
            "donde ir en Houston https://example.com/reel",
            "https://example.com/reel");
        Assert.Equal("donde ir en Houston", label);
    }

    [Fact]
    public void ExtractLabelText_UrlOnly_returns_null()
    {
        var label = LinkCanonicalization.ExtractLabelText(
            "https://example.com", "https://example.com");
        Assert.Null(label);
    }

    [Fact]
    public void ExtractLabelText_UrlAtStart_returns_trailing_text()
    {
        var label = LinkCanonicalization.ExtractLabelText(
            "https://example.com cool article",
            "https://example.com");
        Assert.Equal("cool article", label);
    }
}
