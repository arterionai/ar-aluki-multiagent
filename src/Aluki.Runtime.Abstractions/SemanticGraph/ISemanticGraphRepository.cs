namespace Aluki.Runtime.Abstractions.SemanticGraph;

public interface ISemanticGraphRepository
{
    // ── Entities ──────────────────────────────────────────────────────────────

    Task<SemanticEntity> CreateEntityAsync(
        Guid tenantId, string entityType, string canonicalName, string? description, CancellationToken ct);

    Task<SemanticEntity?> GetEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct);

    Task<IReadOnlyList<SemanticEntity>> ListEntitiesAsync(Guid tenantId, string? entityType, CancellationToken ct);

    Task<SemanticEntity?> FindByAliasAsync(Guid tenantId, string alias, CancellationToken ct);

    Task DeactivateEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct);

    // ── Aliases ───────────────────────────────────────────────────────────────

    Task<EntityAlias?> AddAliasAsync(
        Guid entityId, Guid tenantId, string alias, decimal confidence, CancellationToken ct);

    Task<IReadOnlyList<EntityAlias>> GetAliasesAsync(Guid entityId, CancellationToken ct);

    // ── Relationships ─────────────────────────────────────────────────────────

    Task<SemanticRelationship> CreateRelationshipAsync(
        Guid tenantId, Guid sourceEntityId, Guid targetEntityId,
        string relationshipType, decimal confidence, string? explanation,
        IReadOnlyList<Guid> sourceFactIds, CancellationToken ct);

    Task<SemanticRelationship?> FindRelationshipAsync(
        Guid tenantId, Guid sourceEntityId, Guid targetEntityId,
        string relationshipType, CancellationToken ct);

    Task<IReadOnlyList<SemanticRelationship>> ListOutboundRelationshipsAsync(
        Guid tenantId, Guid entityId, CancellationToken ct);

    Task<IReadOnlyList<SemanticRelationship>> ListInboundRelationshipsAsync(
        Guid tenantId, Guid entityId, CancellationToken ct);

    Task<bool> ArchiveRelationshipAsync(Guid tenantId, Guid relationshipId, CancellationToken ct);

    // ── Entity-fact links ─────────────────────────────────────────────────────

    Task LinkFactAsync(Guid entityId, Guid factId, Guid tenantId, CancellationToken ct);

    Task<IReadOnlyList<Guid>> ListFactsByEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct);

    Task<IReadOnlyList<Guid>> ListEntitiesByFactAsync(Guid tenantId, Guid factId, CancellationToken ct);

    // ── Merge ─────────────────────────────────────────────────────────────────

    Task MergeEntitiesAsync(MergeEntitiesRequest request, CancellationToken ct);
}
