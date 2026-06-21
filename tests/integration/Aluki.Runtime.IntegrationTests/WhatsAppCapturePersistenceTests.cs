using System.Net;
using System.Net.Http.Json;
using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Database-backed persistence and duplicate-suppression tests (T012). Asserts a
/// single canonical artifact per unique provider message and zero extra artifacts
/// on redelivery (FR-001..FR-004, FR-013, SC-002, SC-003, SC-008).
/// Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class WhatsAppCapturePersistenceTests
{
    private const string Route = "/api/channels/whatsapp/inbound";
    private readonly DbCaptureFixture _fixture;

    public WhatsAppCapturePersistenceTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Persists_single_canonical_message_and_suppresses_duplicates()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var client = _fixture.Factory.CreateClient();
        var providerMessageId = $"wamid-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync(Route, TextPayload(seed, providerMessageId));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var ack = await first.Content.ReadFromJsonAsync<CaptureAck>();
        Assert.Equal(CaptureStatus.Accepted, ack!.Status);
        Assert.Equal(1, await _fixture.CountMessagesAsync(seed.TenantId, providerMessageId));

        // Redelivery of the same provider message id must not create a new artifact.
        var duplicate = await client.PostAsJsonAsync(Route, TextPayload(seed, providerMessageId));
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        var duplicateAck = await duplicate.Content.ReadFromJsonAsync<CaptureAck>();
        Assert.Equal(CaptureStatus.DuplicateSuppressed, duplicateAck!.Status);
        Assert.Equal(1, await _fixture.CountMessagesAsync(seed.TenantId, providerMessageId));
    }

    [Fact]
    public async Task Persists_media_artifact_for_image()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var client = _fixture.Factory.CreateClient();
        var providerMessageId = $"wamid-{Guid.NewGuid():N}";

        var payload = new
        {
            provider_message_id = providerMessageId,
            source_channel = "whatsapp",
            sender = new { external_user_id = seed.ExternalId },
            context_metadata = new { tenant_hint = seed.TenantId, context_id = seed.ContextId },
            payload = new
            {
                type = "image",
                media = new { media_type = "image", content_type = "image/jpeg", provider_media_id = "m1" }
            },
            occurred_at_utc = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync(Route, payload);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await _fixture.CountMessagesAsync(seed.TenantId, providerMessageId));
        Assert.True(await _fixture.CountMediaAsync(seed.TenantId) >= 1);
    }

    private static object TextPayload(SeededPrincipal seed, string providerMessageId) => new
    {
        provider_message_id = providerMessageId,
        source_channel = "whatsapp",
        sender = new { external_user_id = seed.ExternalId },
        context_metadata = new { tenant_hint = seed.TenantId, context_id = seed.ContextId },
        payload = new { type = "text", text = "hola" },
        occurred_at_utc = DateTimeOffset.UtcNow
    };
}
