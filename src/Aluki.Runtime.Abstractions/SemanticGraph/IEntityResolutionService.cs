namespace Aluki.Runtime.Abstractions.SemanticGraph;

/// <summary>
/// Extracts entities and relationships from free text via LLM,
/// deduplicates against existing tenant entities, and persists new ones.
/// </summary>
public interface IEntityResolutionService
{
    Task<ResolvedEntitiesResult> ResolveAsync(ResolveEntitiesRequest request, CancellationToken ct);
}
