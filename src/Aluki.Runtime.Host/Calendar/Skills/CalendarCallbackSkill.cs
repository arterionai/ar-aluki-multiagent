using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Audit;
using Aluki.Runtime.Host.Calendar.Observability;
using Aluki.Runtime.Host.Calendar.Providers;

namespace Aluki.Runtime.Host.Calendar.Skills;

public sealed class CalendarCallbackSkill
{
    private readonly IOAuthCallbackStateRepository _callbackStates;
    private readonly ICalendarConnectionRepository _connections;
    private readonly IEnumerable<IOAuthTokenExchanger> _exchangers;
    private readonly ICalendarTokenService _tokenService;
    private readonly CalendarAuditWriter _audit;
    private readonly CalendarTelemetry _telemetry;

    public CalendarCallbackSkill(
        IOAuthCallbackStateRepository callbackStates,
        ICalendarConnectionRepository connections,
        IEnumerable<IOAuthTokenExchanger> exchangers,
        ICalendarTokenService tokenService,
        CalendarAuditWriter audit,
        CalendarTelemetry telemetry)
    {
        _callbackStates = callbackStates;
        _connections = connections;
        _exchangers = exchangers;
        _tokenService = tokenService;
        _audit = audit;
        _telemetry = telemetry;
    }

    public async Task<OAuthCallbackResult> ExecuteAsync(OAuthCallbackRequest request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var state = await _callbackStates.GetByNonceAsync(request.StateNonce, ct);

        if (state is null)
        {
            return await RejectAsync(null, request, "nonce_not_found", "State nonce not found.", ct);
        }

        if (state.Provider != request.Provider)
        {
            return await RejectAsync(state, request, "provider_mismatch", "Provider in callback does not match issued state.", ct);
        }

        if (state.Status != OAuthCallbackStatus.Issued)
        {
            return await RejectAsync(state, request, "nonce_already_used", $"Callback state has already been {state.Status.ToString().ToLowerInvariant()}.", ct);
        }

        if (DateTimeOffset.UtcNow > state.ExpiresAtUtc)
        {
            return await RejectAsync(state, request, "nonce_expired", "Callback state has expired.", ct);
        }

        // Single-use: burn the nonce before any external exchange so it can't be replayed
        // even if the exchange races or fails.
        await _callbackStates.MarkConsumedAsync(state.OAuthCallbackStateId, DateTimeOffset.UtcNow, ct);

        var exchanger = _exchangers.FirstOrDefault(e => e.Provider == request.Provider);
        if (exchanger is null)
        {
            return await RejectAsync(state, request, "provider_not_configured",
                $"No OAuth exchanger registered for {request.Provider}.", ct);
        }

        // Exchange the authorization code for access/refresh tokens (requires client secret).
        var tokens = await exchanger.ExchangeCodeAsync(request.Code, ct);
        if (!tokens.Success || string.IsNullOrEmpty(tokens.AccessToken))
        {
            return await RejectAsync(state, request, "token_exchange_failed",
                $"Provider rejected the authorization code ({tokens.Error}).", ct);
        }

        // Resolve a human-meaningful account reference (best-effort); fall back to a synthetic ref.
        string providerAccountRef;
        try
        {
            providerAccountRef = await exchanger.ResolveAccountRefAsync(tokens.AccessToken, ct)
                ?? $"{request.Provider.ToString().ToLowerInvariant()}:{state.UserId}";
        }
        catch
        {
            providerAccountRef = $"{request.Provider.ToString().ToLowerInvariant()}:{state.UserId}";
        }

        var outcomeRef = Guid.NewGuid().ToString("N");
        var connectionId = Guid.NewGuid();

        var connection = new CalendarConnectionRecord(
            CalendarConnectionId: connectionId,
            TenantId: state.TenantId,
            ContextId: state.ContextId,
            UserId: state.UserId,
            Provider: request.Provider,
            Status: ConnectionStatus.Connected,
            ConnectedAtUtc: DateTimeOffset.UtcNow,
            DisconnectedAtUtc: null,
            ProviderAccountRef: providerAccountRef,
            DefaultForUser: true,
            CorrelationId: request.CorrelationId);

        await _connections.UpsertAsync(connection, ct);

        // Persist the encrypted tokens so provider adapters can call the real APIs.
        await _tokenService.PersistAsync(connectionId, state.TenantId, state.ContextId, state.UserId,
            request.Provider, tokens, request.CorrelationId, ct);

        await _audit.WriteAsync("calendar.callback.completed", state.TenantId, state.ContextId, state.UserId,
            request.Provider, nameof(CalendarCallbackSkill), "connection_established", outcomeRef, request.CorrelationId,
            new { provider = request.Provider.ToString(), provider_account_ref = providerAccountRef }, ct);

        _telemetry.RecordConnectOutcome(state.TenantId, state.UserId, request.Provider, "connection_established", sw.ElapsedMilliseconds);

        return new OAuthCallbackResult(Success: true, Status: ConnectionStatus.Connected, OutcomeReference: outcomeRef);
    }

    private async Task<OAuthCallbackResult> RejectAsync(
        OAuthCallbackStateRecord? state, OAuthCallbackRequest request,
        string reason, string message, CancellationToken ct)
    {
        if (state is not null)
            await _callbackStates.MarkRejectedAsync(state.OAuthCallbackStateId, ct);

        var tenantId = state?.TenantId ?? Guid.Empty;
        var contextId = state?.ContextId ?? Guid.Empty;
        Guid? userId = state?.UserId;
        var outcomeRef = Guid.NewGuid().ToString("N");

        await _audit.WriteAsync("calendar.callback.rejected", tenantId, contextId, userId,
            request.Provider, nameof(CalendarCallbackSkill), "rejected", outcomeRef, request.CorrelationId,
            new { reason, message, provider = request.Provider.ToString() }, ct);

        return new OAuthCallbackResult(Success: false, Status: ConnectionStatus.Failed, OutcomeReference: outcomeRef);
    }
}
