using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar;
using Aluki.Runtime.Calendar.Skills;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Integration tests for the 10-minute deduplication window (T022, FR-009, FR-009a, SC-005, SC-012).
/// Uses an in-memory deduplication repository to verify:
/// - First request creates a new dedup record
/// - Duplicate within window returns the same outcome_reference
/// - Expired window allows a new record
/// </summary>
[Trait("Category", "Integration")]
public sealed class CalendarDeduplicationIntegrationTests
{
    private static CalendarIdempotencyGuardSkill BuildSkill(InMemoryDeduplicationRepository repo) =>
        new(repo, Options.Create(new CalendarOptions { DeduplicationWindowMinutes = 10 }));

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ContextId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const CalendarProvider Provider = CalendarProvider.Outlook;

    [Fact]
    public async Task First_request_is_not_a_duplicate()
    {
        var repo = new InMemoryDeduplicationRepository();
        var skill = BuildSkill(repo);
        var key = skill.ComputeIdempotencyKey(TenantId, UserId, Provider, "Team sync", "tomorrow at 2pm", "America/New_York");

        var check = await skill.CheckAsync(TenantId, ContextId, UserId, Provider, key);

        Assert.False(check.IsDuplicate);
    }

    [Fact]
    public async Task Second_request_within_window_is_a_duplicate()
    {
        var repo = new InMemoryDeduplicationRepository();
        var skill = BuildSkill(repo);
        var key = skill.ComputeIdempotencyKey(TenantId, UserId, Provider, "Dentist", "Friday at 9am", "America/Chicago");

        // First: begin window
        var record = await skill.BeginAsync(TenantId, ContextId, UserId, Provider, key);
        await skill.CompleteAsync(record.DeduplicationRecordId, "outlook-event-123");

        // Second check: should detect duplicate
        var check = await skill.CheckAsync(TenantId, ContextId, UserId, Provider, key);

        Assert.True(check.IsDuplicate);
        Assert.Equal(record.FirstOutcomeRef, check.Existing!.FirstOutcomeRef);
    }

    [Fact]
    public async Task Duplicate_returns_stable_outcome_reference()
    {
        var repo = new InMemoryDeduplicationRepository();
        var skill = BuildSkill(repo);
        var key = skill.ComputeIdempotencyKey(TenantId, UserId, Provider, "Board meeting", "Monday at 10am", "UTC");

        var record = await skill.BeginAsync(TenantId, ContextId, UserId, Provider, key);
        await skill.CompleteAsync(record.DeduplicationRecordId, "outlook-event-abc");

        var check1 = await skill.CheckAsync(TenantId, ContextId, UserId, Provider, key);
        var check2 = await skill.CheckAsync(TenantId, ContextId, UserId, Provider, key);

        Assert.True(check1.IsDuplicate);
        Assert.True(check2.IsDuplicate);
        Assert.Equal(check1.Existing!.FirstOutcomeRef, check2.Existing!.FirstOutcomeRef);
        Assert.Equal("outlook-event-abc", check1.Existing.FirstProviderEventRef);
    }

    [Fact]
    public void Idempotency_key_is_deterministic()
    {
        var repo = new InMemoryDeduplicationRepository();
        var skill = BuildSkill(repo);

        var key1 = skill.ComputeIdempotencyKey(TenantId, UserId, Provider, "Meeting", "tomorrow", "PST");
        var key2 = skill.ComputeIdempotencyKey(TenantId, UserId, Provider, "Meeting", "tomorrow", "PST");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Different_title_produces_different_key()
    {
        var repo = new InMemoryDeduplicationRepository();
        var skill = BuildSkill(repo);

        var key1 = skill.ComputeIdempotencyKey(TenantId, UserId, Provider, "Meeting A", "tomorrow", "PST");
        var key2 = skill.ComputeIdempotencyKey(TenantId, UserId, Provider, "Meeting B", "tomorrow", "PST");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public async Task Expired_window_record_is_not_returned()
    {
        var repo = new InMemoryDeduplicationRepository();
        var skill = BuildSkill(repo);
        var key = skill.ComputeIdempotencyKey(TenantId, UserId, Provider, "Old meeting", "last week", "UTC");

        // Seed an already-expired record directly
        await repo.CreateAsync(new DeduplicationRecord(
            DeduplicationRecordId: Guid.NewGuid(),
            TenantId: TenantId,
            ContextId: ContextId,
            UserId: UserId,
            Provider: Provider,
            IdempotencyKey: key,
            WindowStartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-20),
            WindowExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10), // expired
            FirstOutcomeRef: "old-outcome",
            FirstProviderEventRef: "old-event",
            Status: DeduplicationStatus.Created));

        var check = await skill.CheckAsync(TenantId, ContextId, UserId, Provider, key);

        // Expired record should not be returned — treated as new
        Assert.False(check.IsDuplicate);
    }

    // ── In-memory stub ─────────────────────────────────────────────────────

    public sealed class InMemoryDeduplicationRepository : IDeduplicationRepository
    {
        private readonly List<DeduplicationRecord> _store = new();

        public Task<DeduplicationRecord?> GetActiveAsync(Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, string idempotencyKey, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            var result = _store.FirstOrDefault(r =>
                r.TenantId == tenantId && r.ContextId == contextId &&
                r.UserId == userId && r.Provider == provider &&
                r.IdempotencyKey == idempotencyKey &&
                r.WindowExpiresAtUtc > now);
            return Task.FromResult(result);
        }

        public Task CreateAsync(DeduplicationRecord record, CancellationToken ct = default)
        {
            _store.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(Guid id, DeduplicationStatus status, string? providerEventRef, CancellationToken ct = default)
        {
            var idx = _store.FindIndex(r => r.DeduplicationRecordId == id);
            if (idx >= 0)
            {
                var r = _store[idx];
                _store[idx] = r with
                {
                    Status = status,
                    FirstProviderEventRef = providerEventRef ?? r.FirstProviderEventRef
                };
            }
            return Task.CompletedTask;
        }
    }
}
