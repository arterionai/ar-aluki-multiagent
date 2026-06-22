using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Reminders;
using Aluki.Runtime.Reminders.Configuration;
using Aluki.Runtime.Reminders.Delivery;
using Aluki.Runtime.Reminders.Persistence;
using Aluki.Runtime.Reminders.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-005 US1: one-shot reminder lifecycle against scoped PostgreSQL — create,
/// list, snooze, cancel, quota enforcement, and the timer fire-sweep with a fake
/// delivery channel. Skipped unless ALUKI_TEST_POSTGRES is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class ReminderLifecycleIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public ReminderLifecycleIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task One_shot_create_persists_scheduled_and_counts_quota()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var service = BuildService(clock);

        var result = await service.CreateAsync(CreateRequest(seed, "Call Ana", clock.GetUtcNow().AddMinutes(30)), CancellationToken.None);

        Assert.Equal(201, result.StatusCode);
        var response = Assert.IsType<ReminderResponse>(result.Body);
        Assert.Equal(ReminderResponseStatus.Created, response.Status);
        Assert.NotNull(response.Reminder);
        Assert.Equal(ReminderStatus.Scheduled, response.Reminder!.Status);
        Assert.Equal(1, response.Quota!.ActiveCount);

        Assert.Equal("scheduled", await ReminderStatusOf(seed.TenantId, response.Reminder.ReminderId));
        Assert.True(await CountAudit(seed.TenantId, response.Reminder.ReminderId, ReminderAuditEventType.Created) >= 1);
    }

    [Fact]
    public async Task Duplicate_create_is_idempotent()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var service = BuildService(clock);
        var when = clock.GetUtcNow().AddMinutes(30);

        var first = Assert.IsType<ReminderResponse>((await service.CreateAsync(CreateRequest(seed, "x", when, "rid-1"), CancellationToken.None)).Body);
        var second = Assert.IsType<ReminderResponse>((await service.CreateAsync(CreateRequest(seed, "x", when, "rid-1"), CancellationToken.None)).Body);

        Assert.Equal(first.Reminder!.ReminderId, second.Reminder!.ReminderId);
        Assert.Equal(1, await CountReminders(seed.TenantId));
    }

    [Fact]
    public async Task Snooze_reschedules_and_increments_count()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var service = BuildService(clock);
        var created = Assert.IsType<ReminderResponse>((await service.CreateAsync(CreateRequest(seed, "Call Ana", clock.GetUtcNow().AddMinutes(30)), CancellationToken.None)).Body);

        var snoozeResult = await service.SnoozeAsync(created.Reminder!.ReminderId,
            new SnoozeReminderRequest(null, Principal(seed), 900), CancellationToken.None);

        Assert.Equal(200, snoozeResult.StatusCode);
        var snoozed = Assert.IsType<ReminderResponse>(snoozeResult.Body);
        Assert.Equal(1, snoozed.Reminder!.SnoozeCount);
        Assert.True(await CountAudit(seed.TenantId, created.Reminder.ReminderId, ReminderAuditEventType.Snoozed) >= 1);
    }

    [Fact]
    public async Task Cancel_soft_deletes_and_frees_quota()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var service = BuildService(clock);
        var created = Assert.IsType<ReminderResponse>((await service.CreateAsync(CreateRequest(seed, "Call Ana", clock.GetUtcNow().AddMinutes(30)), CancellationToken.None)).Body);

        var cancel = await service.CancelAsync(created.Reminder!.ReminderId, new CancelReminderRequest(null, Principal(seed)), CancellationToken.None);

        Assert.Equal(200, cancel.StatusCode);
        Assert.Equal("user_cancelled", await ReminderStatusOf(seed.TenantId, created.Reminder.ReminderId));

        // Listing excludes soft-deleted reminders.
        var list = Assert.IsType<ReminderListResponse>((await service.ListAsync(Principal(seed), null, CancellationToken.None)).Body);
        Assert.DoesNotContain(list.Reminders, r => r.ReminderId == created.Reminder.ReminderId);
    }

    [Fact]
    public async Task Quota_block_returns_409_when_limit_reached()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        await SetQuotaLimit(seed.TenantId, 2);
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var service = BuildService(clock);

        await service.CreateAsync(CreateRequest(seed, "one", clock.GetUtcNow().AddMinutes(10), "a"), CancellationToken.None);
        await service.CreateAsync(CreateRequest(seed, "two", clock.GetUtcNow().AddMinutes(20), "b"), CancellationToken.None);
        var third = await service.CreateAsync(CreateRequest(seed, "three", clock.GetUtcNow().AddMinutes(30), "c"), CancellationToken.None);

        Assert.Equal(409, third.StatusCode);
        var body = Assert.IsType<ReminderResponse>(third.Body);
        Assert.Equal(ReminderResponseStatus.QuotaExceeded, body.Status);
    }

    [Fact]
    public async Task Sweep_fires_due_reminder_and_marks_delivered()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var channel = new RecordingChannel();
        var service = BuildService(clock, channel);
        var created = Assert.IsType<ReminderResponse>((await service.CreateAsync(CreateRequest(seed, "Call Ana", clock.GetUtcNow().AddMinutes(5)), CancellationToken.None)).Body);

        // Advance past the due time and run the sweep.
        clock.Advance(TimeSpan.FromMinutes(6));
        var processed = await service.FireDueAsync(CancellationToken.None);

        Assert.True(processed >= 1);
        Assert.Equal(1, channel.Calls);
        Assert.Equal("delivered", await ReminderStatusOf(seed.TenantId, created.Reminder!.ReminderId));
        Assert.Equal(1, await CountDeliveryAttempts(seed.TenantId, created.Reminder.ReminderId));
        Assert.True(await CountAudit(seed.TenantId, created.Reminder.ReminderId, ReminderAuditEventType.Delivered) >= 1);
    }

    [Fact]
    public async Task Recurring_daily_create_persists_rule_and_reminder()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var service = BuildService(clock);
        var rec = new ReminderRecurrenceInput("daily", null, null, "never", null);

        var result = await service.CreateAsync(RecurringRequest(seed, "Standup", clock.GetUtcNow().AddHours(1), rec), CancellationToken.None);

        Assert.Equal(201, result.StatusCode);
        var response = Assert.IsType<ReminderResponse>(result.Body);
        Assert.Equal(ReminderType.Recurring, response.Reminder!.ReminderType);
        Assert.Equal(1, await CountRecurrenceRules(seed.TenantId, response.Reminder.ReminderId));
    }

    [Fact]
    public async Task Sweep_reschedules_recurring_to_next_occurrence()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var channel = new RecordingChannel();
        var service = BuildService(clock, channel);
        var rec = new ReminderRecurrenceInput("daily", null, null, "never", null);
        var created = Assert.IsType<ReminderResponse>(
            (await service.CreateAsync(RecurringRequest(seed, "Standup", clock.GetUtcNow().AddMinutes(5), rec), CancellationToken.None)).Body);
        var firstFire = created.Reminder!.ScheduledTimeUtc;

        clock.Advance(TimeSpan.FromMinutes(6));
        await service.FireDueAsync(CancellationToken.None);

        // Delivered once, then re-armed to the next day at the same local time (status back to scheduled).
        Assert.Equal(1, channel.Calls);
        Assert.Equal("scheduled", await ReminderStatusOf(seed.TenantId, created.Reminder.ReminderId));
        var nextFire = await ScheduledTimeOf(seed.TenantId, created.Reminder.ReminderId);
        Assert.Equal(firstFire.AddDays(1), nextFire);
        Assert.Equal(1, await CountDeliveryAttempts(seed.TenantId, created.Reminder.ReminderId));
        Assert.True(await CountAudit(seed.TenantId, created.Reminder.ReminderId, ReminderAuditEventType.Delivered) >= 1);
    }

    [Fact]
    public async Task Transient_failure_retries_then_delivers()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var channel = new ScriptedChannel(
            new ReminderDeliveryResult(DeliveryStatus.TransientFailure, null, DeliveryFailureCategory.ServiceUnavailable, "temp"),
            new ReminderDeliveryResult(DeliveryStatus.Delivered, "ok"));
        var service = BuildService(clock, channel);
        var created = Assert.IsType<ReminderResponse>(
            (await service.CreateAsync(CreateRequest(seed, "Call Ana", clock.GetUtcNow().AddMinutes(5)), CancellationToken.None)).Body);

        clock.Advance(TimeSpan.FromMinutes(6));
        await service.FireDueAsync(CancellationToken.None);
        // After a transient failure the reminder stays in-flight, armed for retry.
        Assert.Equal("firing", await ReminderStatusOf(seed.TenantId, created.Reminder!.ReminderId));

        clock.Advance(TimeSpan.FromSeconds(10)); // past the 5s backoff
        await service.FireDueAsync(CancellationToken.None);

        Assert.Equal(2, channel.Calls);
        Assert.Equal("delivered", await ReminderStatusOf(seed.TenantId, created.Reminder.ReminderId));
        Assert.Equal(2, await CountDeliveryAttempts(seed.TenantId, created.Reminder.ReminderId));
    }

    [Fact]
    public async Task Transient_failure_exhausts_to_delivery_failed()
    {
        if (!_fixture.Available)
        {
            return;
        }

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var fail = new ReminderDeliveryResult(DeliveryStatus.TransientFailure, null, DeliveryFailureCategory.NetworkTimeout, "down");
        var channel = new ScriptedChannel(fail, fail, fail);
        var service = BuildService(clock, channel);
        var created = Assert.IsType<ReminderResponse>(
            (await service.CreateAsync(CreateRequest(seed, "Call Ana", clock.GetUtcNow().AddMinutes(5)), CancellationToken.None)).Body);

        clock.Advance(TimeSpan.FromMinutes(6));
        await service.FireDueAsync(CancellationToken.None);   // attempt 1 -> retry @ +5s
        clock.Advance(TimeSpan.FromSeconds(10));
        await service.FireDueAsync(CancellationToken.None);   // attempt 2 -> retry @ +25s
        clock.Advance(TimeSpan.FromSeconds(30));
        await service.FireDueAsync(CancellationToken.None);   // attempt 3 -> exhausted

        Assert.Equal(3, channel.Calls);
        Assert.Equal("delivery_failed", await ReminderStatusOf(seed.TenantId, created.Reminder!.ReminderId));
        Assert.Equal(3, await CountDeliveryAttempts(seed.TenantId, created.Reminder.ReminderId));
        Assert.True(await CountAudit(seed.TenantId, created.Reminder.ReminderId, ReminderAuditEventType.DeliveryFailed) >= 1);
    }

    private ReminderService BuildService(TimeProvider clock, IReminderDeliveryChannel? channel = null)
    {
        var factory = BuildFactory(_fixture.ConnectionString!);
        return new ReminderService(
            new ReminderScopeGuard(factory),
            new ReminderStore(factory),
            channel ?? new RecordingChannel(),
            Options.Create(new ReminderOptions()),
            NullLogger<ReminderService>.Instance,
            clock);
    }

    private static CreateReminderRequest CreateRequest(SeededPrincipal seed, string text, DateTimeOffset when, string? id = null) =>
        new(id, "c1", Principal(seed), text, when, "America/Mexico_City", "one_shot", null, "in_app");

    private static CreateReminderRequest RecurringRequest(SeededPrincipal seed, string text, DateTimeOffset when, ReminderRecurrenceInput rec, string? id = null) =>
        new(id, "c1", Principal(seed), text, when, "America/Mexico_City", "recurring", rec, "in_app");

    private static ReminderPrincipalContext Principal(SeededPrincipal seed) =>
        new(seed.TenantId, seed.ContextId, seed.UserId);

    private async Task<string?> ReminderStatusOf(Guid tenantId, Guid reminderId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select status from reminders where tenant_id = @t and reminder_id = @r;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("r", reminderId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task<int> CountRecurrenceRules(Guid tenantId, Guid reminderId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from reminder_recurrence_rules where tenant_id = @t and reminder_id = @r;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("r", reminderId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<DateTimeOffset> ScheduledTimeOf(Guid tenantId, Guid reminderId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select scheduled_time_utc from reminders where tenant_id = @t and reminder_id = @r;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("r", reminderId);
        var value = (DateTime)(await command.ExecuteScalarAsync())!;
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
    }

    private async Task<int> CountReminders(Guid tenantId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from reminders where tenant_id = @t;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountDeliveryAttempts(Guid tenantId, Guid reminderId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from reminder_delivery_attempts where tenant_id = @t and reminder_id = @r;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("r", reminderId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountAudit(Guid tenantId, Guid reminderId, string eventType)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from reminder_audit_events where tenant_id = @t and reminder_id = @r and event_type = @e;", connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("r", reminderId);
        command.Parameters.AddWithValue("e", eventType);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task SetQuotaLimit(Guid tenantId, int limit)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into reminder_quotas (tenant_id, quota_limit) values (@t, @l)
            on conflict (tenant_id) do update set quota_limit = @l;
            """, connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("l", limit);
        await command.ExecuteNonQueryAsync();
    }

    private static NpgsqlConnectionFactory BuildFactory(string connectionString) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = connectionString })
            .Build());

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) => _now = now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class RecordingChannel : IReminderDeliveryChannel
    {
        public int Calls { get; private set; }

        public Task<ReminderDeliveryResult> DeliverAsync(ReminderDeliveryRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ReminderDeliveryResult(DeliveryStatus.Delivered, $"notif-{request.ReminderId:N}"));
        }
    }

    /// <summary>Returns a scripted sequence of delivery outcomes (then delivers).</summary>
    private sealed class ScriptedChannel : IReminderDeliveryChannel
    {
        private readonly Queue<ReminderDeliveryResult> _results;
        public int Calls { get; private set; }

        public ScriptedChannel(params ReminderDeliveryResult[] results) => _results = new Queue<ReminderDeliveryResult>(results);

        public Task<ReminderDeliveryResult> DeliverAsync(ReminderDeliveryRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            var result = _results.Count > 0 ? _results.Dequeue() : new ReminderDeliveryResult(DeliveryStatus.Delivered, "ok");
            return Task.FromResult(result);
        }
    }
}
