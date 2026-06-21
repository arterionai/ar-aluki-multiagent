namespace Aluki.Runtime.Abstractions.Security;

/// <summary>
/// Channel-derived identity inputs used to resolve a <see cref="PrincipalContext"/>.
/// </summary>
public sealed record ChannelIdentity(
    string SourceChannel,
    string SenderExternalId,
    Guid? TenantHint,
    Guid? ContextId,
    string CorrelationId);

/// <summary>Structured reason a scope resolution was denied.</summary>
public enum ScopeDenialReason
{
    None = 0,
    TenantNotResolved,
    MembershipNotFound,
    ContextNotFound,
    ContextNotAuthorized,
    ConsentStopActive
}

/// <summary>
/// Outcome of principal/scope resolution. On success exposes the validated
/// <see cref="PrincipalContext"/>; on denial exposes the reason and any partially
/// resolved tenant/user that can scope a denial audit record.
/// </summary>
public sealed record PrincipalResolution(
    bool Success,
    PrincipalContext? Principal,
    ScopeDenialReason DenialReason,
    Guid? ResolvedTenantId,
    Guid? ResolvedUserId,
    string? DenialMessage)
{
    public static PrincipalResolution Allow(PrincipalContext principal) =>
        new(true, principal, ScopeDenialReason.None, principal.TenantId, principal.UserId, null);

    public static PrincipalResolution Deny(
        ScopeDenialReason reason,
        string message,
        Guid? tenantId = null,
        Guid? userId = null) =>
        new(false, null, reason, tenantId, userId, message);
}

/// <summary>
/// Resolves and validates a tenant/context principal scope from channel identity
/// before any capture side effect. Implementations must fail closed.
/// </summary>
public interface IPrincipalContextResolver
{
    Task<PrincipalResolution> ResolveAsync(ChannelIdentity identity, CancellationToken cancellationToken);
}
