using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Connect;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for the consent/result HTML rendering: the page explains what will
/// happen, posts the signed token to the begin URL, and never auto-starts OAuth.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CalendarConsentPagesTests
{
    [Fact]
    public void Consent_page_explains_and_posts_token_to_begin()
    {
        var html = CalendarConsentPages.RenderConsent(
            CalendarProvider.Outlook, "https://func.example.net/api/calendar/connect/begin", "TOKEN123");

        Assert.Contains("Outlook", html);
        Assert.Contains("Conectar de forma segura", html);
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("action=\"https://func.example.net/api/calendar/connect/begin\"", html);
        Assert.Contains("value=\"TOKEN123\"", html);
        // It must explain the permission and the security posture.
        Assert.Contains("eventos", html);
        Assert.Contains("cifrad", html); // "cifradas"
    }

    [Fact]
    public void Consent_page_uses_friendly_google_name()
    {
        var html = CalendarConsentPages.RenderConsent(
            CalendarProvider.Google, "https://x/begin", "T");
        Assert.Contains("Google Calendar", html);
    }

    [Fact]
    public void Success_page_names_provider_and_points_back_to_whatsapp()
    {
        var html = CalendarConsentPages.RenderSuccess(CalendarProvider.Google);
        Assert.Contains("Google Calendar", html);
        Assert.Contains("WhatsApp", html);
    }

    [Fact]
    public void Error_and_expired_pages_render_message()
    {
        // Note: HtmlEncode turns accented chars into numeric entities, so assert on ASCII.
        Assert.Contains("algo paso mal", CalendarConsentPages.RenderError("algo paso mal"));
        Assert.Contains("enlace", CalendarConsentPages.RenderExpired());
    }

    [Fact]
    public void Html_escapes_injected_message()
    {
        var html = CalendarConsentPages.RenderError("<script>alert(1)</script>");
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
