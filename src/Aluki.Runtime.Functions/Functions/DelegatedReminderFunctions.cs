using System.Net;
using System.Text.Json;
using Aluki.Runtime.DelegatedReminders;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// HTTP ingress for SB-006 delegated reminders: create, list, and cancel.
/// Delegates to <see cref="DelegatedReminderService"/> and returns contract-shaped responses.
/// </summary>
public sealed class DelegatedReminderFunctions
{
    private readonly DelegatedReminderService _service;

    public DelegatedReminderFunctions(DelegatedReminderService service)
    {
        _service = service;
    }

    [Function("DelegatedReminderCreate")]
    public async Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "delegated-reminders")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        CreateDelegatedReminderRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<CreateDelegatedReminderRequest>(
                request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            return await WriteAsync(request, new DelegatedReminderHttpResult(
                400, new DelegatedReminderErrorResponse(
                    Guid.NewGuid().ToString("N"),
                    DelegatedReminderErrorCode.InvalidPayload,
                    "Invalid JSON body.")),
                cancellationToken);
        }

        var result = await _service.CreateAsync(payload, cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    [Function("DelegatedReminderList")]
    public async Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "delegated-reminders")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var query = ParseQuery(request.Url.Query);
        var userId = ParseGuid(Get(query, "user_id"));
        var tenantId = ParseGuid(Get(query, "tenant_id"));
        var contextId = ParseGuid(Get(query, "context_id"));

        var principalContext = userId is null || tenantId is null
            ? null
            : new DelegatedPrincipalContext(tenantId.Value, contextId, userId.Value);

        var result = await _service.ListAsync(principalContext, Get(query, "correlation_id"), cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    [Function("DelegatedReminderCancel")]
    public async Task<HttpResponseData> CancelAsync(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "delegated-reminders/{reminderId}")]
        HttpRequestData request,
        string reminderId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(reminderId, out var parsed))
        {
            return await WriteAsync(request, new DelegatedReminderHttpResult(
                400, new DelegatedReminderErrorResponse(
                    Guid.NewGuid().ToString("N"),
                    DelegatedReminderErrorCode.InvalidPayload,
                    "reminderId must be a UUID.")),
                cancellationToken);
        }

        CancelDelegatedReminderRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<CancelDelegatedReminderRequest>(
                request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        payload ??= new CancelDelegatedReminderRequest(null, null);
        var result = await _service.CancelAsync(parsed, payload, cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    // ── Helpers (mirrors ReminderFunctions pattern) ──────────────────────────

    private static async Task<HttpResponseData> WriteAsync(
        HttpRequestData request, DelegatedReminderHttpResult result, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse((HttpStatusCode)result.StatusCode);
        await response.WriteAsJsonAsync(result.Body, cancellationToken);
        return response;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        var q = query.TrimStart('?');
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..idx]);
            var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static string? Get(Dictionary<string, string> q, string key) =>
        q.TryGetValue(key, out var v) ? v : null;

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var g) ? g : null;
}
