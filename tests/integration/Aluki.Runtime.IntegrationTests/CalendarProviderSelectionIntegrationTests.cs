using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Skills;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Integration tests for CalendarProviderSelectionSkill (T033, FR-006, SC-007).
/// Verifies explicit hint routing, default-provider fallback, and deterministic
/// tie-break when multiple connections exist.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CalendarProviderSelectionIntegrationTests
{
    private readonly CalendarProviderSelectionSkill _skill = new();

    private static CalendarConnectionRecord Outlook(bool isDefault = false) => new(
        CalendarConnectionId: Guid.NewGuid(),
        TenantId: Guid.NewGuid(),
        ContextId: Guid.NewGuid(),
        UserId: Guid.NewGuid(),
        Provider: CalendarProvider.Outlook,
        Status: ConnectionStatus.Connected,
        ConnectedAtUtc: DateTimeOffset.UtcNow,
        DisconnectedAtUtc: null,
        ProviderAccountRef: "user@outlook.com",
        DefaultForUser: isDefault,
        CorrelationId: Guid.NewGuid().ToString());

    private static CalendarConnectionRecord Google(bool isDefault = false) => new(
        CalendarConnectionId: Guid.NewGuid(),
        TenantId: Guid.NewGuid(),
        ContextId: Guid.NewGuid(),
        UserId: Guid.NewGuid(),
        Provider: CalendarProvider.Google,
        Status: ConnectionStatus.Connected,
        ConnectedAtUtc: DateTimeOffset.UtcNow,
        DisconnectedAtUtc: null,
        ProviderAccountRef: "user@gmail.com",
        DefaultForUser: isDefault,
        CorrelationId: Guid.NewGuid().ToString());

    // ── Explicit hint routing ──────────────────────────────────────────────

    [Fact]
    public void Explicit_outlook_hint_selects_outlook()
    {
        var connections = new[] { Outlook(), Google() };
        var result = _skill.Select("outlook", connections);

        Assert.True(result.HasProvider);
        Assert.Equal(CalendarProvider.Outlook, result.SelectedProvider);
        Assert.Equal(SelectionReason.ExplicitRequest, result.Reason);
    }

    [Fact]
    public void Explicit_google_hint_selects_google()
    {
        var connections = new[] { Outlook(), Google() };
        var result = _skill.Select("google", connections);

        Assert.True(result.HasProvider);
        Assert.Equal(CalendarProvider.Google, result.SelectedProvider);
        Assert.Equal(SelectionReason.ExplicitRequest, result.Reason);
    }

    // ── Default provider fallback ──────────────────────────────────────────

    [Fact]
    public void No_hint_with_default_connection_selects_default()
    {
        var connections = new[] { Outlook(isDefault: false), Google(isDefault: true) };
        var result = _skill.Select(null, connections);

        Assert.True(result.HasProvider);
        Assert.Equal(CalendarProvider.Google, result.SelectedProvider);
        Assert.Equal(SelectionReason.UserDefault, result.Reason);
    }

    [Fact]
    public void Outlook_as_default_is_selected_when_no_hint()
    {
        var connections = new[] { Outlook(isDefault: true), Google(isDefault: false) };
        var result = _skill.Select(null, connections);

        Assert.True(result.HasProvider);
        Assert.Equal(CalendarProvider.Outlook, result.SelectedProvider);
        Assert.Equal(SelectionReason.UserDefault, result.Reason);
    }

    // ── Deterministic tie-break ────────────────────────────────────────────

    [Fact]
    public void No_hint_no_default_falls_back_to_deterministic_tiebreak()
    {
        var connections = new[] { Outlook(isDefault: false), Google(isDefault: false) };
        var result = _skill.Select(null, connections);

        Assert.True(result.HasProvider);
        Assert.Equal(SelectionReason.DeterministicTiebreak, result.Reason);
    }

    // ── No connections ─────────────────────────────────────────────────────

    [Fact]
    public void Empty_connections_returns_no_provider()
    {
        var result = _skill.Select(null, []);

        Assert.False(result.HasProvider);
    }

    [Fact]
    public void Hint_with_no_matching_connection_returns_no_provider()
    {
        var connections = new[] { Outlook() }; // only Outlook, hint asks for Google
        var result = _skill.Select("google", connections);

        Assert.False(result.HasProvider);
    }

    // ── Single connection ──────────────────────────────────────────────────

    [Fact]
    public void Single_connection_is_selected_when_no_hint()
    {
        var connections = new[] { Outlook() };
        var result = _skill.Select(null, connections);

        Assert.True(result.HasProvider);
        Assert.Equal(CalendarProvider.Outlook, result.SelectedProvider);
    }

    [Fact]
    public void Single_google_connection_is_selected_when_no_hint()
    {
        var connections = new[] { Google() };
        var result = _skill.Select(null, connections);

        Assert.True(result.HasProvider);
        Assert.Equal(CalendarProvider.Google, result.SelectedProvider);
    }
}
