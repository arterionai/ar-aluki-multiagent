using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Observability;

namespace Aluki.Runtime.Host.Calendar.Audit;

// Thin façade that emits well-known audit event names for the authorization lifecycle.
// Keeps event name constants in one place so all callers are consistent.
public sealed class CalendarAuthorizationAuditSkill
{
    public const string ConnectInitiated = "calendar.connect.initiated";
    public const string ConnectCompleted = "calendar.callback.completed";
    public const string ConnectDenied = "calendar.connect.denied";
    public const string CallbackRejected = "calendar.callback.rejected";
    public const string DisconnectCompleted = "calendar.disconnect.completed";
    public const string DisconnectDenied = "calendar.disconnect.denied";
    public const string DisconnectNoop = "calendar.disconnect.noop";

    private readonly CalendarAuditWriter _writer;
    private readonly CalendarTelemetry _telemetry;

    public CalendarAuthorizationAuditSkill(CalendarAuditWriter writer, CalendarTelemetry telemetry)
    {
        _writer = writer;
        _telemetry = telemetry;
    }

    public Task WriteConnectInitiatedAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, string correlationId, CancellationToken ct = default) =>
        _writer.WriteAsync(ConnectInitiated, tenantId, contextId, userId, provider, nameof(CalendarAuthorizationAuditSkill),
            "initiated", null, correlationId, null, ct);

    public Task WriteCallbackCompletedAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, string outcomeRef, string correlationId, CancellationToken ct = default) =>
        _writer.WriteAsync(ConnectCompleted, tenantId, contextId, userId, provider, nameof(CalendarAuthorizationAuditSkill),
            "connection_established", outcomeRef, correlationId, null, ct);

    public Task WriteCallbackRejectedAsync(Guid tenantId, Guid contextId, Guid? userId, CalendarProvider provider, string reason, string outcomeRef, string correlationId, CancellationToken ct = default)
    {
        _telemetry.RecordAuthFailure(tenantId, userId ?? Guid.Empty, provider, reason);
        return _writer.WriteAsync(CallbackRejected, tenantId, contextId, userId, provider, nameof(CalendarAuthorizationAuditSkill),
            "rejected", outcomeRef, correlationId, new { reason }, ct);
    }

    public Task WriteDisconnectCompletedAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, string outcomeRef, string correlationId, CancellationToken ct = default) =>
        _writer.WriteAsync(DisconnectCompleted, tenantId, contextId, userId, provider, nameof(CalendarAuthorizationAuditSkill),
            "disconnected", outcomeRef, correlationId, null, ct);
}
