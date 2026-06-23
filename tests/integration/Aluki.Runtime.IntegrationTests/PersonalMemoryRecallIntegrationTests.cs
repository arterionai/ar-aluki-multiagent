using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Persistence;
using Aluki.Runtime.Memory.Recall;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-002 US2: vector search + corroboration over scoped, non-deleted artifacts
/// (T019). Uses synthetic embeddings (AI-independent) to exercise pgvector search
/// and the corroboration gate. Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class PersonalMemoryRecallIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public PersonalMemoryRecallIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Vector_search_corroborates_two_similar_artifacts()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var principal = new PrincipalScope(seed.TenantId, seed.ContextId, seed.UserId, null);
        var store = new MemoryStore(BuildFactory(_fixture.ConnectionString!));

        var topic = Vec(3);     // two artifacts share this vector
        var unrelated = Vec(700); // orthogonal

        await store.CaptureNoteAsync(principal, "whatsapp", $"a1-{Guid.NewGuid():N}", "cita dentista martes", topic, "p", "c", CancellationToken.None);
        await store.CaptureNoteAsync(principal, "whatsapp", $"a2-{Guid.NewGuid():N}", "dentista el martes 4pm", topic, "p", "c", CancellationToken.None);
        await store.CaptureNoteAsync(principal, "whatsapp", $"b1-{Guid.NewGuid():N}", "comprar pan", unrelated, "p", "c", CancellationToken.None);

        var results = await store.SearchAsync(principal, topic, 5, CancellationToken.None);
        var corroboration = CorroborationPolicy.Evaluate(results, maxDistance: 0.6);

        Assert.Equal(RecallDecision.Confirmed, corroboration.Decision);
        Assert.Equal(2, corroboration.Relevant.Count);
        Assert.All(corroboration.Relevant, c => Assert.True(c.Distance <= 0.6));
    }

    [Fact]
    public async Task No_relevant_evidence_yields_none()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var principal = new PrincipalScope(seed.TenantId, seed.ContextId, seed.UserId, null);
        var store = new MemoryStore(BuildFactory(_fixture.ConnectionString!));

        await store.CaptureNoteAsync(principal, "whatsapp", $"x-{Guid.NewGuid():N}", "nota", Vec(10), "p", "c", CancellationToken.None);

        var results = await store.SearchAsync(principal, Vec(900), 5, CancellationToken.None);
        var corroboration = CorroborationPolicy.Evaluate(results, maxDistance: 0.6);

        Assert.Equal(RecallDecision.None, corroboration.Decision);
    }

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
}
