namespace Aluki.Runtime.Abstractions.SemanticGraph;

public interface IGraphTraversalService
{
    Task<TraversalResult?> TraverseAsync(TraverseRequest request, CancellationToken ct);

    Task<IReadOnlyList<RelationshipHop>> FindPathAsync(
        Guid tenantId, Guid fromEntityId, Guid toEntityId, int maxHops, CancellationToken ct);
}
