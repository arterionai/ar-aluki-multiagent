using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Dispatch;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class MessageDispatchIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public MessageDispatchIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    private IDispatchAuditStore BuildAuditStore()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = _fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<NpgsqlConnectionFactory>();
        services.AddSingleton<IDispatchAuditStore, DispatchAuditStore>();
        return services.BuildServiceProvider().GetRequiredService<IDispatchAuditStore>();
    }

    private static PrincipalContext MakePrincipal(Guid tenantId) =>
        new(UserId: Guid.NewGuid(), TenantId: tenantId, ContextId: Guid.NewGuid(),
            Roles: [], SourceChannel: "whatsapp", CorrelationId: Guid.NewGuid().ToString("N"));

    private static UnifiedMessage MakeMessage(string? text = "test message") =>
        new(MessageId: Guid.NewGuid().ToString("N"), ChannelType: ChannelType.WhatsApp,
            Text: text, MediaRefs: [], ReceivedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString("N"));

    private MessageDispatcher BuildDispatcher(IDispatchAuditStore store, params IDomainAgent[] agents) =>
        new(agents, store, NullLogger<MessageDispatcher>.Instance);

    [Fact]
    public async Task Dispatch_audit_row_persisted_for_dispatched_outcome()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var store = BuildAuditStore();
        var dispatcher = BuildDispatcher(store, new DispatchTestAgent("test.agent", 10));
        var msg = MakeMessage();

        var result = await dispatcher.DispatchAsync(msg, MakePrincipal(seeded.TenantId), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.AuditEventId);
        var count = await CountAuditEventsAsync(seeded.TenantId, msg.MessageId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Dispatch_audit_row_persisted_for_no_agents_outcome()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var store = BuildAuditStore();
        var dispatcher = BuildDispatcher(store);
        var msg = MakeMessage();

        var result = await dispatcher.DispatchAsync(msg, MakePrincipal(seeded.TenantId), CancellationToken.None);

        Assert.Equal(DispatchOutcome.NoAgents, result.Outcome);
        Assert.NotEqual(Guid.Empty, result.AuditEventId);
        Assert.Equal(1, await CountAuditEventsAsync(seeded.TenantId, msg.MessageId));
    }

    [Fact]
    public async Task Dispatch_audit_row_persisted_for_contained_failure()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var store = BuildAuditStore();
        var dispatcher = BuildDispatcher(store, new DispatchThrowingAgent("fail.agent", 10));
        var msg = MakeMessage();

        var result = await dispatcher.DispatchAsync(msg, MakePrincipal(seeded.TenantId), CancellationToken.None);

        Assert.Equal(DispatchOutcome.ContainedFailure, result.Outcome);
        Assert.NotEqual(Guid.Empty, result.AuditEventId);

        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select failure_agent_id from dispatch_audit_events where id = @id;", conn);
        cmd.Parameters.AddWithValue("id", result.AuditEventId);
        var failId = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("fail.agent", failId);
    }

    [Fact]
    public async Task Multiple_dispatches_produce_independent_audit_rows()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var store = BuildAuditStore();
        var dispatcher = BuildDispatcher(store, new DispatchTestAgent("a.agent", 10));
        var principal = MakePrincipal(seeded.TenantId);

        var r1 = await dispatcher.DispatchAsync(MakeMessage(), principal, CancellationToken.None);
        var r2 = await dispatcher.DispatchAsync(MakeMessage(), principal, CancellationToken.None);

        Assert.NotEqual(r1.AuditEventId, r2.AuditEventId);
    }

    [Fact]
    public async Task Tie_break_recorded_in_audit_row()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var store = BuildAuditStore();
        var dispatcher = BuildDispatcher(store,
            new DispatchTestAgent("z.agent", 50),
            new DispatchTestAgent("a.agent", 50));
        var msg = MakeMessage();

        var result = await dispatcher.DispatchAsync(msg, MakePrincipal(seeded.TenantId), CancellationToken.None);

        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select tie_break_applied from dispatch_audit_events where id = @id;", conn);
        cmd.Parameters.AddWithValue("id", result.AuditEventId);
        var tieBreakApplied = (bool)(await cmd.ExecuteScalarAsync())!;
        Assert.True(tieBreakApplied);
        Assert.Equal("a.agent", result.SelectedAgentId);
    }

    private async Task<int> CountAuditEventsAsync(Guid tenantId, string messageId)
    {
        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select count(*) from dispatch_audit_events where tenant_id = @t and unified_message_id = @m;",
            conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("m", messageId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}

file sealed class DispatchTestAgent : IDomainAgent
{
    public DispatchTestAgent(string agentId, int priority) { AgentId = agentId; Priority = priority; }
    public string AgentId { get; }
    public int Priority { get; }
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;
    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal) => true;
    public Task<AgentHandleResult> HandleAsync(UnifiedMessage message, PrincipalContext principal, CancellationToken ct)
        => Task.FromResult(new AgentHandleResult(true, OutcomeCode: "handled"));
}

file sealed class DispatchThrowingAgent : IDomainAgent
{
    public DispatchThrowingAgent(string agentId, int priority) { AgentId = agentId; Priority = priority; }
    public string AgentId { get; }
    public int Priority { get; }
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;
    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal) => true;
    public Task<AgentHandleResult> HandleAsync(UnifiedMessage message, PrincipalContext principal, CancellationToken ct)
        => throw new InvalidOperationException("injected failure");
}
