namespace Aluki.Runtime.Abstractions.SemanticGraph;

// ── Entity ────────────────────────────────────────────────────────────────────

public sealed record SemanticEntity(
    Guid Id,
    Guid TenantId,
    string EntityType,
    string CanonicalName,
    string? Description,
    bool Active,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public IReadOnlyList<EntityAlias> Aliases { get; init; } = [];
    public IReadOnlyList<SemanticRelationship> OutboundRelationships { get; init; } = [];
}

public sealed record EntityAlias(
    Guid Id,
    Guid EntityId,
    string Alias,
    decimal Confidence,
    DateTimeOffset CreatedAtUtc);

public static class EntityType
{
    public const string Person = "person";
    public const string Organization = "organization";
    public const string Location = "location";
    public const string Concept = "concept";

    public static bool IsValid(string value) =>
        value is Person or Organization or Location or Concept;
}

// ── Relationship ──────────────────────────────────────────────────────────────

public sealed record SemanticRelationship(
    Guid Id,
    Guid TenantId,
    Guid SourceEntityId,
    Guid TargetEntityId,
    string RelationshipType,
    decimal Confidence,
    string? Explanation,
    IReadOnlyList<Guid> SourceFactIds,
    bool Active,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ArchivedAtUtc);

public static class RelationshipType
{
    public const string WorksAt = "worksAt";
    public const string Owns = "owns";
    public const string Mentions = "mentions";
    public const string CollaboratesWith = "collaboratesWith";
    public const string Manages = "manages";
    public const string Generic = "generic";

    public static bool IsValid(string value) =>
        value is WorksAt or Owns or Mentions or CollaboratesWith or Manages or Generic;

    public static string Normalize(string? value) =>
        value switch
        {
            WorksAt or Owns or Mentions or CollaboratesWith or Manages or Generic => value,
            _ => Generic
        };
}

// ── Resolution ────────────────────────────────────────────────────────────────

public sealed record ResolveEntitiesRequest(
    Guid TenantId,
    string Text,
    IReadOnlyList<Guid>? FactIds = null,
    string? CorrelationId = null);

public sealed record ResolvedEntitiesResult(
    IReadOnlyList<SemanticEntity> Entities,
    IReadOnlyList<SemanticRelationship> Relationships);

// ── Traversal ─────────────────────────────────────────────────────────────────

public sealed record TraverseRequest(
    Guid TenantId,
    Guid EntityId,
    int MaxHops = 1,
    string? RelationshipTypeFilter = null);

public sealed record TraversalResult(
    SemanticEntity RootEntity,
    IReadOnlyList<RelationshipHop> Hops);

public sealed record RelationshipHop(
    int HopNumber,
    Guid FromEntityId,
    SemanticRelationship Relationship,
    SemanticEntity ToEntity);

// ── Merge ─────────────────────────────────────────────────────────────────────

public sealed record MergeEntitiesRequest(
    Guid TenantId,
    Guid SourceEntityId,
    Guid TargetEntityId,
    string? Reason = null);
