using System.Net;
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
/// scenario: disabled (reconnect_required), not-connected (reconnect_required),
/// connected+success (provider event ref), and auth-error (reconnect_required).
/// Provider HTTP calls are stubbed; no live network or database is required.
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
        TenantId: Guid.NewGuid(),
        ContextId: Guid.NewGuid(),
        UserId: Guid.NewGuid(),
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

    // ── No stored token → reconnect required ───────────────────────────────

    [Fact]
    public async Task Google_enabled_without_token_returns_reconnect_required()
    {
        var provider = BuildGoogleProvider(enabled: true, accessToken: null);
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.True(result.ReconnectRequired);
    }

    [Fact]
    public async Task Outlook_enabled_without_token_returns_reconnect_required()
    {
        var provider = BuildOutlookProvider(enabled: true, accessToken: null);
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.True(result.ReconnectRequired);
    }

    // ── Connected + provider accepts → success with event ref ──────────────

    [Fact]
    public async Task Google_connected_returns_success_with_event_ref()
    {
        var provider = BuildGoogleProvider(enabled: true,
            response: (HttpStatusCode.OK, """{"id":"google-evt-123"}"""));
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.True(result.Success);
        Assert.False(result.ReconnectRequired);
        Assert.Equal("google-evt-123", result.ProviderEventRef);
    }

    [Fact]
    public async Task Outlook_connected_returns_success_with_event_ref()
    {
        var provider = BuildOutlookProvider(enabled: true,
            response: (HttpStatusCode.Created, """{"id":"outlook-evt-456"}"""));
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.True(result.Success);
        Assert.False(result.ReconnectRequired);
        Assert.Equal("outlook-evt-456", result.ProviderEventRef);
    }

    [Fact]
    public async Task Both_providers_connected_produce_parity_valid_results()
    {
        var google = await BuildGoogleProvider(enabled: true,
            response: (HttpStatusCode.OK, """{"id":"g1"}""")).CreateEventAsync(MakeRequest());
        var outlook = await BuildOutlookProvider(enabled: true,
            response: (HttpStatusCode.Created, """{"id":"o1"}""")).CreateEventAsync(MakeRequest());

        Assert.True(Policy.Validate(google).IsValid, string.Join("; ", Policy.Validate(google).Violations));
        Assert.True(Policy.Validate(outlook).IsValid, string.Join("; ", Policy.Validate(outlook).Violations));
    }

    // ── Authorization failure → reconnect required ─────────────────────────

    [Fact]
    public async Task Google_unauthorized_returns_reconnect_required()
    {
        var provider = BuildGoogleProvider(enabled: true,
            response: (HttpStatusCode.Unauthorized, """{"error":"invalid_token"}"""));
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.True(result.ReconnectRequired);
        Assert.Null(result.ProviderEventRef);
    }

    [Fact]
    public async Task Outlook_unauthorized_returns_reconnect_required()
    {
        var provider = BuildOutlookProvider(enabled: true,
            response: (HttpStatusCode.Unauthorized, """{"error":"InvalidAuthenticationToken"}"""));
        var result = await provider.CreateEventAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.True(result.ReconnectRequired);
        Assert.Null(result.ProviderEventRef);
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

    // ── Helpers ────────────────────────────────────────────────────────────

    private static GoogleCalendarProvider BuildGoogleProvider(
        bool enabled,
        string? accessToken = "access-xyz",
        (HttpStatusCode Status, string Json)? response = null)
    {
        var opts = Options.Create(new Host.Calendar.CalendarOptions
        {
            Google = new Host.Calendar.GoogleProviderOptions { Enabled = enabled }
        });
        var r = response ?? (HttpStatusCode.OK, """{"id":"g"}""");
        var http = new HttpClient(new StubHttpMessageHandler(r.Status, r.Json));
        return new GoogleCalendarProvider(http, new FakeCalendarTokenService(accessToken), opts, NullLogger<GoogleCalendarProvider>.Instance);
    }

    private static OutlookCalendarProvider BuildOutlookProvider(
        bool enabled,
        string? accessToken = "access-xyz",
        (HttpStatusCode Status, string Json)? response = null)
    {
        var opts = Options.Create(new Host.Calendar.CalendarOptions
        {
            Outlook = new Host.Calendar.OutlookProviderOptions { Enabled = enabled }
        });
        var r = response ?? (HttpStatusCode.Created, """{"id":"o"}""");
        var http = new HttpClient(new StubHttpMessageHandler(r.Status, r.Json));
        return new OutlookCalendarProvider(http, new FakeCalendarTokenService(accessToken), opts, NullLogger<OutlookCalendarProvider>.Instance);
    }
}
