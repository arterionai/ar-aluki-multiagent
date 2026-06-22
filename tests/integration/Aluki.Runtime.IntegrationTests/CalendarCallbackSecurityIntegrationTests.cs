using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Audit;
using Aluki.Runtime.Calendar.Observability;
using Aluki.Runtime.Calendar.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Integration tests for OAuth callback hardening (T013, FR-002a, SC-009):
/// single-use enforcement, expiry rejection, provider mismatch rejection.
/// Uses an in-memory stub repository so no live PostgreSQL is required.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CalendarCallbackSecurityIntegrationTests
{
    private static CalendarCallbackSkill BuildSkill(
        IOAuthCallbackStateRepository? callbackRepo = null,
        ICalendarConnectionRepository? connectionRepo = null)
    {
        var telemetry = new CalendarTelemetry(NullLogger<CalendarTelemetry>.Instance);
        var auditRepo = new NullCalendarAuditRepository();
        var auditWriter = new CalendarAuditWriter(auditRepo, NullLogger<CalendarAuditWriter>.Instance);
        var exchangers = new[]
        {
            new FakeOAuthTokenExchanger(CalendarProvider.Outlook),
            new FakeOAuthTokenExchanger(CalendarProvider.Google),
        };
        return new CalendarCallbackSkill(
            callbackRepo ?? new InMemoryCallbackStateRepository(),
            connectionRepo ?? new InMemoryConnectionRepository(),
            exchangers,
            new FakeCalendarTokenService(),
            auditWriter,
            telemetry);
    }

    [Fact]
    public async Task Unknown_nonce_is_rejected()
    {
        var skill = BuildSkill();
        var request = new OAuthCallbackRequest("unknown-nonce", "code123", CalendarProvider.Outlook, "corr-1");

        var result = await skill.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Equal(ConnectionStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.OutcomeReference));
    }

    [Fact]
    public async Task Valid_nonce_succeeds_and_persists_connection()
    {
        var callbackRepo = new InMemoryCallbackStateRepository();
        var connectionRepo = new InMemoryConnectionRepository();
        var tenantId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string nonce = "valid-nonce-abc";

        await callbackRepo.CreateAsync(new OAuthCallbackStateRecord(
            OAuthCallbackStateId: Guid.NewGuid(),
            TenantId: tenantId,
            ContextId: contextId,
            UserId: userId,
            Provider: CalendarProvider.Outlook,
            StateNonce: nonce,
            IssuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(9),
            UsedAtUtc: null,
            Status: OAuthCallbackStatus.Issued,
            CorrelationId: "corr-setup"));

        var skill = BuildSkill(callbackRepo, connectionRepo);
        var result = await skill.ExecuteAsync(new OAuthCallbackRequest(nonce, "auth-code", CalendarProvider.Outlook, "corr-1"));

        Assert.True(result.Success);
        Assert.Equal(ConnectionStatus.Connected, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.OutcomeReference));

        var state = await callbackRepo.GetByNonceAsync(nonce);
        Assert.Equal(OAuthCallbackStatus.Consumed, state!.Status);

        var connection = await connectionRepo.GetActiveAsync(tenantId, contextId, userId, CalendarProvider.Outlook);
        Assert.NotNull(connection);
        Assert.Equal(ConnectionStatus.Connected, connection.Status);
    }

    [Fact]
    public async Task Replaying_consumed_nonce_is_rejected()
    {
        var callbackRepo = new InMemoryCallbackStateRepository();
        const string nonce = "replay-nonce";

        await callbackRepo.CreateAsync(new OAuthCallbackStateRecord(
            OAuthCallbackStateId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(), UserId: Guid.NewGuid(),
            Provider: CalendarProvider.Outlook,
            StateNonce: nonce,
            IssuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(8),
            UsedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            Status: OAuthCallbackStatus.Consumed,
            CorrelationId: "corr-setup"));

        var skill = BuildSkill(callbackRepo);
        var result = await skill.ExecuteAsync(new OAuthCallbackRequest(nonce, "auth-code", CalendarProvider.Outlook, "corr-replay"));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Expired_nonce_is_rejected()
    {
        var callbackRepo = new InMemoryCallbackStateRepository();
        const string nonce = "expired-nonce";

        await callbackRepo.CreateAsync(new OAuthCallbackStateRecord(
            OAuthCallbackStateId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(), UserId: Guid.NewGuid(),
            Provider: CalendarProvider.Outlook,
            StateNonce: nonce,
            IssuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-15),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            UsedAtUtc: null,
            Status: OAuthCallbackStatus.Issued,
            CorrelationId: "corr-setup"));

        var skill = BuildSkill(callbackRepo);
        var result = await skill.ExecuteAsync(new OAuthCallbackRequest(nonce, "auth-code", CalendarProvider.Outlook, "corr-expire"));

        Assert.False(result.Success);
        var state = await callbackRepo.GetByNonceAsync(nonce);
        Assert.Equal(OAuthCallbackStatus.Rejected, state!.Status);
    }

    [Fact]
    public async Task Provider_mismatch_in_callback_is_rejected()
    {
        var callbackRepo = new InMemoryCallbackStateRepository();
        const string nonce = "mismatch-nonce";

        await callbackRepo.CreateAsync(new OAuthCallbackStateRecord(
            OAuthCallbackStateId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(), UserId: Guid.NewGuid(),
            Provider: CalendarProvider.Outlook,
            StateNonce: nonce,
            IssuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(9),
            UsedAtUtc: null,
            Status: OAuthCallbackStatus.Issued,
            CorrelationId: "corr-setup"));

        var skill = BuildSkill(callbackRepo);
        // Issued for Outlook but callback arrives claiming Google
        var result = await skill.ExecuteAsync(new OAuthCallbackRequest(nonce, "auth-code", CalendarProvider.Google, "corr-mismatch"));

        Assert.False(result.Success);
        var state = await callbackRepo.GetByNonceAsync(nonce);
        Assert.Equal(OAuthCallbackStatus.Rejected, state!.Status);
    }

    // ── In-memory stubs ─────────────────────────────────────────────────────

    private sealed class InMemoryCallbackStateRepository : IOAuthCallbackStateRepository
    {
        private readonly Dictionary<string, OAuthCallbackStateRecord> _store = new();

        public Task CreateAsync(OAuthCallbackStateRecord record, CancellationToken ct = default)
        {
            _store[record.StateNonce] = record;
            return Task.CompletedTask;
        }

        public Task<OAuthCallbackStateRecord?> GetByNonceAsync(string nonce, CancellationToken ct = default)
        {
            _store.TryGetValue(nonce, out var record);
            return Task.FromResult(record);
        }

        public Task MarkConsumedAsync(Guid id, DateTimeOffset consumedAt, CancellationToken ct = default)
        {
            var entry = _store.Values.FirstOrDefault(r => r.OAuthCallbackStateId == id);
            if (entry is not null)
                _store[entry.StateNonce] = entry with { Status = OAuthCallbackStatus.Consumed, UsedAtUtc = consumedAt };
            return Task.CompletedTask;
        }

        public Task MarkRejectedAsync(Guid id, CancellationToken ct = default)
        {
            var entry = _store.Values.FirstOrDefault(r => r.OAuthCallbackStateId == id);
            if (entry is not null)
                _store[entry.StateNonce] = entry with { Status = OAuthCallbackStatus.Rejected };
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryConnectionRepository : ICalendarConnectionRepository
    {
        private readonly List<CalendarConnectionRecord> _store = new();

        public Task<CalendarConnectionRecord?> GetActiveAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default)
        {
            var result = _store.FirstOrDefault(c =>
                c.TenantId == tenantId && c.ContextId == contextId &&
                c.UserId == userId && c.Provider == provider &&
                c.Status == ConnectionStatus.Connected);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<CalendarConnectionRecord>> GetAllActiveAsync(Guid tenantId, Guid contextId, Guid userId, CancellationToken ct = default)
        {
            IReadOnlyList<CalendarConnectionRecord> result = _store
                .Where(c => c.TenantId == tenantId && c.ContextId == contextId &&
                            c.UserId == userId && c.Status == ConnectionStatus.Connected)
                .ToList();
            return Task.FromResult(result);
        }

        public Task UpsertAsync(CalendarConnectionRecord record, CancellationToken ct = default)
        {
            _store.RemoveAll(c => c.CalendarConnectionId == record.CalendarConnectionId);
            _store.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class NullCalendarAuditRepository : ICalendarAuditRepository
    {
        public Task AppendAsync(CalendarAuditRecord record, CancellationToken ct = default) => Task.CompletedTask;
    }
}
