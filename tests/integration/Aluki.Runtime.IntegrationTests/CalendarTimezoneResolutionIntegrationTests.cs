using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Skills;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Integration tests for timezone normalization and DST ambiguity clarification
/// (T021, FR-004, FR-004a, SC-011). Exercises CalendarTimezoneResolverSkill and
/// CalendarClarificationSkill together to verify the clarification gate fires on
/// DST-ambiguous times and passes on unambiguous times.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CalendarTimezoneResolutionIntegrationTests
{
    private readonly CalendarTimezoneResolverSkill _resolver = new();
    private readonly CalendarClarificationSkill _clarification = new();
    private readonly CalendarRequestClassifierSkill _classifier = new();

    // ── Timezone resolution ────────────────────────────────────────────────

    [Theory]
    [InlineData("PST", "America/Los_Angeles")]
    [InlineData("EST", "America/New_York")]
    [InlineData("CST", "America/Chicago")]
    [InlineData("MST", "America/Denver")]
    [InlineData("UTC", "UTC")]
    [InlineData("Eastern Time", "America/New_York")]
    [InlineData("Pacific Time", "America/Los_Angeles")]
    public void Abbreviation_resolves_to_expected_IANA_id(string abbreviation, string expectedIana)
    {
        var resolution = _resolver.Resolve(abbreviation, null);

        Assert.True(resolution.IsResolved);
        Assert.Equal(expectedIana, resolution.IanaId);
        Assert.Equal(TimezoneResolutionSource.Request, resolution.Source);
    }

    [Fact]
    public void IANA_id_passed_directly_resolves_successfully()
    {
        var resolution = _resolver.Resolve("America/Chicago", null);

        Assert.True(resolution.IsResolved);
        Assert.Equal("America/Chicago", resolution.IanaId);
    }

    [Fact]
    public void Unknown_hint_with_profile_fallback_uses_profile()
    {
        var resolution = _resolver.Resolve("BADTZ", "America/Denver");

        Assert.True(resolution.IsResolved);
        Assert.Equal("America/Denver", resolution.IanaId);
        Assert.Equal(TimezoneResolutionSource.Profile, resolution.Source);
    }

    [Fact]
    public void Null_hint_and_null_profile_returns_unresolved()
    {
        var resolution = _resolver.Resolve(null, null);

        Assert.False(resolution.IsResolved);
    }

    // ── DST ambiguity detection ────────────────────────────────────────────

    [Fact]
    public void Non_ambiguous_time_is_not_flagged()
    {
        // 2024-03-10 14:00 — after spring forward in US Eastern (unambiguous)
        var ambiguous = _resolver.IsDstAmbiguous("America/New_York", "2024-03-10T14:00:00");
        Assert.False(ambiguous);
    }

    [Fact]
    public void DST_fall_back_hour_is_detected_as_ambiguous()
    {
        // 2024-11-03 01:30 — occurs twice in US Eastern (fall back from EDT to EST)
        var ambiguous = _resolver.IsDstAmbiguous("America/New_York", "2024-11-03T01:30:00");
        Assert.True(ambiguous);
    }

    [Fact]
    public void Spring_forward_gap_is_not_ambiguous_but_invalid()
    {
        // 2024-03-10 02:30 — skipped by spring forward; ToUtc returns null
        var utc = _resolver.ToUtc("America/New_York", "2024-03-10T02:30:00");
        Assert.Null(utc);
    }

    // ── Clarification gate ─────────────────────────────────────────────────

    [Fact]
    public void Missing_title_triggers_clarification()
    {
        var classified = _classifier.Classify("tomorrow at 3pm PST");
        var timezone = _resolver.Resolve("PST", null);
        // Force no title scenario
        var noTitle = classified with { Title = null };

        var decision = _clarification.Evaluate(noTitle, timezone);

        Assert.True(decision.NeedsClarification);
        Assert.Equal("title", decision.RequestedField);
    }

    [Fact]
    public void Missing_start_time_triggers_clarification()
    {
        var classified = new ClassifiedRequest("Team meeting", null, null, "PST", null, "hash");
        var timezone = _resolver.Resolve("PST", null);

        var decision = _clarification.Evaluate(classified, timezone);

        Assert.True(decision.NeedsClarification);
        Assert.Equal("start_time", decision.RequestedField);
    }

    [Fact]
    public void Missing_timezone_triggers_clarification()
    {
        var classified = new ClassifiedRequest("Standup", "tomorrow at 10am", null, null, null, "hash");
        var timezone = _resolver.Resolve(null, null); // unresolved

        var decision = _clarification.Evaluate(classified, timezone);

        Assert.True(decision.NeedsClarification);
        Assert.Equal("timezone", decision.RequestedField);
    }

    [Fact]
    public void DST_ambiguous_time_triggers_clarification()
    {
        var classified = new ClassifiedRequest("Dentist", "2024-11-03T01:30:00", null, "EST", null, "hash");
        var timezone = _resolver.Resolve("EST", null) with { DstAmbiguous = true };

        var decision = _clarification.Evaluate(classified, timezone);

        Assert.True(decision.NeedsClarification);
        Assert.Equal("dst_ambiguity", decision.RequestedField);
        Assert.Contains("daylight saving", decision.QuestionText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Complete_unambiguous_request_requires_no_clarification()
    {
        var classified = new ClassifiedRequest("Team sync", "2024-06-15T14:00:00", null, "PST", null, "hash");
        var timezone = _resolver.Resolve("PST", null);

        var decision = _clarification.Evaluate(classified, timezone);

        Assert.False(decision.NeedsClarification);
    }

    // ── UTC conversion ─────────────────────────────────────────────────────

    [Fact]
    public void Unambiguous_time_converts_to_expected_UTC()
    {
        // 2024-06-15 14:00 PDT = 2024-06-15 21:00 UTC
        var utc = _resolver.ToUtc("America/Los_Angeles", "2024-06-15T14:00:00");

        Assert.NotNull(utc);
        Assert.Equal(21, utc!.Value.Hour);
        Assert.Equal(0, utc.Value.Minute);
    }
}
