using Aluki.Runtime.Abstractions.SemanticGraph;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Host.Skills.SemanticGraph;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class SemanticGraphIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public SemanticGraphIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    private ISemanticGraphRepository BuildRepo()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = _fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<NpgsqlConnectionFactory>();
        services.AddScoped<SemanticGraphRepository>();
        services.AddScoped<ISemanticGraphRepository>(sp => sp.GetRequiredService<SemanticGraphRepository>());
        return services.BuildServiceProvider().GetRequiredService<ISemanticGraphRepository>();
    }

    [Fact]
    public async Task Entity_create_get_roundtrip()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var repo = BuildRepo();

        var entity = await repo.CreateEntityAsync(
            seeded.TenantId, EntityType.Person, "Jane Doe", "A test person", CancellationToken.None);

        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.Equal("Jane Doe", entity.CanonicalName);

        var fetched = await repo.GetEntityAsync(seeded.TenantId, entity.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(entity.Id, fetched.Id);
    }

    [Fact]
    public async Task Alias_add_and_find_by_alias()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var repo = BuildRepo();

        var entity = await repo.CreateEntityAsync(
            seeded.TenantId, EntityType.Person, "John Smith", null, CancellationToken.None);
        await repo.AddAliasAsync(entity.Id, seeded.TenantId, "John", 0.9m, CancellationToken.None);

        var found = await repo.FindByAliasAsync(seeded.TenantId, "john", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(entity.Id, found.Id);
    }

    [Fact]
    public async Task Relationship_create_and_archive()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var repo = BuildRepo();

        var person = await repo.CreateEntityAsync(seeded.TenantId, EntityType.Person, "Alice", null, CancellationToken.None);
        var org = await repo.CreateEntityAsync(seeded.TenantId, EntityType.Organization, "TechCorp", null, CancellationToken.None);

        var rel = await repo.CreateRelationshipAsync(
            seeded.TenantId, person.Id, org.Id,
            RelationshipType.WorksAt, 0.95m, "Alice works at TechCorp", [],
            CancellationToken.None);

        Assert.True(rel.Active);

        var outbound = await repo.ListOutboundRelationshipsAsync(seeded.TenantId, person.Id, CancellationToken.None);
        Assert.Single(outbound);

        var archived = await repo.ArchiveRelationshipAsync(seeded.TenantId, rel.Id, CancellationToken.None);
        Assert.True(archived);

        var afterArchive = await repo.ListOutboundRelationshipsAsync(seeded.TenantId, person.Id, CancellationToken.None);
        Assert.Empty(afterArchive);
    }

    [Fact]
    public async Task Entity_fact_link_roundtrip()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var repo = BuildRepo();
        var factId = Guid.NewGuid();

        var entity = await repo.CreateEntityAsync(
            seeded.TenantId, EntityType.Concept, "Project X", null, CancellationToken.None);
        await repo.LinkFactAsync(entity.Id, factId, seeded.TenantId, CancellationToken.None);

        // Idempotent: linking the same fact again should not throw.
        await repo.LinkFactAsync(entity.Id, factId, seeded.TenantId, CancellationToken.None);

        var facts = await repo.ListFactsByEntityAsync(seeded.TenantId, entity.Id, CancellationToken.None);
        Assert.Single(facts);
        Assert.Contains(factId, facts);

        var entities = await repo.ListEntitiesByFactAsync(seeded.TenantId, factId, CancellationToken.None);
        Assert.Contains(entity.Id, entities);
    }

    [Fact]
    public async Task Entity_merge_redirects_relationships()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var repo = BuildRepo();

        var john = await repo.CreateEntityAsync(seeded.TenantId, EntityType.Person, "John", null, CancellationToken.None);
        var johnSmith = await repo.CreateEntityAsync(seeded.TenantId, EntityType.Person, "John Smith", null, CancellationToken.None);
        var acme = await repo.CreateEntityAsync(seeded.TenantId, EntityType.Organization, "Acme", null, CancellationToken.None);

        await repo.CreateRelationshipAsync(
            seeded.TenantId, john.Id, acme.Id, RelationshipType.WorksAt, 0.8m, null, [], CancellationToken.None);

        await repo.MergeEntitiesAsync(new MergeEntitiesRequest(seeded.TenantId, john.Id, johnSmith.Id), CancellationToken.None);

        var johnAfter = await repo.GetEntityAsync(seeded.TenantId, john.Id, CancellationToken.None);
        Assert.NotNull(johnAfter);
        Assert.False(johnAfter.Active);

        var outboundFromJohnSmith = await repo.ListOutboundRelationshipsAsync(seeded.TenantId, johnSmith.Id, CancellationToken.None);
        Assert.Contains(outboundFromJohnSmith, r => r.TargetEntityId == acme.Id);
    }

    [Fact]
    public async Task Graph_traversal_single_hop()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var repo = BuildRepo();
        var traversal = new GraphTraversalService(repo, NullLogger<GraphTraversalService>.Instance);

        var bob = await repo.CreateEntityAsync(seeded.TenantId, EntityType.Person, "Bob", null, CancellationToken.None);
        var globex = await repo.CreateEntityAsync(seeded.TenantId, EntityType.Organization, "Globex", null, CancellationToken.None);
        await repo.CreateRelationshipAsync(
            seeded.TenantId, bob.Id, globex.Id, RelationshipType.WorksAt, 0.88m, null, [], CancellationToken.None);

        var result = await traversal.TraverseAsync(
            new TraverseRequest(seeded.TenantId, bob.Id, MaxHops: 1), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Hops);
        Assert.Equal(globex.Id, result.Hops[0].ToEntity.Id);
    }
}
