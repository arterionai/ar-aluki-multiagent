using Aluki.Runtime.Abstractions.SemanticGraph;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.SemanticGraph;

public sealed class GraphTraversalService : IGraphTraversalService
{
    private readonly ISemanticGraphRepository _repo;
    private readonly ILogger<GraphTraversalService> _logger;

    public GraphTraversalService(ISemanticGraphRepository repo, ILogger<GraphTraversalService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<TraversalResult?> TraverseAsync(TraverseRequest request, CancellationToken ct)
    {
        var root = await _repo.GetEntityAsync(request.TenantId, request.EntityId, ct);
        if (root is null) return null;

        var hops = new List<RelationshipHop>();
        var visited = new HashSet<Guid> { request.EntityId };
        var frontier = new List<Guid> { request.EntityId };
        var hopNumber = 0;

        while (frontier.Count > 0 && hopNumber < request.MaxHops)
        {
            hopNumber++;
            var nextFrontier = new List<Guid>();

            foreach (var entityId in frontier)
            {
                var outbound = await _repo.ListOutboundRelationshipsAsync(request.TenantId, entityId, ct);
                foreach (var rel in outbound)
                {
                    if (request.RelationshipTypeFilter is not null &&
                        rel.RelationshipType != request.RelationshipTypeFilter) continue;
                    if (visited.Contains(rel.TargetEntityId)) continue;

                    var toEntity = await _repo.GetEntityAsync(request.TenantId, rel.TargetEntityId, ct);
                    if (toEntity is null) continue;

                    hops.Add(new RelationshipHop(hopNumber, entityId, rel, toEntity));
                    visited.Add(rel.TargetEntityId);
                    nextFrontier.Add(rel.TargetEntityId);
                }
            }

            frontier = nextFrontier;
        }

        return new TraversalResult(root, hops);
    }

    public async Task<IReadOnlyList<RelationshipHop>> FindPathAsync(
        Guid tenantId, Guid fromEntityId, Guid toEntityId, int maxHops, CancellationToken ct)
    {
        maxHops = Math.Min(maxHops, 3);

        // BFS up to maxHops to find the shortest path.
        var queue = new Queue<(Guid entityId, List<RelationshipHop> path)>();
        queue.Enqueue((fromEntityId, []));
        var visited = new HashSet<Guid> { fromEntityId };

        while (queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();
            if (path.Count >= maxHops) continue;

            var outbound = await _repo.ListOutboundRelationshipsAsync(tenantId, current, ct);
            foreach (var rel in outbound)
            {
                if (visited.Contains(rel.TargetEntityId)) continue;
                visited.Add(rel.TargetEntityId);

                var toEntity = await _repo.GetEntityAsync(tenantId, rel.TargetEntityId, ct);
                if (toEntity is null) continue;

                var newPath = new List<RelationshipHop>(path)
                    { new RelationshipHop(path.Count + 1, current, rel, toEntity) };

                if (rel.TargetEntityId == toEntityId)
                    return newPath;

                queue.Enqueue((rel.TargetEntityId, newPath));
            }
        }

        return [];
    }
}
