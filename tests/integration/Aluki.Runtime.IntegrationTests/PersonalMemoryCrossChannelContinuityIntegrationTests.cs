using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Persistence;
using Aluki.Runtime.Memory.Recall;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-002 US3 (T029): cross-channel memory continuity within tenant/context.
/// Captures related notes on different channels (whatsapp + telegram) and verifies
/// a single query recalls both — retrieval is scoped by context, never by channel —
/// and that the corroborated evidence is reported as cross-channel.
/// Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class PersonalMemoryCrossChannelContinuityIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public PersonalMemoryCrossChannelContinuityIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Recall_unifies_evidence_across_channels()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var principal = new PrincipalScope(seed.TenantId, seed.ContextId, seed.UserId, null);
        var store = new MemoryStore(BuildFactory(_fixture.ConnectionString!));

        var topic = Vec(11);

        await store.CaptureNoteAsync(principal, "whatsapp", $"w-{Guid.NewGuid():N}", "cita dentista martes", topic, "whatsapp:w", "c", CancellationToken.None);
        await store.CaptureNoteAsync(principal, "telegram", $"t-{Guid.NewGuid():N}", "dentista el martes 4pm", topic, "telegram:t", "c", CancellationToken.None);

        var relevant = CorroborationPolicy
            .Evaluate(await store.SearchAsync(principal, topic, 5, CancellationToken.None), maxDistance: 0.6)
            .Relevant;

        Assert.Equal(2, relevant.Count);
        Assert.True(MemoryContinuityPolicy.IsCrossChannel(relevant));
        Assert.Equal(["telegram", "whatsapp"], MemoryContinuityPolicy.DistinctChannels(relevant));

        // A single topic group cites both channels' artifacts (unified memory).
        var result = new MemoryRecallResponseAssembler(new TopicGroupingSkill())
            .AssembleGrounded("Tu cita con el dentista es el martes a las 4pm.", relevant);
        var claim = Assert.Single(result.Claims);
        Assert.Equal(2, claim.Citations.Count);
        Assert.Contains(claim.Citations, c => c.ProvenanceRef.StartsWith("whatsapp:"));
        Assert.Contains(claim.Citations, c => c.ProvenanceRef.StartsWith("telegram:"));
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
