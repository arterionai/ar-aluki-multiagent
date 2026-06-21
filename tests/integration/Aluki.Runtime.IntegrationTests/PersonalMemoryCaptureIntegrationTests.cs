using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Persistence;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-002 US1: canonical note capture and source-identity dedupe (T011).
/// Exercises MemoryStore against PostgreSQL with no embedding (AI-independent).
/// Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class PersonalMemoryCaptureIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public PersonalMemoryCaptureIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Captures_canonical_note_and_suppresses_duplicate()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var principal = new PrincipalScope(seed.TenantId, seed.ContextId, seed.UserId, null);
        var store = new MemoryStore(BuildFactory(_fixture.ConnectionString!));
        var sourceIdentity = $"note-{Guid.NewGuid():N}";

        var first = await store.CaptureNoteAsync(
            principal, "whatsapp", sourceIdentity, "la cita es el martes", null, "prov", "corr-1", CancellationToken.None);
        Assert.True(first.IsNew);
        Assert.Equal(1, first.ChainVersion);

        var duplicate = await store.CaptureNoteAsync(
            principal, "whatsapp", sourceIdentity, "la cita es el martes", null, "prov", "corr-2", CancellationToken.None);
        Assert.False(duplicate.IsNew);
        Assert.Equal(first.CanonicalChainId, duplicate.CanonicalChainId);

        Assert.Equal(1, await CountArtifactsAsync(seed.TenantId, sourceIdentity));
    }

    private async Task<int> CountArtifactsAsync(Guid tenantId, string sourceIdentity)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "select count(*) from memory_artifact where tenant_id = @t and source_identity = @s;",
            connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("s", sourceIdentity);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static NpgsqlConnectionFactory BuildFactory(string connectionString) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = connectionString })
            .Build());
}
