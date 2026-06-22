using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Audit;
using Aluki.Runtime.Calendar.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Integration tests for token confidentiality and audit writer isolation (T039,
/// FR-008a, FR-011, SC-010). Verifies that token material is never present in
/// user-visible output, that ProviderTokenBoundary.ToString() is always redacted,
/// that TokenRedactionPolicy strips all sensitive fields, and that audit writer
/// silently isolates errors rather than propagating them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CalendarSecurityAndAuditIntegrationTests
{
    // ── ProviderTokenBoundary ──────────────────────────────────────────────

    [Fact]
    public void ProviderTokenBoundary_ToString_returns_REDACTED()
    {
        var boundary = ProviderTokenBoundary.Wrap("super-secret-token-abc123");
        Assert.Equal("[REDACTED]", boundary.ToString());
    }

    [Fact]
    public void ProviderTokenBoundary_string_interpolation_is_redacted()
    {
        var boundary = ProviderTokenBoundary.Wrap("secret");
        var formatted = $"Token value: {boundary}";
        Assert.Equal("Token value: [REDACTED]", formatted);
        Assert.DoesNotContain("secret", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderTokenBoundary_Unwrap_returns_raw_material()
    {
        const string raw = "eyJhbGciOiJSUzI1NiJ9.payload.signature";
        var boundary = ProviderTokenBoundary.Wrap(raw);
        Assert.Equal(raw, boundary.Unwrap());
    }

    // ── TokenRedactionPolicy.Redact ────────────────────────────────────────

    [Theory]
    [InlineData("access_token")]
    [InlineData("refresh_token")]
    [InlineData("client_secret")]
    [InlineData("code")]
    [InlineData("id_token")]
    public void Redact_removes_sensitive_field_values(string fieldName)
    {
        var json = $@"{{""tenant"":""abc"",""{fieldName}"":""super-secret"",""scope"":""calendars""}}";
        var redacted = TokenRedactionPolicy.Redact(json);

        Assert.DoesNotContain("super-secret", redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
        Assert.Contains(fieldName, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_preserves_non_sensitive_fields()
    {
        const string json = @"{""tenant_id"":""abc123"",""access_token"":""tok"",""scope"":""calendars""}";
        var redacted = TokenRedactionPolicy.Redact(json);

        Assert.Contains("tenant_id", redacted, StringComparison.Ordinal);
        Assert.Contains("abc123", redacted, StringComparison.Ordinal);
        Assert.Contains("scope", redacted, StringComparison.Ordinal);
        Assert.Contains("calendars", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_handles_empty_string()
    {
        var result = TokenRedactionPolicy.Redact("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Redact_handles_json_with_no_sensitive_fields()
    {
        const string json = @"{""tenant_id"":""abc"",""provider"":""google""}";
        var result = TokenRedactionPolicy.Redact(json);
        Assert.Equal(json, result);
    }

    // ── TokenRedactionPolicy.RedactDictionary ──────────────────────────────

    [Fact]
    public void RedactDictionary_masks_all_sensitive_keys()
    {
        var payload = new Dictionary<string, object?>
        {
            ["tenant_id"] = "abc",
            ["access_token"] = "tok1",
            ["refresh_token"] = "tok2",
            ["client_secret"] = "sec",
            ["code"] = "auth-code",
            ["id_token"] = "id-tok"
        };

        var redacted = TokenRedactionPolicy.RedactDictionary(payload);

        Assert.Equal("[REDACTED]", redacted["access_token"]);
        Assert.Equal("[REDACTED]", redacted["refresh_token"]);
        Assert.Equal("[REDACTED]", redacted["client_secret"]);
        Assert.Equal("[REDACTED]", redacted["code"]);
        Assert.Equal("[REDACTED]", redacted["id_token"]);
        Assert.Equal("abc", redacted["tenant_id"]);
    }

    [Fact]
    public void RedactDictionary_is_case_insensitive()
    {
        var payload = new Dictionary<string, object?>
        {
            ["ACCESS_TOKEN"] = "upper-case-token",
            ["Refresh_Token"] = "mixed-case-token"
        };

        var redacted = TokenRedactionPolicy.RedactDictionary(payload);

        Assert.Equal("[REDACTED]", redacted["ACCESS_TOKEN"]);
        Assert.Equal("[REDACTED]", redacted["Refresh_Token"]);
    }

    // ── TokenRedactionPolicy.SerializeRedacted ─────────────────────────────

    [Fact]
    public void SerializeRedacted_null_produces_empty_object()
    {
        var result = TokenRedactionPolicy.SerializeRedacted(null);
        Assert.Equal("{}", result);
    }

    [Fact]
    public void SerializeRedacted_strips_token_from_anonymous_object()
    {
        var payload = new { tenant_id = "abc", access_token = "secret-tok", scope = "calendars" };
        var result = TokenRedactionPolicy.SerializeRedacted(payload);

        Assert.DoesNotContain("secret-tok", result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
        Assert.Contains("abc", result, StringComparison.Ordinal);
    }

    // ── CalendarAuditWriter isolation ──────────────────────────────────────

    [Fact]
    public async Task AuditWriter_does_not_throw_when_repository_fails()
    {
        var writer = new CalendarAuditWriter(
            new ThrowingAuditRepository(),
            NullLogger<CalendarAuditWriter>.Instance);

        // Should not propagate exception from the failing repository
        var exception = await Record.ExceptionAsync(() =>
            writer.WriteAsync(
                eventName: "test_event",
                tenantId: Guid.NewGuid(),
                contextId: Guid.NewGuid(),
                userId: Guid.NewGuid(),
                provider: CalendarProvider.Google,
                skillName: "CalendarSecurityAndAuditIntegrationTests",
                result: "test",
                outcomeRef: null,
                correlationId: Guid.NewGuid().ToString(),
                payload: new { key = "value" }));

        Assert.Null(exception);
    }

    // ── Stubs ──────────────────────────────────────────────────────────────

    private sealed class ThrowingAuditRepository : ICalendarAuditRepository
    {
        public Task AppendAsync(CalendarAuditRecord record, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
