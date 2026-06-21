using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Xunit;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Contract tests for POST /api/channels/whatsapp/inbound validating the 400
/// rejection response shape and required fields per the OpenAPI contract (T011).
/// The 202 accepted/duplicate/unsupported paths require a scoped PostgreSQL and
/// are validated in the integration suite.
/// </summary>
[Trait("Category", "Contract")]
public sealed class WhatsAppInboundContractTests : IClassFixture<CaptureWebApplicationFactory>
{
    private const string Route = "/api/channels/whatsapp/inbound";
    private readonly CaptureWebApplicationFactory _factory;

    public WhatsAppInboundContractTests(CaptureWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Missing_provider_message_id_returns_400_error_contract()
    {
        var client = _factory.CreateClient();
        var payload = new
        {
            source_channel = "whatsapp",
            sender = new { external_user_id = "5215555555555" },
            payload = new { type = "text", text = "hi" },
            occurred_at_utc = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync(Route, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CaptureError>();
        Assert.NotNull(error);
        Assert.Equal(CaptureStatus.Rejected, error!.Status);
        Assert.Equal(CaptureErrorCode.InvalidPayload, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.CorrelationId));
    }

    [Fact]
    public async Task Wrong_source_channel_returns_400()
    {
        var client = _factory.CreateClient();
        var payload = new
        {
            provider_message_id = "wamid-1",
            source_channel = "telegram",
            sender = new { external_user_id = "5215555555555" },
            payload = new { type = "text", text = "hi" },
            occurred_at_utc = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync(Route, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_payload_type_returns_400()
    {
        var client = _factory.CreateClient();
        var payload = new
        {
            provider_message_id = "wamid-1",
            source_channel = "whatsapp",
            sender = new { external_user_id = "5215555555555" },
            payload = new { type = "video-call" },
            occurred_at_utc = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync(Route, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Error_response_uses_snake_case_property_names()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(Route, content);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("correlation_id", out _));
        Assert.True(document.RootElement.TryGetProperty("code", out _));
    }
}
