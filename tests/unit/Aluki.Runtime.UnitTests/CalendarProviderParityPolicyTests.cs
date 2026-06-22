using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Skills;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for CalendarProviderParityPolicy (T034, FR-010, SC-008, SC-010).
/// Verifies that the policy correctly detects adapter contract violations:
/// success without event ref, failure with event ref, success+reconnect,
/// and error messages containing raw token material.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CalendarProviderParityPolicyTests
{
    private readonly CalendarProviderParityPolicy _policy = new();

    // ── Valid results ──────────────────────────────────────────────────────

    [Fact]
    public void Success_with_event_ref_is_valid()
    {
        var result = new ProviderCreateResult(Success: true, ProviderEventRef: "evt-123", ReconnectRequired: false, ErrorMessage: null);
        var diagnostic = _policy.Validate(result);

        Assert.True(diagnostic.IsValid);
        Assert.Empty(diagnostic.Violations);
    }

    [Fact]
    public void Failure_without_event_ref_is_valid()
    {
        var result = new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: false, ErrorMessage: "Something failed.");
        var diagnostic = _policy.Validate(result);

        Assert.True(diagnostic.IsValid);
        Assert.Empty(diagnostic.Violations);
    }

    [Fact]
    public void Reconnect_required_failure_without_event_ref_is_valid()
    {
        var result = new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: true, ErrorMessage: "Token expired.");
        var diagnostic = _policy.Validate(result);

        Assert.True(diagnostic.IsValid);
        Assert.Empty(diagnostic.Violations);
    }

    // ── Contract violations ────────────────────────────────────────────────

    [Fact]
    public void Success_without_event_ref_is_invalid()
    {
        var result = new ProviderCreateResult(Success: true, ProviderEventRef: null, ReconnectRequired: false, ErrorMessage: null);
        var diagnostic = _policy.Validate(result);

        Assert.False(diagnostic.IsValid);
        Assert.Single(diagnostic.Violations);
    }

    [Fact]
    public void Failure_with_event_ref_is_invalid()
    {
        var result = new ProviderCreateResult(Success: false, ProviderEventRef: "evt-123", ReconnectRequired: false, ErrorMessage: "Error.");
        var diagnostic = _policy.Validate(result);

        Assert.False(diagnostic.IsValid);
        Assert.Single(diagnostic.Violations);
    }

    [Fact]
    public void Success_and_reconnect_required_is_invalid()
    {
        var result = new ProviderCreateResult(Success: true, ProviderEventRef: "evt-123", ReconnectRequired: true, ErrorMessage: null);
        var diagnostic = _policy.Validate(result);

        Assert.False(diagnostic.IsValid);
        Assert.Single(diagnostic.Violations);
    }

    // ── Token leakage detection ────────────────────────────────────────────

    [Theory]
    [InlineData("access_token", "access_token=eyJhbG...")]
    [InlineData("refresh_token", "refresh_token value is abc123")]
    [InlineData("client_secret", "client_secret is exposed")]
    [InlineData("id_token", "id_token: eyJhbG...")]
    [InlineData("Bearer ", "Authorization: Bearer eyJhbG...")]
    public void Error_message_with_token_material_is_invalid(string _, string errorMessage)
    {
        var result = new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: false, ErrorMessage: errorMessage);
        var diagnostic = _policy.Validate(result);

        Assert.False(diagnostic.IsValid);
        Assert.Contains(diagnostic.Violations, v => v.Contains("sensitive token material"));
    }

    [Fact]
    public void Benign_error_message_without_token_material_is_valid()
    {
        var result = new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: false,
            ErrorMessage: "Authorization expired or revoked. Please reconnect your account.");
        var diagnostic = _policy.Validate(result);

        Assert.True(diagnostic.IsValid);
    }

    // ── Multiple violations ────────────────────────────────────────────────

    [Fact]
    public void Multiple_violations_are_all_reported()
    {
        // Success=true, no event ref, AND reconnect_required=true — two violations
        var result = new ProviderCreateResult(Success: true, ProviderEventRef: null, ReconnectRequired: true, ErrorMessage: null);
        var diagnostic = _policy.Validate(result);

        Assert.False(diagnostic.IsValid);
        Assert.True(diagnostic.Violations.Count >= 2);
    }
}
