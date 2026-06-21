namespace Aluki.Runtime.Abstractions.Security;

public sealed record PrincipalContext(
    Guid UserId,
    Guid TenantId,
    Guid ContextId,
    string[] Roles,
    string SourceChannel,
    string CorrelationId
);
