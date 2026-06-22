using Aluki.Runtime.Abstractions.SemanticGraph;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class SemanticGraphContractTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();

    // ── EntityType ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("person")]
    [InlineData("organization")]
    [InlineData("location")]
    [InlineData("concept")]
    public void EntityType_IsValid_returns_true_for_known_types(string type)
        => Assert.True(EntityType.IsValid(type));

    [Fact]
    public void EntityType_IsValid_returns_false_for_unknown()
        => Assert.False(EntityType.IsValid("animal"));

    // ── RelationshipType ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("worksAt")]
    [InlineData("owns")]
    [InlineData("mentions")]
    [InlineData("collaboratesWith")]
    [InlineData("manages")]
    [InlineData("generic")]
    public void RelationshipType_IsValid_returns_true_for_known_types(string type)
        => Assert.True(RelationshipType.IsValid(type));

    [Fact]
    public void RelationshipType_Normalize_maps_unknown_to_generic()
        => Assert.Equal(RelationshipType.Generic, RelationshipType.Normalize("unknownType"));

    [Fact]
    public void RelationshipType_Normalize_preserves_valid_types()
        => Assert.Equal(RelationshipType.WorksAt, RelationshipType.Normalize("worksAt"));

    // ── SemanticEntity ────────────────────────────────────────────────────────

    [Fact]
    public void SemanticEntity_defaults_to_empty_collections()
    {
        var entity = new SemanticEntity(Guid.NewGuid(), _tenantId, EntityType.Person,
            "John Smith", null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Empty(entity.Aliases);
        Assert.Empty(entity.OutboundRelationships);
    }

    [Fact]
    public void SemanticEntity_init_with_aliases_works()
    {
        var alias = new EntityAlias(Guid.NewGuid(), Guid.NewGuid(), "John", 0.9m, DateTimeOffset.UtcNow);
        var entity = new SemanticEntity(Guid.NewGuid(), _tenantId, EntityType.Person,
            "John Smith", null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        { Aliases = [alias] };

        Assert.Single(entity.Aliases);
        Assert.Equal("John", entity.Aliases[0].Alias);
    }

    // ── SemanticRelationship ──────────────────────────────────────────────────

    [Fact]
    public void SemanticRelationship_active_when_archived_at_null()
    {
        var rel = new SemanticRelationship(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(), Guid.NewGuid(),
            RelationshipType.WorksAt, 0.95m, "Works at the company", [],
            Active: true, DateTimeOffset.UtcNow, ArchivedAtUtc: null);

        Assert.True(rel.Active);
        Assert.Null(rel.ArchivedAtUtc);
    }

    // ── StubGraphTraversalService ─────────────────────────────────────────────

    [Fact]
    public async Task GraphTraversalService_returns_null_for_missing_root_entity()
    {
        var repo = new StubSemanticGraphRepository();
        var traversal = new Aluki.Runtime.Host.Skills.SemanticGraph.GraphTraversalService(
            repo, Microsoft.Extensions.Logging.Abstractions.NullLogger<Aluki.Runtime.Host.Skills.SemanticGraph.GraphTraversalService>.Instance);

        var result = await traversal.TraverseAsync(
            new TraverseRequest(_tenantId, Guid.NewGuid(), MaxHops: 1), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GraphTraversalService_returns_root_with_no_hops_when_no_relationships()
    {
        var repo = new StubSemanticGraphRepository();
        var entityId = Guid.NewGuid();
        await repo.CreateEntityAsync(_tenantId, EntityType.Person, "John Smith", null, CancellationToken.None);
        var entity = await repo.FindByAliasAsync(_tenantId, "John Smith", CancellationToken.None);

        var traversal = new Aluki.Runtime.Host.Skills.SemanticGraph.GraphTraversalService(
            repo, Microsoft.Extensions.Logging.Abstractions.NullLogger<Aluki.Runtime.Host.Skills.SemanticGraph.GraphTraversalService>.Instance);

        var result = await traversal.TraverseAsync(
            new TraverseRequest(_tenantId, entity!.Id, MaxHops: 1), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.RootEntity.Id);
        Assert.Empty(result.Hops);
    }

    [Fact]
    public async Task GraphTraversalService_finds_direct_relationship()
    {
        var repo = new StubSemanticGraphRepository();
        var john = await repo.CreateEntityAsync(_tenantId, EntityType.Person, "John", null, CancellationToken.None);
        var acme = await repo.CreateEntityAsync(_tenantId, EntityType.Organization, "Acme", null, CancellationToken.None);
        await repo.CreateRelationshipAsync(_tenantId, john.Id, acme.Id, RelationshipType.WorksAt, 0.9m, null, [], CancellationToken.None);

        var traversal = new Aluki.Runtime.Host.Skills.SemanticGraph.GraphTraversalService(
            repo, Microsoft.Extensions.Logging.Abstractions.NullLogger<Aluki.Runtime.Host.Skills.SemanticGraph.GraphTraversalService>.Instance);

        var result = await traversal.TraverseAsync(
            new TraverseRequest(_tenantId, john.Id, MaxHops: 1), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Hops);
        Assert.Equal(acme.Id, result.Hops[0].ToEntity.Id);
        Assert.Equal(RelationshipType.WorksAt, result.Hops[0].Relationship.RelationshipType);
    }

    [Fact]
    public async Task FindPath_returns_empty_when_no_path_exists()
    {
        var repo = new StubSemanticGraphRepository();
        var john = await repo.CreateEntityAsync(_tenantId, EntityType.Person, "John", null, CancellationToken.None);
        var acme = await repo.CreateEntityAsync(_tenantId, EntityType.Organization, "Acme", null, CancellationToken.None);

        var traversal = new Aluki.Runtime.Host.Skills.SemanticGraph.GraphTraversalService(
            repo, Microsoft.Extensions.Logging.Abstractions.NullLogger<Aluki.Runtime.Host.Skills.SemanticGraph.GraphTraversalService>.Instance);

        var path = await traversal.FindPathAsync(_tenantId, john.Id, acme.Id, 3, CancellationToken.None);

        Assert.Empty(path);
    }
}

// ── Stub repository ───────────────────────────────────────────────────────────

file sealed class StubSemanticGraphRepository : ISemanticGraphRepository
{
    private readonly List<SemanticEntity> _entities = [];
    private readonly List<EntityAlias> _aliases = [];
    private readonly List<SemanticRelationship> _relationships = [];
    private readonly List<(Guid EntityId, Guid FactId, Guid TenantId)> _links = [];

    public Task<SemanticEntity> CreateEntityAsync(Guid tenantId, string entityType, string canonicalName, string? description, CancellationToken ct)
    {
        var entity = new SemanticEntity(Guid.NewGuid(), tenantId, entityType, canonicalName, description, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _entities.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<SemanticEntity?> GetEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct)
        => Task.FromResult(_entities.FirstOrDefault(e => e.TenantId == tenantId && e.Id == entityId && e.Active));

    public Task<IReadOnlyList<SemanticEntity>> ListEntitiesAsync(Guid tenantId, string? entityType, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SemanticEntity>>(
            _entities.Where(e => e.TenantId == tenantId && e.Active &&
                (entityType is null || e.EntityType == entityType)).ToList());

    public Task<SemanticEntity?> FindByAliasAsync(Guid tenantId, string alias, CancellationToken ct)
    {
        var entityId = _aliases.FirstOrDefault(a =>
            _entities.Any(e => e.Id == a.EntityId && e.TenantId == tenantId) &&
            string.Equals(a.Alias, alias, StringComparison.OrdinalIgnoreCase))?.EntityId;
        if (entityId is null)
        {
            var direct = _entities.FirstOrDefault(e => e.TenantId == tenantId && e.Active &&
                string.Equals(e.CanonicalName, alias, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(direct);
        }
        return Task.FromResult(_entities.FirstOrDefault(e => e.Id == entityId && e.Active));
    }

    public Task DeactivateEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct)
    {
        var idx = _entities.FindIndex(e => e.Id == entityId && e.TenantId == tenantId);
        if (idx >= 0) _entities[idx] = _entities[idx] with { Active = false };
        return Task.CompletedTask;
    }

    public Task<EntityAlias?> AddAliasAsync(Guid entityId, Guid tenantId, string alias, decimal confidence, CancellationToken ct)
    {
        if (_aliases.Any(a => a.EntityId == entityId && string.Equals(a.Alias, alias, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult<EntityAlias?>(null);
        var ea = new EntityAlias(Guid.NewGuid(), entityId, alias, confidence, DateTimeOffset.UtcNow);
        _aliases.Add(ea);
        return Task.FromResult<EntityAlias?>(ea);
    }

    public Task<IReadOnlyList<EntityAlias>> GetAliasesAsync(Guid entityId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EntityAlias>>(_aliases.Where(a => a.EntityId == entityId).ToList());

    public Task<SemanticRelationship> CreateRelationshipAsync(
        Guid tenantId, Guid sourceEntityId, Guid targetEntityId,
        string relationshipType, decimal confidence, string? explanation,
        IReadOnlyList<Guid> sourceFactIds, CancellationToken ct)
    {
        var rel = new SemanticRelationship(Guid.NewGuid(), tenantId, sourceEntityId, targetEntityId,
            relationshipType, confidence, explanation, sourceFactIds, true, DateTimeOffset.UtcNow, null);
        _relationships.Add(rel);
        return Task.FromResult(rel);
    }

    public Task<SemanticRelationship?> FindRelationshipAsync(Guid tenantId, Guid sourceEntityId, Guid targetEntityId, string relationshipType, CancellationToken ct)
        => Task.FromResult(_relationships.FirstOrDefault(r =>
            r.TenantId == tenantId && r.SourceEntityId == sourceEntityId &&
            r.TargetEntityId == targetEntityId && r.RelationshipType == relationshipType && r.Active));

    public Task<IReadOnlyList<SemanticRelationship>> ListOutboundRelationshipsAsync(Guid tenantId, Guid entityId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SemanticRelationship>>(
            _relationships.Where(r => r.TenantId == tenantId && r.SourceEntityId == entityId && r.Active).ToList());

    public Task<IReadOnlyList<SemanticRelationship>> ListInboundRelationshipsAsync(Guid tenantId, Guid entityId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SemanticRelationship>>(
            _relationships.Where(r => r.TenantId == tenantId && r.TargetEntityId == entityId && r.Active).ToList());

    public Task<bool> ArchiveRelationshipAsync(Guid tenantId, Guid relationshipId, CancellationToken ct)
    {
        var idx = _relationships.FindIndex(r => r.Id == relationshipId && r.TenantId == tenantId && r.Active);
        if (idx < 0) return Task.FromResult(false);
        _relationships[idx] = _relationships[idx] with { Active = false, ArchivedAtUtc = DateTimeOffset.UtcNow };
        return Task.FromResult(true);
    }

    public Task LinkFactAsync(Guid entityId, Guid factId, Guid tenantId, CancellationToken ct)
    {
        if (!_links.Any(l => l.EntityId == entityId && l.FactId == factId))
            _links.Add((entityId, factId, tenantId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListFactsByEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Guid>>(_links.Where(l => l.EntityId == entityId && l.TenantId == tenantId).Select(l => l.FactId).ToList());

    public Task<IReadOnlyList<Guid>> ListEntitiesByFactAsync(Guid tenantId, Guid factId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Guid>>(_links.Where(l => l.FactId == factId && l.TenantId == tenantId).Select(l => l.EntityId).ToList());

    public Task MergeEntitiesAsync(MergeEntitiesRequest request, CancellationToken ct)
    {
        var sourceIdx = _entities.FindIndex(e => e.Id == request.SourceEntityId);
        if (sourceIdx >= 0) _entities[sourceIdx] = _entities[sourceIdx] with { Active = false };
        return Task.CompletedTask;
    }
}
