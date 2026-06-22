using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Skills;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// HTTP ingress for calendar integration (SB-003): OAuth connect/callback,
/// disconnect, and natural-language event creation. Mirrors the Host minimal-API
/// surface (routes, validation, status codes) so the deployed worker exposes the
/// same contract. Delegates to the calendar skills in <c>Aluki.Runtime.Calendar</c>.
/// </summary>
public sealed class CalendarFunctions
{
    private readonly CalendarConnectSkill _connect;
    private readonly CalendarCallbackSkill _callback;
    private readonly CalendarDisconnectSkill _disconnect;
    private readonly CalendarCreateSkill _create;
    private readonly ILogger<CalendarFunctions> _logger;

    public CalendarFunctions(
        CalendarConnectSkill connect,
        CalendarCallbackSkill callback,
        CalendarDisconnectSkill disconnect,
        CalendarCreateSkill create,
        ILogger<CalendarFunctions> logger)
    {
        _connect = connect;
        _callback = callback;
        _disconnect = disconnect;
        _create = create;
        _logger = logger;
    }

    [Function("CalendarConnect")]
    public async Task<HttpResponseData> ConnectAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "calendar/connect")] HttpRequestData request,
        CancellationToken ct)
    {
        var body = await ReadJsonAsync<CalendarConnectHttpRequest>(request, ct);
        if (body is null)
            return await Json(request, HttpStatusCode.BadRequest, new { error = "invalid_body", message = "Invalid JSON body." });

        if (!TryParseProvider(body.Provider, out var provider))
            return await Json(request, HttpStatusCode.BadRequest, new { error = "invalid_provider", message = "provider must be 'outlook' or 'google'." });

        if (body.TenantId == Guid.Empty || body.ContextId == Guid.Empty || body.UserId == Guid.Empty)
            return await Json(request, HttpStatusCode.BadRequest, new { error = "missing_fields", message = "tenant_id, context_id, and user_id are required." });

        var outcome = await _connect.ExecuteAsync(new CalendarConnectRequest(
            body.TenantId, body.ContextId, body.UserId, provider,
            body.CorrelationId ?? Guid.NewGuid().ToString("N")), ct);

        if (!outcome.IsSuccess)
            return await Json(request, HttpStatusCode.Forbidden,
                new { error = "scope_denied", denial_code = outcome.Denial!.DenialCode, message = outcome.Denial.Reason });

        return await Json(request, HttpStatusCode.OK, new
        {
            connect_url = outcome.Result!.ConnectUrl,
            state_nonce = outcome.Result.StateNonce,
            expires_at_utc = outcome.Result.ExpiresAtUtc,
        });
    }

    [Function("CalendarCallback")]
    public async Task<HttpResponseData> CallbackAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "calendar/callback")] HttpRequestData request,
        CancellationToken ct)
    {
        var query = HttpUtility.ParseQueryString(request.Url.Query);
        var state = query["state"];
        var code = query["code"];
        var error = query["error"];
        var provider = query["provider"];

        if (!string.IsNullOrEmpty(error))
            return await Json(request, HttpStatusCode.BadRequest, new { error = "provider_error", provider_error = error });

        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
            return await Json(request, HttpStatusCode.BadRequest, new { error = "missing_params", message = "state and code are required." });

        if (!TryParseProvider(provider, out var calendarProvider))
            return await Json(request, HttpStatusCode.BadRequest, new { error = "invalid_provider", message = "provider query parameter must be 'outlook' or 'google'." });

        var result = await _callback.ExecuteAsync(new OAuthCallbackRequest(
            state, code, calendarProvider, Guid.NewGuid().ToString("N")), ct);

        if (!result.Success)
            return await Json(request, HttpStatusCode.BadRequest,
                new { error = "callback_rejected", outcome_reference = result.OutcomeReference });

        return await Json(request, HttpStatusCode.OK,
            new { status = "connection_established", outcome_reference = result.OutcomeReference });
    }

    [Function("CalendarDisconnect")]
    public async Task<HttpResponseData> DisconnectAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "calendar/disconnect")] HttpRequestData request,
        CancellationToken ct)
    {
        var body = await ReadJsonAsync<CalendarDisconnectHttpRequest>(request, ct);
        if (body is null)
            return await Json(request, HttpStatusCode.BadRequest, new { error = "invalid_body", message = "Invalid JSON body." });

        if (!TryParseProvider(body.Provider, out var provider))
            return await Json(request, HttpStatusCode.BadRequest, new { error = "invalid_provider", message = "provider must be 'outlook' or 'google'." });

        if (body.TenantId == Guid.Empty || body.ContextId == Guid.Empty || body.UserId == Guid.Empty)
            return await Json(request, HttpStatusCode.BadRequest, new { error = "missing_fields", message = "tenant_id, context_id, and user_id are required." });

        var result = await _disconnect.ExecuteAsync(new CalendarDisconnectRequest(
            body.TenantId, body.ContextId, body.UserId, provider,
            body.CorrelationId ?? Guid.NewGuid().ToString("N")), ct);

        return await Json(request, HttpStatusCode.OK,
            new { disconnected = result.Disconnected, outcome_reference = result.OutcomeReference });
    }

    [Function("CalendarCreateEvent")]
    public async Task<HttpResponseData> CreateEventAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "calendar/create_event")] HttpRequestData request,
        CancellationToken ct)
    {
        var body = await ReadJsonAsync<CalendarCreateEventHttpRequest>(request, ct);
        if (body is null)
            return await Json(request, HttpStatusCode.BadRequest, new { error = "invalid_body", message = "Invalid JSON body." });

        if (body.TenantId == Guid.Empty || body.ContextId == Guid.Empty || body.UserId == Guid.Empty)
            return await Json(request, HttpStatusCode.BadRequest, new { error = "missing_fields", message = "tenant_id, context_id, and user_id are required." });

        if (string.IsNullOrWhiteSpace(body.NaturalLanguageInput))
            return await Json(request, HttpStatusCode.BadRequest, new { error = "missing_input", message = "natural_language_input is required." });

        var createRequest = new CalendarCreateRequest(
            body.TenantId, body.ContextId, body.UserId,
            body.NaturalLanguageInput, body.ProviderHint,
            body.CorrelationId ?? Guid.NewGuid().ToString("N"));

        CalendarCreateResult result;
        try
        {
            result = await _create.ExecuteAsync(createRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "calendar.create.failed correlation_id={CorrelationId}", createRequest.CorrelationId);
            return await Json(request, HttpStatusCode.InternalServerError,
                new { error = "internal_error", message = "Calendar event creation failed." });
        }

        var status = result.OutcomeType switch
        {
            CalendarOutcomeType.Created => HttpStatusCode.OK,
            CalendarOutcomeType.PreviouslyCreated => HttpStatusCode.OK,
            CalendarOutcomeType.ClarificationRequired => HttpStatusCode.OK,
            CalendarOutcomeType.ReconnectRequired => HttpStatusCode.PaymentRequired,
            CalendarOutcomeType.Denied => HttpStatusCode.Forbidden,
            _ => HttpStatusCode.InternalServerError,
        };

        return await Json(request, status, SerializeOutcome(result));
    }

    private static object SerializeOutcome(CalendarCreateResult r) => new
    {
        outcome_type = r.OutcomeType.ToString().ToLowerInvariant(),
        outcome_reference = r.OutcomeReference,
        provider_event_reference = r.ProviderEventReference,
        final_title = r.FinalTitle,
        final_start_utc = r.FinalStartUtc,
        final_end_utc = r.FinalEndUtc,
        final_timezone = r.FinalTimezone,
        clarification_question = r.ClarificationQuestion,
        selected_provider = r.SelectedProvider?.ToString().ToLowerInvariant(),
        selection_reason = r.SelectionReason,
        reconnect_required = r.ReconnectRequired,
    };

    private static async Task<T?> ReadJsonAsync<T>(HttpRequestData request, CancellationToken ct) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<HttpResponseData> Json(HttpRequestData request, HttpStatusCode status, object payload)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(payload);
        response.StatusCode = status; // WriteAsJsonAsync forces 200; restore intended status
        return response;
    }

    private static bool TryParseProvider(string? value, out CalendarProvider provider)
    {
        switch (value?.ToLowerInvariant())
        {
            case "outlook": provider = CalendarProvider.Outlook; return true;
            case "google": provider = CalendarProvider.Google; return true;
            default: provider = default; return false;
        }
    }
}

public sealed record CalendarConnectHttpRequest(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("context_id")] Guid ContextId,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId);

public sealed record CalendarDisconnectHttpRequest(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("context_id")] Guid ContextId,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId);

public sealed record CalendarCreateEventHttpRequest(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("context_id")] Guid ContextId,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("natural_language_input")] string NaturalLanguageInput,
    [property: JsonPropertyName("provider_hint")] string? ProviderHint,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId);
