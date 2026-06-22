using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;
using Aluki.Runtime.Host.Skills.SuggestionsAdmin;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

public sealed class SuggestionsAdminFunctions
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SuggestionsAdminService _adminService;
    private readonly RewardLedgerService _rewardService;

    public SuggestionsAdminFunctions(SuggestionsAdminService adminService, RewardLedgerService rewardService)
    {
        _adminService = adminService;
        _rewardService = rewardService;
    }

    [Function("AdminListSuggestions")]
    public async Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/suggestions")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var query = ParseQuery(request.Url.Query);

        if (!query.TryGetValue("tenantId", out var tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return await WriteBadRequestAsync(request, "tenantId_required", cancellationToken);

        if (!query.TryGetValue("actorUserId", out var actorUserId) || string.IsNullOrWhiteSpace(actorUserId))
            return await WriteBadRequestAsync(request, "actorUserId_required", cancellationToken);

        if (!query.TryGetValue("actorRole", out var actorRole) || string.IsNullOrWhiteSpace(actorRole))
            return await WriteBadRequestAsync(request, "actorRole_required", cancellationToken);

        query.TryGetValue("status", out var status);
        query.TryGetValue("category", out var category);
        query.TryGetValue("priority", out var priority);
        query.TryGetValue("search", out var search);
        _ = int.TryParse(query.GetValueOrDefault("page", "1"), out var page);
        _ = int.TryParse(query.GetValueOrDefault("pageSize", "20"), out var pageSize);
        query.TryGetValue("sortBy", out var sortBy);

        var listRequest = new ListSuggestionsRequest(
            TenantId: tenantId,
            ActorUserId: actorUserId,
            ActorRole: actorRole,
            StatusFilter: string.IsNullOrWhiteSpace(status) ? null : status,
            CategoryFilter: string.IsNullOrWhiteSpace(category) ? null : category,
            PriorityFilter: string.IsNullOrWhiteSpace(priority) ? null : priority,
            SearchText: string.IsNullOrWhiteSpace(search) ? null : search,
            Page: page < 1 ? 1 : page,
            PageSize: pageSize < 1 ? 20 : pageSize,
            SortBy: string.IsNullOrWhiteSpace(sortBy) ? "created_at_utc" : sortBy);

        var result = await _adminService.ListQueueAsync(listRequest, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function("AdminTriageSuggestion")]
    public async Task<HttpResponseData> TriageAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/suggestions/{suggestionId}/triage")]
        HttpRequestData request,
        string suggestionId,
        CancellationToken cancellationToken)
    {
        TriageSuggestionRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<TriageSuggestionRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
            return await WriteBadRequestAsync(request, "invalid_payload", cancellationToken);

        var result = await _adminService.TriageAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    [Function("AdminRewardDecide")]
    public async Task<HttpResponseData> RewardDecideAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/rewards/decide")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        RewardDecisionRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<RewardDecisionRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
            return await WriteBadRequestAsync(request, "invalid_payload", cancellationToken);

        var result = await _rewardService.ProcessRewardDecisionAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, result, cancellationToken);
    }

    private static async Task<HttpResponseData> WriteBadRequestAsync(HttpRequestData request, string error, CancellationToken ct)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(new { error }, ct);
        response.StatusCode = HttpStatusCode.BadRequest;
        return response;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(HttpRequestData request, HttpStatusCode statusCode, T body, CancellationToken ct)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(body, ct);
        response.StatusCode = statusCode;
        return response;
    }

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(queryString)) return result;
        foreach (var pair in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0) continue;
            var key = Uri.UnescapeDataString(pair[..separatorIndex]);
            var value = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            result[key] = value;
        }
        return result;
    }
}
