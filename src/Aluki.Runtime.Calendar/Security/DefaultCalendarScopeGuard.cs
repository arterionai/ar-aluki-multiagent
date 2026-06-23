using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Calendar.Security;

// Stub implementation — always permits. Real policy enforcement added in US1 auth lifecycle.
public sealed class DefaultCalendarScopeGuard : ICalendarScopeGuard
{
    public ValueTask<CalendarScopeDenial?> EvaluateConnectAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default)
        => ValueTask.FromResult<CalendarScopeDenial?>(null);

    public ValueTask<CalendarScopeDenial?> EvaluateCreateAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default)
        => ValueTask.FromResult<CalendarScopeDenial?>(null);

    public ValueTask<CalendarScopeDenial?> EvaluateDisconnectAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default)
        => ValueTask.FromResult<CalendarScopeDenial?>(null);
}
