using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Contract tests for calendar authorization lifecycle endpoints (T012).
/// Validates response shapes for connect, disconnect, and callback paths
/// without requiring a live database — the default scope guard always permits,
/// and the skill stubs fail fast on missing DB with a non-2xx response.
/// </summary>
[Trait("Category", "Contract")]
public sealed class CalendarAuthorizationContractTests : IClassFixture<CaptureWebApplicationFactory>
{
    private readonly CaptureWebApplicationFactory _factory;

    public CalendarAuthorizationContractTests(CaptureWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── /api/calendar/connect ──────────────────────────────────────────────

    [Fact]
    public async Task Connect_missing_body_returns_400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/calendar/connect", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Connect_invalid_provider_returns_400_with_error_field()
    {
        var client = _factory.CreateClient();
        var body = new
        {
            tenant_id = Guid.NewGuid(),
            context_id = Guid.NewGuid(),
            user_id = Guid.NewGuid(),
            provider = "unknown_provider"
        };

        var response = await client.PostAsJsonAsync("/api/calendar/connect", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_provider", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Connect_empty_tenant_returns_400_with_missing_fields()
    {
        var client = _factory.CreateClient();
        var body = new
        {
            tenant_id = Guid.Empty,
            context_id = Guid.NewGuid(),
            user_id = Guid.NewGuid(),
            provider = "outlook"
        };

        var response = await client.PostAsJsonAsync("/api/calendar/connect", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("missing_fields", json.GetProperty("error").GetString());
    }

    // ── /api/calendar/callback ─────────────────────────────────────────────

    [Fact]
    public async Task Callback_missing_state_returns_400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/calendar/callback?code=abc&provider=outlook");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("missing_params", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Callback_missing_code_returns_400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/calendar/callback?state=abc&provider=outlook");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("missing_params", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Callback_invalid_provider_returns_400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/calendar/callback?state=abc&code=xyz&provider=unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_provider", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Callback_provider_error_param_returns_400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/calendar/callback?error=access_denied&provider=outlook");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("provider_error", json.GetProperty("error").GetString());
        Assert.Equal("access_denied", json.GetProperty("provider_error").GetString());
    }

    // ── /api/calendar/disconnect ───────────────────────────────────────────

    [Fact]
    public async Task Disconnect_invalid_provider_returns_400()
    {
        var client = _factory.CreateClient();
        var body = new
        {
            tenant_id = Guid.NewGuid(),
            context_id = Guid.NewGuid(),
            user_id = Guid.NewGuid(),
            provider = "invalid"
        };

        var response = await client.PostAsJsonAsync("/api/calendar/disconnect", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_provider", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Disconnect_empty_user_id_returns_400()
    {
        var client = _factory.CreateClient();
        var body = new
        {
            tenant_id = Guid.NewGuid(),
            context_id = Guid.NewGuid(),
            user_id = Guid.Empty,
            provider = "google"
        };

        var response = await client.PostAsJsonAsync("/api/calendar/disconnect", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
