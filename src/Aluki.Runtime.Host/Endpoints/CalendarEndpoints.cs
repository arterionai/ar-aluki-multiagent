using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Skills;

namespace Aluki.Runtime.Host.Endpoints;

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/calendar/connect", HandleConnectAsync);
        endpoints.MapGet("/api/calendar/callback", HandleCallbackAsync);
        endpoints.MapPost("/api/calendar/disconnect", HandleDisconnectAsync);
        endpoints.MapPost("/api/calendar/create_event", HandleCreateEventAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateEventAsync(
        CalendarCreateEventHttpRequest body,
        CalendarCreateSkill skill,
        CancellationToken ct)
    {
        if (body.TenantId == Guid.Empty || body.ContextId == Guid.Empty || body.UserId == Guid.Empty)
            return Results.BadRequest(new { error = "missing_fields", message = "tenant_id, context_id, and user_id are required." });

        if (string.IsNullOrWhiteSpace(body.NaturalLanguageInput))
            return Results.BadRequest(new { error = "missing_input", message = "natural_language_input is required." });

        var request = new CalendarCreateRequest(
            TenantId: body.TenantId,
            ContextId: body.ContextId,
            UserId: body.UserId,
            NaturalLanguageInput: body.NaturalLanguageInput,
            ProviderHint: body.ProviderHint,
            CorrelationId: body.CorrelationId ?? Guid.NewGuid().ToString("N"));

        var result = await skill.ExecuteAsync(request, ct);

        return result.OutcomeType switch
        {
            CalendarOutcomeType.Created => Results.Ok(SerializeOutcome(result)),
            CalendarOutcomeType.PreviouslyCreated => Results.Ok(SerializeOutcome(result)),
            CalendarOutcomeType.ClarificationRequired => Results.Json(SerializeOutcome(result), statusCode: StatusCodes.Status200OK),
            CalendarOutcomeType.ReconnectRequired => Results.Json(SerializeOutcome(result), statusCode: StatusCodes.Status402PaymentRequired),
            CalendarOutcomeType.Denied => Results.Json(SerializeOutcome(result), statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Json(SerializeOutcome(result), statusCode: StatusCodes.Status500InternalServerError)
        };
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
        reconnect_required = r.ReconnectRequired
    };

    private static async Task<IResult> HandleConnectAsync(
        CalendarConnectHttpRequest body,
        CalendarConnectSkill skill,
        CancellationToken ct)
    {
        if (!TryParseProvider(body.Provider, out var provider))
            return Results.BadRequest(new { error = "invalid_provider", message = "provider must be 'outlook' or 'google'." });

        if (body.TenantId == Guid.Empty || body.ContextId == Guid.Empty || body.UserId == Guid.Empty)
            return Results.BadRequest(new { error = "missing_fields", message = "tenant_id, context_id, and user_id are required." });

        var request = new CalendarConnectRequest(
            TenantId: body.TenantId,
            ContextId: body.ContextId,
            UserId: body.UserId,
            Provider: provider,
            CorrelationId: body.CorrelationId ?? Guid.NewGuid().ToString("N"));

        var outcome = await skill.ExecuteAsync(request, ct);

        if (!outcome.IsSuccess)
        {
            return Results.Json(
                new { error = "scope_denied", denial_code = outcome.Denial!.DenialCode, message = outcome.Denial.Reason },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(new
        {
            connect_url = outcome.Result!.ConnectUrl,
            state_nonce = outcome.Result.StateNonce,
            expires_at_utc = outcome.Result.ExpiresAtUtc
        });
    }

    private static async Task<IResult> HandleCallbackAsync(
        string? state,
        string? code,
        string? error,
        string? provider,
        CalendarCallbackSkill skill,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return Results.BadRequest(new { error = "provider_error", provider_error = error });
        }

        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest(new { error = "missing_params", message = "state and code are required." });
        }

        if (!TryParseProvider(provider, out var calendarProvider))
        {
            return Results.BadRequest(new { error = "invalid_provider", message = "provider query parameter must be 'outlook' or 'google'." });
        }

        var request = new OAuthCallbackRequest(
            StateNonce: state,
            Code: code,
            Provider: calendarProvider,
            CorrelationId: Guid.NewGuid().ToString("N"));

        var result = await skill.ExecuteAsync(request, ct);

        if (!result.Success)
        {
            return Results.Json(
                new { error = "callback_rejected", outcome_reference = result.OutcomeReference },
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(new
        {
            status = "connection_established",
            outcome_reference = result.OutcomeReference
        });
    }

    private static async Task<IResult> HandleDisconnectAsync(
        CalendarDisconnectHttpRequest body,
        CalendarDisconnectSkill skill,
        CancellationToken ct)
    {
        if (!TryParseProvider(body.Provider, out var provider))
            return Results.BadRequest(new { error = "invalid_provider", message = "provider must be 'outlook' or 'google'." });

        if (body.TenantId == Guid.Empty || body.ContextId == Guid.Empty || body.UserId == Guid.Empty)
            return Results.BadRequest(new { error = "missing_fields", message = "tenant_id, context_id, and user_id are required." });

        var request = new CalendarDisconnectRequest(
            TenantId: body.TenantId,
            ContextId: body.ContextId,
            UserId: body.UserId,
            Provider: provider,
            CorrelationId: body.CorrelationId ?? Guid.NewGuid().ToString("N"));

        var result = await skill.ExecuteAsync(request, ct);

        return Results.Ok(new
        {
            disconnected = result.Disconnected,
            outcome_reference = result.OutcomeReference
        });
    }

    private static bool TryParseProvider(string? value, out CalendarProvider provider)
    {
        provider = default;
        return value?.ToLowerInvariant() switch
        {
            "outlook" => SetAndReturn(CalendarProvider.Outlook, out provider),
            "google" => SetAndReturn(CalendarProvider.Google, out provider),
            _ => false
        };
    }

    private static bool SetAndReturn(CalendarProvider value, out CalendarProvider target)
    {
        target = value;
        return true;
    }
}

public sealed record CalendarCreateEventHttpRequest(
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    string NaturalLanguageInput,
    string? ProviderHint,
    string? CorrelationId);

public sealed record CalendarConnectHttpRequest(
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    string Provider,
    string? CorrelationId);

public sealed record CalendarDisconnectHttpRequest(
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    string Provider,
    string? CorrelationId);
