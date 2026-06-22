using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.SemanticGraph;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

public sealed class SemanticGraphFunctions
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IEntityResolutionService _resolution;
    private readonly IGraphTraversalService _traversal;
    private readonly ISemanticGraphRepository _repo;

    public SemanticGraphFunctions(
        IEntityResolutionService resolution,
        IGraphTraversalService traversal,
        ISemanticGraphRepository repo)
    {
        _resolution = resolution;
        _traversal = traversal;
        _repo = repo;
    }

    // ── Entity resolution ─────────────────────────────────────────────────────

    [Function("SemanticGraphResolve")]
    public async Task<HttpResponseData> ResolveAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "semantic-graph/resolve")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        ResolveEntitiesRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<ResolveEntitiesRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException) { payload = null; }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Text))
            return await WriteBadRequestAsync(request, "invalid_payload", cancellationToken);

        var result = await _resolution.ResolveAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    // ── Entities ──────────────────────────────────────────────────────────────

    [Function("SemanticGraphListEntities")]
    public async Task<HttpResponseData> ListEntitiesAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "semantic-graph/entities")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId))
            return await WriteBadRequestAsync(request, "tenant_id_required", cancellationToken);

        var entityType = request.Query["entityType"];
        var entities = await _repo.ListEntitiesAsync(tenantId, entityType, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, entities, cancellationToken);
    }

    [Function("SemanticGraphGetEntity")]
    public async Task<HttpResponseData> GetEntityAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "semantic-graph/entities/{entityId}")]
        HttpRequestData request,
        string entityId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId) ||
            !Guid.TryParse(entityId, out var entityGuid))
            return await WriteBadRequestAsync(request, "tenant_id_and_entity_id_required", cancellationToken);

        var entity = await _repo.GetEntityAsync(tenantId, entityGuid, cancellationToken);
        if (entity is null) return await WriteNotFoundAsync(request, cancellationToken);

        var aliases = await _repo.GetAliasesAsync(entityGuid, cancellationToken);
        var outbound = await _repo.ListOutboundRelationshipsAsync(tenantId, entityGuid, cancellationToken);

        var detail = entity with { Aliases = aliases, OutboundRelationships = outbound };
        return await WriteJsonAsync(request, HttpStatusCode.OK, detail, cancellationToken);
    }

    [Function("SemanticGraphMergeEntities")]
    public async Task<HttpResponseData> MergeEntitiesAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "semantic-graph/entities/merge")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        MergeEntitiesRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<MergeEntitiesRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException) { payload = null; }

        if (payload is null)
            return await WriteBadRequestAsync(request, "invalid_payload", cancellationToken);

        await _repo.MergeEntitiesAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, new { merged = true }, cancellationToken);
    }

    // ── Traversal ─────────────────────────────────────────────────────────────

    [Function("SemanticGraphTraverse")]
    public async Task<HttpResponseData> TraverseAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "semantic-graph/traverse")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId) ||
            !Guid.TryParse(request.Query["entityId"], out var entityId))
            return await WriteBadRequestAsync(request, "tenant_id_and_entity_id_required", cancellationToken);

        int maxHops = int.TryParse(request.Query["maxHops"], out var hops) ? Math.Min(hops, 3) : 1;
        var relTypeFilter = request.Query["relationshipType"];

        var traverseRequest = new TraverseRequest(tenantId, entityId, maxHops,
            string.IsNullOrWhiteSpace(relTypeFilter) ? null : relTypeFilter);
        var result = await _traversal.TraverseAsync(traverseRequest, cancellationToken);

        if (result is null) return await WriteNotFoundAsync(request, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function("SemanticGraphFindPath")]
    public async Task<HttpResponseData> FindPathAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "semantic-graph/path")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId) ||
            !Guid.TryParse(request.Query["fromEntityId"], out var fromId) ||
            !Guid.TryParse(request.Query["toEntityId"], out var toId))
            return await WriteBadRequestAsync(request, "tenant_id_from_and_to_required", cancellationToken);

        int maxHops = int.TryParse(request.Query["maxHops"], out var hops) ? Math.Min(hops, 3) : 3;
        var path = await _traversal.FindPathAsync(tenantId, fromId, toId, maxHops, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, new { hops = path }, cancellationToken);
    }

    // ── Relationships ─────────────────────────────────────────────────────────

    [Function("SemanticGraphArchiveRelationship")]
    public async Task<HttpResponseData> ArchiveRelationshipAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "semantic-graph/relationships/{relationshipId}/archive")]
        HttpRequestData request,
        string relationshipId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId) ||
            !Guid.TryParse(relationshipId, out var relGuid))
            return await WriteBadRequestAsync(request, "tenant_id_and_relationship_id_required", cancellationToken);

        var archived = await _repo.ArchiveRelationshipAsync(tenantId, relGuid, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, new { archived }, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<HttpResponseData> WriteBadRequestAsync(
        HttpRequestData request, string error, CancellationToken ct)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(new { error }, ct);
        response.StatusCode = HttpStatusCode.BadRequest;
        return response;
    }

    private static async Task<HttpResponseData> WriteNotFoundAsync(
        HttpRequestData request, CancellationToken ct)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(new { error = "not_found" }, ct);
        response.StatusCode = HttpStatusCode.NotFound;
        return response;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request, HttpStatusCode statusCode, T body, CancellationToken ct)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(body, ct);
        response.StatusCode = statusCode;
        return response;
    }
}
