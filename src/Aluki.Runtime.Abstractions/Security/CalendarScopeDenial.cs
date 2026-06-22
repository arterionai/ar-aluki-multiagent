namespace Aluki.Runtime.Abstractions.Security;

public sealed record CalendarScopeDenial(
    string Reason,
    string DenialCode,
    Guid TenantId,
    Guid ContextId,
    Guid UserId);
