using System.Net;
using System.Text.Json;
using Aluki.Runtime.Reminders;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// HTTP ingress for SB-005 scheduled reminders: create, list, snooze, and cancel.
/// Delegates to <see cref="ReminderService"/> and returns contract-shaped responses.
/// </summary>
public sealed class ReminderFunctions
{
    private readonly ReminderService _service;

    public ReminderFunctions(ReminderService service)
    {
        _service = service;
    }

    [Function("ReminderCreate")]
    public async Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "reminders")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        CreateReminderRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<CreateReminderRequest>(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            return await WriteAsync(request, new ReminderHttpResult(
                400, new ReminderErrorResponse(Guid.NewGuid().ToString("N"), ReminderErrorCode.InvalidPayload, "Invalid JSON body.")),
                cancellationToken);
        }

        var result = await _service.CreateAsync(payload, cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    [Function("ReminderList")]
    public async Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "reminders")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var query = ParseQuery(request.Url.Query);
        var principalId = ParseGuid(Get(query, "user_id"));
        var tenantId = ParseGuid(Get(query, "tenant_id"));
        var contextId = ParseGuid(Get(query, "context_id"));

        var principalContext = principalId is null || tenantId is null
            ? null
            : new ReminderPrincipalContext(tenantId.Value, contextId, principalId.Value);

        var result = await _service.ListAsync(principalContext, Get(query, "correlation_id"), cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    [Function("ReminderSnooze")]
    public async Task<HttpResponseData> SnoozeAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "reminders/{reminderId}/snooze")]
        HttpRequestData request,
        string reminderId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(reminderId, out var parsed))
        {
            return await WriteAsync(request, new ReminderHttpResult(
                400, new ReminderErrorResponse(Guid.NewGuid().ToString("N"), ReminderErrorCode.InvalidPayload, "reminderId must be a UUID.")),
                cancellationToken);
        }

        SnoozeReminderRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<SnoozeReminderRequest>(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        payload ??= new SnoozeReminderRequest(null, null, null);
        var result = await _service.SnoozeAsync(parsed, payload, cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    [Function("ReminderCancel")]
    public async Task<HttpResponseData> CancelAsync(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "reminders/{reminderId}")]
        HttpRequestData request,
        string reminderId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(reminderId, out var parsed))
        {
            return await WriteAsync(request, new ReminderHttpResult(
                400, new ReminderErrorResponse(Guid.NewGuid().ToString("N"), ReminderErrorCode.InvalidPayload, "reminderId must be a UUID.")),
                cancellationToken);
        }

        CancelReminderRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<CancelReminderRequest>(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }

        payload ??= new CancelReminderRequest(null, null);
        var result = await _service.CancelAsync(parsed, payload, cancellationToken);
        return await WriteAsync(request, result, cancellationToken);
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static string? Get(IReadOnlyDictionary<string, string> query, string key) =>
        query.TryGetValue(key, out var value) ? value : null;

    private static IReadOnlyDictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            result[Uri.UnescapeDataString(pair[..index])] = Uri.UnescapeDataString(pair[(index + 1)..]);
        }

        return result;
    }

    private static async Task<HttpResponseData> WriteAsync(
        HttpRequestData request, ReminderHttpResult result, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(result.Body, cancellationToken);
        response.StatusCode = (HttpStatusCode)result.StatusCode;
        return response;
    }
}
