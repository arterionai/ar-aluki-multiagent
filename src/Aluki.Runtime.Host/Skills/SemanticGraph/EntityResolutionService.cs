using System.Text.Json;
using System.Text.Json.Nodes;
using Aluki.Runtime.Abstractions.SemanticGraph;
using Aluki.Runtime.Memory.Chat;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.SemanticGraph;

public sealed class EntityResolutionService : IEntityResolutionService
{
    private const string SystemPrompt =
        """
        Extract all named entities and relationships from the provided text.
        Return ONLY valid JSON with this exact schema (no markdown, no explanation):
        {
          "entities": [
            { "canonical_name": "Full Name", "entity_type": "person|organization|location|concept", "confidence": 0.95, "aliases": ["Alias1"] }
          ],
          "relationships": [
            { "source_name": "Entity A", "target_name": "Entity B", "relationship_type": "worksAt|owns|mentions|collaboratesWith|manages|generic", "confidence": 0.90, "explanation": "Brief reason" }
          ]
        }
        Rules:
        - entity_type must be exactly: person, organization, location, or concept
        - relationship_type must be exactly: worksAt, owns, mentions, collaboratesWith, manages, or generic
        - confidence is a decimal from 0.00 to 1.00
        - aliases are alternative names or short forms found in the text
        - Include only entities explicitly mentioned in the text
        """;

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatModelRouter _router;
    private readonly ISemanticGraphRepository _repo;
    private readonly ILogger<EntityResolutionService> _logger;

    public EntityResolutionService(
        IChatModelRouter router,
        ISemanticGraphRepository repo,
        ILogger<EntityResolutionService> logger)
    {
        _router = router;
        _repo = repo;
        _logger = logger;
    }

    public async Task<ResolvedEntitiesResult> ResolveAsync(ResolveEntitiesRequest request, CancellationToken ct)
    {
        var raw = await _router.CompleteAsync(SystemPrompt, request.Text, ct);
        var extracted = ParseLlmResponse(raw);
        if (extracted is null)
        {
            _logger.LogWarning("Entity extraction returned unparseable response for tenant {TenantId}", request.TenantId);
            return new ResolvedEntitiesResult([], []);
        }

        // Resolve entities: deduplicate against existing, create new ones.
        var resolvedEntities = new Dictionary<string, SemanticEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractedEntity in extracted.Entities)
        {
            var entity = await ResolveEntityAsync(request.TenantId, extractedEntity, ct);
            resolvedEntities[extractedEntity.CanonicalName] = entity;
        }

        // Link entities to facts.
        if (request.FactIds is { Count: > 0 })
        {
            foreach (var entity in resolvedEntities.Values)
            foreach (var factId in request.FactIds)
                await _repo.LinkFactAsync(entity.Id, factId, request.TenantId, ct);
        }

        // Resolve relationships.
        var resolvedRelationships = new List<SemanticRelationship>();
        foreach (var extractedRel in extracted.Relationships)
        {
            if (!resolvedEntities.TryGetValue(extractedRel.SourceName, out var source)) continue;
            if (!resolvedEntities.TryGetValue(extractedRel.TargetName, out var target)) continue;

            var relType = RelationshipType.Normalize(extractedRel.RelationshipType);
            var existing = await _repo.FindRelationshipAsync(
                request.TenantId, source.Id, target.Id, relType, ct);

            if (existing is not null)
            {
                resolvedRelationships.Add(existing);
                continue;
            }

            var factIds = request.FactIds as IReadOnlyList<Guid> ?? [];
            var rel = await _repo.CreateRelationshipAsync(
                request.TenantId, source.Id, target.Id, relType,
                extractedRel.Confidence, extractedRel.Explanation, factIds, ct);
            resolvedRelationships.Add(rel);
        }

        return new ResolvedEntitiesResult([.. resolvedEntities.Values], resolvedRelationships);
    }

    private async Task<SemanticEntity> ResolveEntityAsync(
        Guid tenantId, ExtractedEntity extracted, CancellationToken ct)
    {
        // Look up by canonical name first, then by each alias.
        var existing = await _repo.FindByAliasAsync(tenantId, extracted.CanonicalName, ct);
        if (existing is null)
        {
            foreach (var alias in extracted.Aliases)
            {
                existing = await _repo.FindByAliasAsync(tenantId, alias, ct);
                if (existing is not null) break;
            }
        }

        if (existing is not null)
        {
            // Add any new aliases from this extraction run.
            await _repo.AddAliasAsync(existing.Id, tenantId, extracted.CanonicalName, extracted.Confidence, ct);
            foreach (var alias in extracted.Aliases)
                await _repo.AddAliasAsync(existing.Id, tenantId, alias, extracted.Confidence * 0.9m, ct);
            return existing;
        }

        // Create a new entity.
        var entityType = EntityType.IsValid(extracted.EntityType) ? extracted.EntityType : EntityType.Concept;
        var entity = await _repo.CreateEntityAsync(tenantId, entityType, extracted.CanonicalName, null, ct);

        // Canonical name is always an alias with confidence 1.0.
        await _repo.AddAliasAsync(entity.Id, tenantId, extracted.CanonicalName, 1.0m, ct);
        foreach (var alias in extracted.Aliases)
            await _repo.AddAliasAsync(entity.Id, tenantId, alias, extracted.Confidence * 0.9m, ct);

        return entity;
    }

    private static ExtractionResponse? ParseLlmResponse(string raw)
    {
        try
        {
            // Strip markdown code fences if present.
            var json = raw.Trim();
            if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
            if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];
            json = json.Trim();

            var node = JsonNode.Parse(json);
            if (node is null) return null;

            var entities = new List<ExtractedEntity>();
            foreach (var e in node["entities"]?.AsArray() ?? [])
            {
                if (e is null) continue;
                var canonicalName = e["canonical_name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(canonicalName)) continue;
                var aliases = new List<string>();
                foreach (var a in e["aliases"]?.AsArray() ?? [])
                    if (a?.GetValue<string>() is { } s) aliases.Add(s);
                entities.Add(new ExtractedEntity(
                    canonicalName,
                    e["entity_type"]?.GetValue<string>() ?? EntityType.Concept,
                    ParseDecimal(e["confidence"]) ?? 0.8m,
                    aliases));
            }

            var relationships = new List<ExtractedRelationship>();
            foreach (var r in node["relationships"]?.AsArray() ?? [])
            {
                if (r is null) continue;
                var sourceName = r["source_name"]?.GetValue<string>();
                var targetName = r["target_name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName)) continue;
                relationships.Add(new ExtractedRelationship(
                    sourceName,
                    targetName,
                    r["relationship_type"]?.GetValue<string>() ?? RelationshipType.Generic,
                    ParseDecimal(r["confidence"]) ?? 0.8m,
                    r["explanation"]?.GetValue<string>()));
            }

            return new ExtractionResponse(entities, relationships);
        }
        catch
        {
            return null;
        }
    }

    private static decimal? ParseDecimal(JsonNode? node)
    {
        if (node is null) return null;
        try { return node.GetValue<decimal>(); }
        catch { return null; }
    }
}

internal sealed record ExtractionResponse(
    IReadOnlyList<ExtractedEntity> Entities,
    IReadOnlyList<ExtractedRelationship> Relationships);

internal sealed record ExtractedEntity(
    string CanonicalName,
    string EntityType,
    decimal Confidence,
    IReadOnlyList<string> Aliases);

internal sealed record ExtractedRelationship(
    string SourceName,
    string TargetName,
    string RelationshipType,
    decimal Confidence,
    string? Explanation);
