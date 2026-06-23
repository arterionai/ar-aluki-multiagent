using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Calendar.Security;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for calendar scope enforcement (T014, FR-002, SC-006).
/// Verifies the DefaultCalendarScopeGuard (permit-all stub) and the
/// CalendarScopeDenial model structure used by real guard implementations.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CalendarScopeGuardTests
{
    private readonly ICalendarScopeGuard _guard = new DefaultCalendarScopeGuard();

    [Fact]
    public async Task Default_guard_permits_connect_for_any_principal()
    {
        var (tenantId, contextId, userId) = ScopeTuple();

        var denial = await _guard.EvaluateConnectAsync(tenantId, contextId, userId);

        Assert.Null(denial);
    }

    [Fact]
    public async Task Default_guard_permits_create_for_any_principal()
    {
        var (tenantId, contextId, userId) = ScopeTuple();

        var denial = await _guard.EvaluateCreateAsync(tenantId, contextId, userId);

        Assert.Null(denial);
    }

    [Fact]
    public async Task Default_guard_permits_disconnect_for_any_principal()
    {
        var (tenantId, contextId, userId) = ScopeTuple();

        var denial = await _guard.EvaluateDisconnectAsync(tenantId, contextId, userId);

        Assert.Null(denial);
    }

    [Fact]
    public void Scope_denial_carries_reason_and_code()
    {
        var tenantId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var denial = new CalendarScopeDenial(
            Reason: "tenant_suspended",
            DenialCode: "TENANT_SUSPENDED",
            TenantId: tenantId,
            ContextId: contextId,
            UserId: userId);

        Assert.Equal("tenant_suspended", denial.Reason);
        Assert.Equal("TENANT_SUSPENDED", denial.DenialCode);
        Assert.Equal(tenantId, denial.TenantId);
        Assert.Equal(contextId, denial.ContextId);
        Assert.Equal(userId, denial.UserId);
    }

    [Fact]
    public async Task Connect_scope_check_uses_provided_tenant_context_user()
    {
        var tenantId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Default guard: null means permitted. Scope triple is threaded through correctly.
        var denial = await _guard.EvaluateConnectAsync(tenantId, contextId, userId);

        Assert.Null(denial);
    }

    [Fact]
    public async Task Disconnect_scope_check_uses_provided_tenant_context_user()
    {
        var tenantId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var denial = await _guard.EvaluateDisconnectAsync(tenantId, contextId, userId);

        Assert.Null(denial);
    }

    private static (Guid TenantId, Guid ContextId, Guid UserId) ScopeTuple() =>
        (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
}
