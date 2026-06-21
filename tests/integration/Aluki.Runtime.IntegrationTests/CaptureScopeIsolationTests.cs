using System.Net;
using System.Net.Http.Json;
using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Cross-tenant isolation and denial-with-audit tests (T020). Verifies scoped
/// persistence isolation and that unauthorized scope is denied before side
/// effects and audited (FR-005, FR-006, FR-014, SC-004).
/// Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class CaptureScopeIsolationTests
{
    private const string Route = "/api/channels/whatsapp/inbound";
    private readonly DbCaptureFixture _fixture;

    public CaptureScopeIsolationTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Captured_artifacts_are_isolated_per_tenant()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var tenantA = await _fixture.SeedPrincipalAsync();
        var tenantB = await _fixture.SeedPrincipalAsync();
        var client = _fixture.Factory.CreateClient();
        var providerA = $"wamid-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(Route, new
        {
            provider_message_id = providerA,
            source_channel = "whatsapp",
            sender = new { external_user_id = tenantA.ExternalId },
            context_metadata = new { tenant_hint = tenantA.TenantId, context_id = tenantA.ContextId },
            payload = new { type = "text", text = "tenant A message" },
            occurred_at_utc = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await _fixture.CountMessagesAsync(tenantA.TenantId, providerA));
        Assert.Equal(0, await _fixture.CountMessagesAsync(tenantB.TenantId, providerA));
    }

    [Fact]
    public async Task Unauthorized_context_is_denied_and_audited()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var client = _fixture.Factory.CreateClient();
        var unauthorizedContext = Guid.NewGuid(); // not granted to the sender

        var response = await client.PostAsJsonAsync(Route, new
        {
            provider_message_id = $"wamid-{Guid.NewGuid():N}",
            source_channel = "whatsapp",
            sender = new { external_user_id = seed.ExternalId },
            context_metadata = new { tenant_hint = seed.TenantId, context_id = unauthorizedContext },
            payload = new { type = "text", text = "should be denied" },
            occurred_at_utc = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CaptureError>();
        Assert.Equal(CaptureErrorCode.ScopeDenied, error!.Code);

        // SC-004: the denial produced an auditable record under the tenant scope.
        Assert.True(await _fixture.CountAuditAsync(seed.TenantId, CaptureAuditEvent.ScopeDenied) >= 1);
    }
}
