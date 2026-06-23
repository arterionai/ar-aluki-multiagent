using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Dispatch;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>Unit tests for the WhatsApp reply composer used by the calendar agent.</summary>
[Trait("Category", "Unit")]
public sealed class CalendarSchedulingReplyTests
{
    [Fact]
    public void Connect_prompt_includes_provider_and_url()
    {
        var reply = CalendarSchedulingReply.ConnectPrompt(
            new[] { (CalendarProvider.Outlook, "https://func/api/calendar/connect/start?token=abc") });

        Assert.Contains("Outlook", reply);
        Assert.Contains("https://func/api/calendar/connect/start?token=abc", reply);
        Assert.Contains("conectar tu calendario", reply);
    }

    [Fact]
    public void Connect_prompt_lists_multiple_providers()
    {
        var reply = CalendarSchedulingReply.ConnectPrompt(new[]
        {
            (CalendarProvider.Outlook, "https://x/o"),
            (CalendarProvider.Google, "https://x/g"),
        });

        Assert.Contains("Outlook", reply);
        Assert.Contains("Google Calendar", reply);
        Assert.Contains("https://x/o", reply);
        Assert.Contains("https://x/g", reply);
    }

    [Fact]
    public void Connect_prompt_handles_no_links()
    {
        var reply = CalendarSchedulingReply.ConnectPrompt(Array.Empty<(CalendarProvider, string)>());
        Assert.Contains("no está disponible", reply);
    }

    [Fact]
    public void Confirmation_formats_local_time()
    {
        var startUtc = new DateTimeOffset(2026, 7, 1, 20, 0, 0, TimeSpan.Zero); // 20:00 UTC
        var reply = CalendarSchedulingReply.Confirmation("Dentista", startUtc, "America/Mexico_City");

        Assert.Contains("Dentista", reply);
        Assert.Contains("14:00", reply); // UTC-6
        Assert.StartsWith("✅", reply);
    }

    [Fact]
    public void Confirmation_without_time_is_still_friendly()
    {
        var reply = CalendarSchedulingReply.Confirmation(null, null, null);
        Assert.Contains("agendé", reply);
    }

    [Fact]
    public void Clarification_falls_back_when_no_question()
    {
        Assert.Contains("detalle", CalendarSchedulingReply.Clarification(null));
        Assert.Equal("¿A qué hora?", CalendarSchedulingReply.Clarification("¿A qué hora?"));
    }
}
