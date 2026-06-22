namespace Aluki.Runtime.Abstractions.Security;

public interface ICalendarScopeGuard
{
    ValueTask<CalendarScopeDenial?> EvaluateConnectAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default);
    ValueTask<CalendarScopeDenial?> EvaluateCreateAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default);
    ValueTask<CalendarScopeDenial?> EvaluateDisconnectAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default);
}
