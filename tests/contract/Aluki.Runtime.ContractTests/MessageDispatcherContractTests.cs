using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class MessageDispatcherContractTests
{
    private static PrincipalContext MakePrincipal() =>
        new(UserId: Guid.NewGuid(), TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(),
            Roles: [], SourceChannel: "whatsapp", CorrelationId: Guid.NewGuid().ToString("N"));

    private static UnifiedMessage MakeMessage(string? text = "hello world") =>
        new(MessageId: Guid.NewGuid().ToString("N"), ChannelType: ChannelType.WhatsApp,
            Text: text, MediaRefs: [], ReceivedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString("N"));

    private static MessageDispatcher Build(params IDomainAgent[] agents) =>
        new(agents, new StubDispatchAuditStore(), new NullDispatchRetryQueue(), NullLogger<MessageDispatcher>.Instance);

    [Fact]
    public async Task Single_claiming_agent_is_selected()
    {
        var agent = new StubDomainAgent("a.agent", priority: 10, claims: true);
        var dispatcher = Build(agent);

        var result = await dispatcher.DispatchAsync(MakeMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(DispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal("a.agent", result.SelectedAgentId);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public async Task No_claiming_agent_produces_no_agents_outcome()
    {
        var agent = new StubDomainAgent("a.agent", priority: 10, claims: false);
        var dispatcher = Build(agent);

        var result = await dispatcher.DispatchAsync(MakeMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(DispatchOutcome.NoAgents, result.Outcome);
        Assert.Null(result.SelectedAgentId);
        Assert.True(result.FallbackUsed);
    }

    [Fact]
    public async Task Fallback_agent_at_max_priority_produces_fallback_outcome()
    {
        var fallback = new StubDomainAgent("memory.recall_and_capture", priority: int.MaxValue, claims: true);
        var dispatcher = Build(fallback);

        var result = await dispatcher.DispatchAsync(MakeMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(DispatchOutcome.Fallback, result.Outcome);
        Assert.True(result.FallbackUsed);
    }

    [Fact]
    public async Task Domain_agent_wins_over_fallback_agent()
    {
        var domain = new StubDomainAgent("reminders.agent", priority: 100, claims: true);
        var fallback = new StubDomainAgent("memory.recall_and_capture", priority: int.MaxValue, claims: true);
        var dispatcher = Build(domain, fallback);

        var result = await dispatcher.DispatchAsync(MakeMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(DispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal("reminders.agent", result.SelectedAgentId);
        Assert.False(result.FallbackUsed);
    }

    [Fact]
    public async Task Tie_broken_by_agent_id_lexical_order()
    {
        var a = new StubDomainAgent("b.agent", priority: 50, claims: true);
        var b = new StubDomainAgent("a.agent", priority: 50, claims: true);
        var dispatcher = Build(a, b);

        var result = await dispatcher.DispatchAsync(MakeMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal("a.agent", result.SelectedAgentId);
    }

    [Fact]
    public async Task Agent_failure_is_contained_not_fallback()
    {
        var failing = new StubDomainAgent("failing.agent", priority: 10, claims: true, throws: true);
        var fallback = new StubDomainAgent("memory.recall_and_capture", priority: int.MaxValue, claims: true);
        var dispatcher = Build(failing, fallback);

        var result = await dispatcher.DispatchAsync(MakeMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(DispatchOutcome.ContainedFailure, result.Outcome);
        Assert.Equal("failing.agent", result.SelectedAgentId);
    }

    [Fact]
    public async Task Audit_event_id_is_populated_on_every_outcome()
    {
        var store = new StubDispatchAuditStore();
        var dispatcher = new MessageDispatcher(
            [new StubDomainAgent("a.agent", priority: 10, claims: true)],
            store,
            new NullDispatchRetryQueue(),
            NullLogger<MessageDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync(MakeMessage(), MakePrincipal(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.AuditEventId);
        Assert.Equal(result.AuditEventId, store.LastId);
    }

    [Fact]
    public async Task Same_message_same_agents_produces_same_selected_agent()
    {
        var a = new StubDomainAgent("z.agent", priority: 50, claims: true);
        var b = new StubDomainAgent("a.agent", priority: 50, claims: true);
        var dispatcher = Build(a, b);
        var msg = MakeMessage();
        var principal = MakePrincipal();

        var r1 = await dispatcher.DispatchAsync(msg, principal, CancellationToken.None);
        var r2 = await dispatcher.DispatchAsync(msg, principal, CancellationToken.None);

        Assert.Equal(r1.SelectedAgentId, r2.SelectedAgentId);
    }

    [Fact]
    public async Task Guard_exception_does_not_crash_dispatcher()
    {
        var throwing = new StubDomainAgent("guard.throws", priority: 10, claims: false, throwOnGuard: true);
        var fallback = new StubDomainAgent("memory.recall_and_capture", priority: int.MaxValue, claims: true);
        var dispatcher = Build(throwing, fallback);

        var result = await dispatcher.DispatchAsync(MakeMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(DispatchOutcome.Fallback, result.Outcome);
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

file sealed class StubDomainAgent : IDomainAgent
{
    private readonly bool _claims;
    private readonly bool _throws;
    private readonly bool _throwOnGuard;

    public StubDomainAgent(string agentId, int priority, bool claims, bool throws = false, bool throwOnGuard = false)
    {
        AgentId = agentId;
        Priority = priority;
        _claims = claims;
        _throws = throws;
        _throwOnGuard = throwOnGuard;
    }

    public string AgentId { get; }
    public int Priority { get; }
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
    {
        if (_throwOnGuard) throw new InvalidOperationException("guard error");
        return _claims;
    }

    public Task<AgentHandleResult> HandleAsync(UnifiedMessage message, PrincipalContext principal, CancellationToken ct)
    {
        if (_throws) throw new InvalidOperationException("handle error");
        return Task.FromResult(new AgentHandleResult(true, OutcomeCode: "handled"));
    }
}

file sealed class StubDispatchAuditStore : IDispatchAuditStore
{
    public Guid LastId { get; private set; }

    public Task<Guid> AppendAsync(DispatchAuditRecord record, CancellationToken ct)
    {
        LastId = Guid.NewGuid();
        return Task.FromResult(LastId);
    }
}
