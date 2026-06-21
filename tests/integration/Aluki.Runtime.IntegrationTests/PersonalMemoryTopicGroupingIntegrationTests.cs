using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Persistence;
using Aluki.Runtime.Memory.Recall;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-002 US3 (T028): topic-group coherence over scoped, non-deleted artifacts.
/// Captures two clusters of related notes, recalls one cluster, and verifies the
/// assembler groups the corroborated evidence into a single coherent topic.
/// Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class PersonalMemoryTopicGroupingIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public PersonalMemoryTopicGroupingIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Recall_groups_corroborated_evidence_into_a_topic()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var principal = new PrincipalScope(seed.TenantId, seed.ContextId, seed.UserId, null);
        var store = new MemoryStore(BuildFactory(_fixture.ConnectionString!));

        var dentist = Vec(3);
        var groceries = Vec(700);

        await store.CaptureNoteAsync(principal, "whatsapp", $"d1-{Guid.NewGuid():N}", "cita con el dentista el martes", dentist, "whatsapp:d1", "c", CancellationToken.None);
        await store.CaptureNoteAsync(principal, "whatsapp", $"d2-{Guid.NewGuid():N}", "recordatorio dentista martes 4pm", dentist, "whatsapp:d2", "c", CancellationToken.None);
        await store.CaptureNoteAsync(principal, "whatsapp", $"g1-{Guid.NewGuid():N}", "comprar pan leche huevos", groceries, "whatsapp:g1", "c", CancellationToken.None);

        var relevant = CorroborationPolicy
            .Evaluate(await store.SearchAsync(principal, dentist, 5, CancellationToken.None), maxDistance: 0.6)
            .Relevant;

        var result = new MemoryRecallResponseAssembler(new TopicGroupingSkill())
            .AssembleGrounded("Tu cita con el dentista es el martes.", relevant);

        var group = Assert.Single(result.TopicGroups);
        Assert.Equal("dentista", group.Topic);
        Assert.Equal(2, group.ArtifactIds.Count);
        Assert.Equal(relevant.Select(r => r.ArtifactId).OrderBy(x => x), group.ArtifactIds.OrderBy(x => x));
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
