using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Ingestion;
using Aluki.Runtime.Memory.Persistence;
using Aluki.Runtime.Memory.Recall;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Capture→memory bridge: WhatsApp messages promoted through the
/// <see cref="MemoryIngestionSink"/> become recall-able artifacts, dedupe by
/// provider message id, and corroborate across deliveries. Uses a deterministic
/// stub embedder (AI-independent). Skipped unless ALUKI_TEST_POSTGRES is set.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class WhatsAppMemoryBridgeIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public WhatsAppMemoryBridgeIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Captured_whatsapp_messages_become_recallable_and_dedupe()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var store = new MemoryStore(BuildFactory(_fixture.ConnectionString!));
        var sink = new MemoryIngestionSink(new StubEmbeddingClient(3), store, NullLogger<MemoryIngestionSink>.Instance);

        var wamid1 = $"wamid-{Guid.NewGuid():N}";
        var wamid2 = $"wamid-{Guid.NewGuid():N}";

        await sink.IngestAsync(Item(seed, wamid1, "cita dentista martes"), CancellationToken.None);
        await sink.IngestAsync(Item(seed, wamid2, "dentista el martes 4pm"), CancellationToken.None);
        // Repeat delivery of wamid1 must be suppressed (idempotent on source identity).
        await sink.IngestAsync(Item(seed, wamid1, "cita dentista martes"), CancellationToken.None);

        var principal = new PrincipalScope(seed.TenantId, seed.ContextId, seed.UserId, null);
        var results = await store.SearchAsync(principal, Vec(3), 10, CancellationToken.None);

        // Only two distinct artifacts despite three ingestions.
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("whatsapp", r.SourceChannel));

        var corroboration = CorroborationPolicy.Evaluate(results, maxDistance: 0.6);
        Assert.Equal(RecallDecision.Confirmed, corroboration.Decision);
    }

    private static MemoryIngestionItem Item(SeededPrincipal seed, string wamid, string text) => new(
        TenantId: seed.TenantId,
        ContextId: seed.ContextId,
        UserId: seed.UserId,
        SourceChannel: "whatsapp",
        SourceIdentity: wamid,
        ContentText: text,
        ProvenanceRef: $"whatsapp:{wamid}",
        CorrelationId: "corr");

    private static float[] Vec(int index)
    {
        var v = new float[1536];
        v[index] = 1f;
        return v;
    }

    private static NpgsqlConnectionFactory BuildFactory(string connectionString) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = connectionString })
            .Build());

    /// <summary>Deterministic embedder: every text maps to the same unit vector.</summary>
    private sealed class StubEmbeddingClient(int index) : IEmbeddingClient
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            var v = new float[1536];
            v[index] = 1f;
            return Task.FromResult(v);
        }
    }
}
