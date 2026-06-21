using Aluki.Runtime.Abstractions.Security;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit coverage for the principal/scope resolution result model and fallback
/// semantics (T021). The database-backed resolution paths (tenant from
/// membership, default personal context fallback) are exercised in the
/// integration suite where a scoped PostgreSQL instance is available.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PrincipalContextResolverTests
{
    [Fact]
    public void Allow_carries_principal_and_resolved_scope()
    {
        var principal = CaptureTestData.Principal();

        var resolution = PrincipalResolution.Allow(principal);

        Assert.True(resolution.Success);
        Assert.Same(principal, resolution.Principal);
        Assert.Equal(ScopeDenialReason.None, resolution.DenialReason);
        Assert.Equal(principal.TenantId, resolution.ResolvedTenantId);
        Assert.Equal(principal.UserId, resolution.ResolvedUserId);
    }

    [Fact]
    public void Deny_without_tenant_has_no_audit_scope()
    {
        var resolution = PrincipalResolution.Deny(
            ScopeDenialReason.MembershipNotFound,
            "no membership");

        Assert.False(resolution.Success);
        Assert.Null(resolution.Principal);
        Assert.Null(resolution.ResolvedTenantId);
        Assert.Equal(ScopeDenialReason.MembershipNotFound, resolution.DenialReason);
    }

    [Fact]
    public void Deny_with_partial_tenant_enables_denial_audit()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var resolution = PrincipalResolution.Deny(
            ScopeDenialReason.ContextNotAuthorized,
            "context not authorized",
            tenantId,
            userId);

        Assert.False(resolution.Success);
        Assert.Equal(tenantId, resolution.ResolvedTenantId);
        Assert.Equal(userId, resolution.ResolvedUserId);
    }
}
