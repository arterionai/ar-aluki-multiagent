using System.Text.Json;
using Aluki.Runtime.Abstractions.SemanticGraph;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aluki.Runtime.Host.Skills.SemanticGraph;

public sealed class SemanticGraphRepository : ISemanticGraphRepository
{
    private static readonly JsonSerializerOptions _json = new();
    private readonly NpgsqlConnectionFactory _factory;
    private readonly ILogger<SemanticGraphRepository> _logger;

    public SemanticGraphRepository(NpgsqlConnectionFactory factory, ILogger<SemanticGraphRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    // ── Entities ──────────────────────────────────────────────────────────────

    public async Task<SemanticEntity> CreateEntityAsync(
        Guid tenantId, string entityType, string canonicalName, string? description, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO semantic_entities (id, tenant_id, entity_type, canonical_name, description, active, created_at_utc, updated_at_utc)
            VALUES (gen_random_uuid(), @tenant_id, @entity_type, @canonical_name, @description, true, now(), now())
            RETURNING id, tenant_id, entity_type, canonical_name, description, active, created_at_utc, updated_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("entity_type", entityType);
        cmd.Parameters.AddWithValue("canonical_name", canonicalName);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadEntity(reader);
    }

    public async Task<SemanticEntity?> GetEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, entity_type, canonical_name, description, active, created_at_utc, updated_at_utc
            FROM semantic_entities
            WHERE tenant_id = @tenant_id AND id = @id
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("id", entityId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadEntity(reader);
    }

    public async Task<IReadOnlyList<SemanticEntity>> ListEntitiesAsync(
        Guid tenantId, string? entityType, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        var sql = entityType is null
            ? "SELECT id, tenant_id, entity_type, canonical_name, description, active, created_at_utc, updated_at_utc FROM semantic_entities WHERE tenant_id = @tenant_id AND active = true ORDER BY canonical_name"
            : "SELECT id, tenant_id, entity_type, canonical_name, description, active, created_at_utc, updated_at_utc FROM semantic_entities WHERE tenant_id = @tenant_id AND entity_type = @entity_type AND active = true ORDER BY canonical_name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        if (entityType is not null)
            cmd.Parameters.AddWithValue("entity_type", entityType);

        var entities = new List<SemanticEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            entities.Add(ReadEntity(reader));
        return entities;
    }

    public async Task<SemanticEntity?> FindByAliasAsync(Guid tenantId, string alias, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        // Search aliases table first, then fall back to canonical_name match.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT e.id, e.tenant_id, e.entity_type, e.canonical_name, e.description, e.active, e.created_at_utc, e.updated_at_utc
            FROM semantic_entities e
            LEFT JOIN semantic_entity_aliases a ON a.entity_id = e.id
            WHERE e.tenant_id = @tenant_id AND e.active = true
              AND (lower(e.canonical_name) = lower(@alias) OR lower(a.alias) = lower(@alias))
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("alias", alias);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadEntity(reader);
    }

    public async Task DeactivateEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE semantic_entities SET active = false, updated_at_utc = now() WHERE tenant_id = @tenant_id AND id = @id",
            conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("id", entityId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Aliases ───────────────────────────────────────────────────────────────

    public async Task<EntityAlias?> AddAliasAsync(
        Guid entityId, Guid tenantId, string alias, decimal confidence, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO semantic_entity_aliases (id, entity_id, tenant_id, alias, confidence, created_at_utc)
            VALUES (gen_random_uuid(), @entity_id, @tenant_id, @alias, @confidence, now())
            ON CONFLICT (entity_id, lower(alias)) DO NOTHING
            RETURNING id, entity_id, alias, confidence, created_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("entity_id", entityId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("alias", alias);
        cmd.Parameters.AddWithValue("confidence", confidence);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new EntityAlias(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetDecimal(3), reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<IReadOnlyList<EntityAlias>> GetAliasesAsync(Guid entityId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, entity_id, alias, confidence, created_at_utc FROM semantic_entity_aliases WHERE entity_id = @entity_id ORDER BY confidence DESC",
            conn);
        cmd.Parameters.AddWithValue("entity_id", entityId);

        var aliases = new List<EntityAlias>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            aliases.Add(new EntityAlias(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetFieldValue<DateTimeOffset>(4)));
        return aliases;
    }

    // ── Relationships ─────────────────────────────────────────────────────────

    public async Task<SemanticRelationship> CreateRelationshipAsync(
        Guid tenantId, Guid sourceEntityId, Guid targetEntityId,
        string relationshipType, decimal confidence, string? explanation,
        IReadOnlyList<Guid> sourceFactIds, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO semantic_relationships
                (id, tenant_id, source_entity_id, target_entity_id, relationship_type,
                 confidence, explanation, source_fact_ids, active, created_at_utc)
            VALUES
                (gen_random_uuid(), @tenant_id, @source_entity_id, @target_entity_id, @relationship_type,
                 @confidence, @explanation, @source_fact_ids::jsonb, true, now())
            RETURNING id, tenant_id, source_entity_id, target_entity_id, relationship_type,
                      confidence, explanation, source_fact_ids, active, created_at_utc, archived_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("source_entity_id", sourceEntityId);
        cmd.Parameters.AddWithValue("target_entity_id", targetEntityId);
        cmd.Parameters.AddWithValue("relationship_type", relationshipType);
        cmd.Parameters.AddWithValue("confidence", confidence);
        cmd.Parameters.AddWithValue("explanation", (object?)explanation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("source_fact_ids", JsonSerializer.Serialize(sourceFactIds, _json));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadRelationship(reader);
    }

    public async Task<SemanticRelationship?> FindRelationshipAsync(
        Guid tenantId, Guid sourceEntityId, Guid targetEntityId,
        string relationshipType, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, source_entity_id, target_entity_id, relationship_type,
                   confidence, explanation, source_fact_ids, active, created_at_utc, archived_at_utc
            FROM semantic_relationships
            WHERE tenant_id = @tenant_id AND source_entity_id = @source_entity_id
              AND target_entity_id = @target_entity_id AND relationship_type = @relationship_type
              AND active = true
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("source_entity_id", sourceEntityId);
        cmd.Parameters.AddWithValue("target_entity_id", targetEntityId);
        cmd.Parameters.AddWithValue("relationship_type", relationshipType);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadRelationship(reader);
    }

    public async Task<IReadOnlyList<SemanticRelationship>> ListOutboundRelationshipsAsync(
        Guid tenantId, Guid entityId, CancellationToken ct)
        => await ListRelationshipsAsync(tenantId, entityId, isSource: true, ct);

    public async Task<IReadOnlyList<SemanticRelationship>> ListInboundRelationshipsAsync(
        Guid tenantId, Guid entityId, CancellationToken ct)
        => await ListRelationshipsAsync(tenantId, entityId, isSource: false, ct);

    private async Task<IReadOnlyList<SemanticRelationship>> ListRelationshipsAsync(
        Guid tenantId, Guid entityId, bool isSource, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        var col = isSource ? "source_entity_id" : "target_entity_id";
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT id, tenant_id, source_entity_id, target_entity_id, relationship_type,
                   confidence, explanation, source_fact_ids, active, created_at_utc, archived_at_utc
            FROM semantic_relationships
            WHERE tenant_id = @tenant_id AND {col} = @entity_id AND active = true
            ORDER BY created_at_utc DESC
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("entity_id", entityId);

        var rels = new List<SemanticRelationship>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rels.Add(ReadRelationship(reader));
        return rels;
    }

    public async Task<bool> ArchiveRelationshipAsync(Guid tenantId, Guid relationshipId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE semantic_relationships
            SET active = false, archived_at_utc = now()
            WHERE tenant_id = @tenant_id AND id = @id AND active = true
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("id", relationshipId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ── Entity-fact links ─────────────────────────────────────────────────────

    public async Task LinkFactAsync(Guid entityId, Guid factId, Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO semantic_entity_facts (id, entity_id, fact_id, tenant_id, created_at_utc)
            VALUES (gen_random_uuid(), @entity_id, @fact_id, @tenant_id, now())
            ON CONFLICT (entity_id, fact_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("entity_id", entityId);
        cmd.Parameters.AddWithValue("fact_id", factId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListFactsByEntityAsync(Guid tenantId, Guid entityId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT fact_id FROM semantic_entity_facts WHERE tenant_id = @tenant_id AND entity_id = @entity_id",
            conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("entity_id", entityId);

        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    public async Task<IReadOnlyList<Guid>> ListEntitiesByFactAsync(Guid tenantId, Guid factId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT entity_id FROM semantic_entity_facts WHERE tenant_id = @tenant_id AND fact_id = @fact_id",
            conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("fact_id", factId);

        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    // ── Merge ─────────────────────────────────────────────────────────────────

    public async Task MergeEntitiesAsync(MergeEntitiesRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);

        // Re-point all outbound relationships from source → target.
        await using var cmdOutbound = new NpgsqlCommand(
            """
            UPDATE semantic_relationships
            SET source_entity_id = @target_id
            WHERE tenant_id = @tenant_id AND source_entity_id = @source_id AND active = true
            """, conn);
        cmdOutbound.Parameters.AddWithValue("tenant_id", request.TenantId);
        cmdOutbound.Parameters.AddWithValue("source_id", request.SourceEntityId);
        cmdOutbound.Parameters.AddWithValue("target_id", request.TargetEntityId);
        await cmdOutbound.ExecuteNonQueryAsync(ct);

        // Re-point all inbound relationships.
        await using var cmdInbound = new NpgsqlCommand(
            """
            UPDATE semantic_relationships
            SET target_entity_id = @target_id
            WHERE tenant_id = @tenant_id AND target_entity_id = @source_id AND active = true
            """, conn);
        cmdInbound.Parameters.AddWithValue("tenant_id", request.TenantId);
        cmdInbound.Parameters.AddWithValue("source_id", request.SourceEntityId);
        cmdInbound.Parameters.AddWithValue("target_id", request.TargetEntityId);
        await cmdInbound.ExecuteNonQueryAsync(ct);

        // Re-point entity-fact links.
        await using var cmdFacts = new NpgsqlCommand(
            """
            INSERT INTO semantic_entity_facts (id, entity_id, fact_id, tenant_id, created_at_utc)
            SELECT gen_random_uuid(), @target_id, fact_id, tenant_id, now()
            FROM semantic_entity_facts
            WHERE tenant_id = @tenant_id AND entity_id = @source_id
            ON CONFLICT (entity_id, fact_id) DO NOTHING
            """, conn);
        cmdFacts.Parameters.AddWithValue("tenant_id", request.TenantId);
        cmdFacts.Parameters.AddWithValue("source_id", request.SourceEntityId);
        cmdFacts.Parameters.AddWithValue("target_id", request.TargetEntityId);
        await cmdFacts.ExecuteNonQueryAsync(ct);

        // Copy aliases from source to target.
        await using var cmdAliases = new NpgsqlCommand(
            """
            INSERT INTO semantic_entity_aliases (id, entity_id, tenant_id, alias, confidence, created_at_utc)
            SELECT gen_random_uuid(), @target_id, tenant_id, alias, confidence, now()
            FROM semantic_entity_aliases
            WHERE entity_id = @source_id
            ON CONFLICT (entity_id, lower(alias)) DO NOTHING
            """, conn);
        cmdAliases.Parameters.AddWithValue("source_id", request.SourceEntityId);
        cmdAliases.Parameters.AddWithValue("target_id", request.TargetEntityId);
        await cmdAliases.ExecuteNonQueryAsync(ct);

        // Deactivate the source entity.
        await using var cmdDeact = new NpgsqlCommand(
            "UPDATE semantic_entities SET active = false, updated_at_utc = now() WHERE tenant_id = @tenant_id AND id = @source_id",
            conn);
        cmdDeact.Parameters.AddWithValue("tenant_id", request.TenantId);
        cmdDeact.Parameters.AddWithValue("source_id", request.SourceEntityId);
        await cmdDeact.ExecuteNonQueryAsync(ct);
    }

    // ── Read helpers ──────────────────────────────────────────────────────────

    private static SemanticEntity ReadEntity(NpgsqlDataReader r)
        => new(
            Id: r.GetGuid(0),
            TenantId: r.GetGuid(1),
            EntityType: r.GetString(2),
            CanonicalName: r.GetString(3),
            Description: r.IsDBNull(4) ? null : r.GetString(4),
            Active: r.GetBoolean(5),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(6),
            UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(7));

    private static SemanticRelationship ReadRelationship(NpgsqlDataReader r)
    {
        var factIdsJson = r.GetString(7);
        var factIds = JsonSerializer.Deserialize<List<Guid>>(factIdsJson, _json) ?? [];
        return new SemanticRelationship(
            Id: r.GetGuid(0),
            TenantId: r.GetGuid(1),
            SourceEntityId: r.GetGuid(2),
            TargetEntityId: r.GetGuid(3),
            RelationshipType: r.GetString(4),
            Confidence: r.GetDecimal(5),
            Explanation: r.IsDBNull(6) ? null : r.GetString(6),
            SourceFactIds: factIds,
            Active: r.GetBoolean(8),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(9),
            ArchivedAtUtc: r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10));
    }
}
