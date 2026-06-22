using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Providers;
using Aluki.Runtime.Host.Calendar.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Integration tests for Google/Outlook provider parity (T032, FR-010, SC-008).
/// Verifies that both adapters produce structurally equivalent results for the same
/// scenario: disabled (reconnect_required), enabled (success + event ref), and
/// auth-error detection. Uses CalendarProviderParityPolicy to enforce shape contracts.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CalendarGoogleParityIntegrationTests
{
    private static readonly CalendarProviderParityPolicy Policy = new();

    private static ProviderCreateRequest MakeRequest() => new(
        Title: "Team sync",
        StartUtc: DateTimeOffset.UtcNow.AddHours(1),
        EndUtc: DateTimeOffset.UtcNow.AddHours(2),
        Timezone: "America/New_York",
        ProviderAccountRef: "user@example.com",
        CorrelationId: Guid.NewGuid().ToString());

    // ── Disabled provider behavior ─────────────────────────────────────────

    [Fact]
    public async Task Google_disabled_returns_reconnect_required()
    {
        var provider = BuildGoogleProvider(enabled: false);
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.True(result.ReconnectRequired);
        Assert.Null(result.ProviderEventRef);
    }

    [Fact]
    public async Task Outlook_disabled_returns_reconnect_required()
    {
        var provider = BuildOutlookProvider(enabled: false);
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.True(result.ReconnectRequired);
        Assert.Null(result.ProviderEventRef);
    }

    [Fact]
    public async Task Both_providers_disabled_produce_parity_valid_results()
    {
        var google = await BuildGoogleProvider(enabled: false).CreateEventAsync(MakeRequest());
        var outlook = await BuildOutlookProvider(enabled: false).CreateEventAsync(MakeRequest());

        Assert.True(Policy.Validate(google).IsValid, string.Join("; ", Policy.Validate(google).Violations));
        Assert.True(Policy.Validate(outlook).IsValid, string.Join("; ", Policy.Validate(outlook).Violations));
    }

    // ── Enabled provider behavior ──────────────────────────────────────────

    [Fact]
    public async Task Google_enabled_returns_success_with_event_ref()
    {
        var provider = BuildGoogleProvider(enabled: true);
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.True(result.Success);
        Assert.False(result.ReconnectRequired);
        Assert.NotNull(result.ProviderEventRef);
        Assert.StartsWith("google-event-", result.ProviderEventRef);
    }

    [Fact]
    public async Task Outlook_enabled_returns_success_with_event_ref()
    {
        var provider = BuildOutlookProvider(enabled: true);
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.True(result.Success);
        Assert.False(result.ReconnectRequired);
        Assert.NotNull(result.ProviderEventRef);
        Assert.StartsWith("outlook-event-", result.ProviderEventRef);
    }

    [Fact]
    public async Task Both_providers_enabled_produce_parity_valid_results()
    {
        var google = await BuildGoogleProvider(enabled: true).CreateEventAsync(MakeRequest());
        var outlook = await BuildOutlookProvider(enabled: true).CreateEventAsync(MakeRequest());

        Assert.True(Policy.Validate(google).IsValid, string.Join("; ", Policy.Validate(google).Violations));
        Assert.True(Policy.Validate(outlook).IsValid, string.Join("; ", Policy.Validate(outlook).Violations));
    }

    // ── Provider identity ──────────────────────────────────────────────────

    [Fact]
    public void Google_provider_reports_correct_provider_enum()
    {
        Assert.Equal(CalendarProvider.Google, BuildGoogleProvider(enabled: true).Provider);
    }

    [Fact]
    public void Outlook_provider_reports_correct_provider_enum()
    {
        Assert.Equal(CalendarProvider.Outlook, BuildOutlookProvider(enabled: true).Provider);
    }

    [Fact]
    public void Provider_enum_values_are_distinct()
    {
        Assert.NotEqual(BuildGoogleProvider(true).Provider, BuildOutlookProvider(true).Provider);
    }

    // ── Idempotency: each call produces a unique event ref ────────────────

    [Fact]
    public async Task Google_each_create_produces_unique_event_ref()
    {
        var provider = BuildGoogleProvider(enabled: true);
        var r1 = await provider.CreateEventAsync(MakeRequest());
        var r2 = await provider.CreateEventAsync(MakeRequest());

        Assert.NotEqual(r1.ProviderEventRef, r2.ProviderEventRef);
    }

    [Fact]
    public async Task Outlook_each_create_produces_unique_event_ref()
    {
        var provider = BuildOutlookProvider(enabled: true);
        var r1 = await provider.CreateEventAsync(MakeRequest());
        var r2 = await provider.CreateEventAsync(MakeRequest());

        Assert.NotEqual(r1.ProviderEventRef, r2.ProviderEventRef);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static GoogleCalendarProvider BuildGoogleProvider(bool enabled)
    {
        var opts = Options.Create(new Host.Calendar.CalendarOptions
        {
            Google = new Host.Calendar.GoogleProviderOptions { Enabled = enabled }
        });
        return new GoogleCalendarProvider(opts, NullLogger<GoogleCalendarProvider>.Instance);
    }

    private static OutlookCalendarProvider BuildOutlookProvider(bool enabled)
    {
        var opts = Options.Create(new Host.Calendar.CalendarOptions
        {
            Outlook = new Host.Calendar.OutlookProviderOptions { Enabled = enabled }
        });
        return new OutlookCalendarProvider(opts, NullLogger<OutlookCalendarProvider>.Instance);
    }
}
