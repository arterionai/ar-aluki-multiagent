using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Audit;
using Aluki.Runtime.Calendar.Observability;

namespace Aluki.Runtime.Calendar.Skills;

public sealed class CalendarDisconnectSkill
{
    private readonly ICalendarScopeGuard _scopeGuard;
    private readonly ICalendarConnectionRepository _connections;
    private readonly CalendarAuditWriter _audit;
    private readonly CalendarTelemetry _telemetry;

    public CalendarDisconnectSkill(
        ICalendarScopeGuard scopeGuard,
        ICalendarConnectionRepository connections,
        CalendarAuditWriter audit,
        CalendarTelemetry telemetry)
    {
        _scopeGuard = scopeGuard;
        _connections = connections;
        _audit = audit;
        _telemetry = telemetry;
    }

    public async Task<CalendarDisconnectResult> ExecuteAsync(CalendarDisconnectRequest request, CancellationToken ct = default)
    {
        var denial = await _scopeGuard.EvaluateDisconnectAsync(request.TenantId, request.ContextId, request.UserId, ct);
        if (denial is not null)
        {
            _telemetry.RecordScopeDenial(request.TenantId, request.UserId, denial.DenialCode);
            await _audit.WriteAsync("calendar.disconnect.denied", request.TenantId, request.ContextId, request.UserId,
                request.Provider, nameof(CalendarDisconnectSkill), "scope_denied", null, request.CorrelationId,
                new { denial.DenialCode, denial.Reason }, ct);
            return new CalendarDisconnectResult(Disconnected: false, OutcomeReference: Guid.NewGuid().ToString("N"));
        }

        var existing = await _connections.GetActiveAsync(
            request.TenantId, request.ContextId, request.UserId, request.Provider, ct);

        var outcomeRef = Guid.NewGuid().ToString("N");

        if (existing is null)
        {
            await _audit.WriteAsync("calendar.disconnect.noop", request.TenantId, request.ContextId, request.UserId,
                request.Provider, nameof(CalendarDisconnectSkill), "no_active_connection", outcomeRef, request.CorrelationId,
                new { provider = request.Provider.ToString() }, ct);
            return new CalendarDisconnectResult(Disconnected: false, OutcomeReference: outcomeRef);
        }

        var disconnected = existing with
        {
            Status = ConnectionStatus.Disconnected,
            DisconnectedAtUtc = DateTimeOffset.UtcNow,
            DefaultForUser = false
        };

        await _connections.UpsertAsync(disconnected, ct);

        await _audit.WriteAsync("calendar.disconnect.completed", request.TenantId, request.ContextId, request.UserId,
            request.Provider, nameof(CalendarDisconnectSkill), "disconnected", outcomeRef, request.CorrelationId,
            new { provider = request.Provider.ToString(), connection_id = existing.CalendarConnectionId }, ct);

        return new CalendarDisconnectResult(Disconnected: true, OutcomeReference: outcomeRef);
    }
}
