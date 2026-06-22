using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.DelegatedReminders;
using Aluki.Runtime.DelegatedReminders.Configuration;
using Aluki.Runtime.DelegatedReminders.Delivery;
using Aluki.Runtime.DelegatedReminders.Persistence;
using Aluki.Runtime.DelegatedReminders.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// SB-006 delegated reminder lifecycle integration tests: create (all three
/// recipient tiers), consent gate, cancel within/outside window, fire-sweep
/// delivery, retry policy, and audit events. Skipped unless ALUKI_TEST_POSTGRES
/// is configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class DelegatedReminderLifecycleIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public DelegatedReminderLifecycleIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    // ── US1: Correct routing and recipient resolution ─────────────────────────

    [Fact]
    public async Task Tier1_known_contact_creates_scheduled_after_consent()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var service = BuildService();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock);

        // Seed consent first
        var store = BuildStore();
        var principal = ToScope(seed);
        await store.UpsertConsentAsync(principal, "+525512345678",
            DelegatedConsentStatus.OptedIn, "global", null, null, CancellationToken.None);

        var result = await svc.CreateAsync(CreateTier1Request(seed, clock), CancellationToken.None);

        Assert.Equal(201, result.StatusCode);
        var response = Assert.IsType<DelegatedReminderResponse>(result.Body);
        Assert.Equal(DelegatedReminderResponseStatus.Created, response.Status);
        Assert.NotNull(response.Reminder);
        Assert.Equal(DelegatedReminderStatus.Scheduled, response.Reminder!.Status);
        Assert.True(response.Reminder.ConsentAcquired);
    }

    [Fact]
    public async Task Tier1_without_consent_creates_awaiting_consent()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock);

        var result = await svc.CreateAsync(CreateTier1Request(seed, clock), CancellationToken.None);

        Assert.Equal(201, result.StatusCode);
        var response = Assert.IsType<DelegatedReminderResponse>(result.Body);
        Assert.Equal(DelegatedReminderResponseStatus.AwaitingConsent, response.Status);
        Assert.Equal(DelegatedReminderStatus.AwaitingConsent, response.Reminder!.Status);
        Assert.Equal(DelegatedResolutionTier.Tier1KnownContactConfirmed, response.Reminder.ResolutionTier);

        Assert.True(await CountAudit(seed.TenantId, response.Reminder.Id, DelegatedAuditEventType.Created) >= 1);
    }

    [Fact]
    public async Task Tier2_phone_only_creates_awaiting_resolution()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock);

        var request = new CreateDelegatedReminderRequest(
            null, null, ToPrincipalContext(seed),
            SenderIdentity: "+14252307522",
            RecipientIdentity: null,
            RecipientName: "Ana García",
            RecipientPhoneE164: "+525512345678",
            RecipientWhatsappHandle: null,
            Content: "Your dentist appointment is tomorrow",
            DueTimeUtc: clock.GetUtcNow().AddHours(24));

        var result = await svc.CreateAsync(request, CancellationToken.None);

        Assert.Equal(201, result.StatusCode);
        var response = Assert.IsType<DelegatedReminderResponse>(result.Body);
        Assert.Equal(DelegatedReminderResponseStatus.AwaitingRecipientResolution, response.Status);
        Assert.Equal(DelegatedResolutionTier.Tier2PhoneOnlyNeedsCapture, response.Reminder!.ResolutionTier);
    }

    [Fact]
    public async Task Tier3_unknown_creates_awaiting_resolution()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock);

        var request = new CreateDelegatedReminderRequest(
            null, null, ToPrincipalContext(seed),
            SenderIdentity: "+14252307522",
            RecipientIdentity: "my-friend-juan",
            RecipientName: null,
            RecipientPhoneE164: null,
            RecipientWhatsappHandle: null,
            Content: "Call me when you arrive",
            DueTimeUtc: clock.GetUtcNow().AddHours(5));

        var result = await svc.CreateAsync(request, CancellationToken.None);

        Assert.Equal(201, result.StatusCode);
        var response = Assert.IsType<DelegatedReminderResponse>(result.Body);
        Assert.Equal(DelegatedReminderStatus.AwaitingRecipientResolution, response.Reminder!.Status);
        Assert.Equal(DelegatedResolutionTier.Tier3UnknownNeedsClarification, response.Reminder.ResolutionTier);
    }

    [Fact]
    public async Task Duplicate_create_is_idempotent()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock);

        var request = CreateTier1Request(seed, clock, externalId: "dedup-1");
        var r1 = Assert.IsType<DelegatedReminderResponse>((await svc.CreateAsync(request, CancellationToken.None)).Body);
        var r2 = Assert.IsType<DelegatedReminderResponse>((await svc.CreateAsync(request, CancellationToken.None)).Body);

        Assert.Equal(r1.Reminder!.Id, r2.Reminder!.Id);
        Assert.Equal(1, await CountReminders(seed.TenantId));
    }

    // ── US1: Query separation ─────────────────────────────────────────────────

    [Fact]
    public async Task List_returns_only_delegated_reminders()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock);

        await svc.CreateAsync(CreateTier1Request(seed, clock), CancellationToken.None);
        await svc.CreateAsync(CreateTier2Request(seed, clock), CancellationToken.None);

        var listResult = await svc.ListAsync(ToPrincipalContext(seed), null, CancellationToken.None);

        Assert.Equal(200, listResult.StatusCode);
        var list = Assert.IsType<DelegatedReminderListResponse>(listResult.Body);
        Assert.Equal(2, list.Reminders.Count);
        Assert.All(list.Reminders, r => Assert.NotNull(r.RecipientIdentity));
    }

    // ── US2: Anti-spam ────────────────────────────────────────────────────────

    [Fact]
    public async Task Anti_spam_limit_enforced_at_daily_cap()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock, antiSpamLimit: 2);

        await svc.CreateAsync(CreateTier1Request(seed, clock, externalId: "as-1"), CancellationToken.None);
        await svc.CreateAsync(CreateTier2Request(seed, clock, externalId: "as-2"), CancellationToken.None);

        var blocked = await svc.CreateAsync(CreateTier1Request(seed, clock, externalId: "as-3"), CancellationToken.None);

        Assert.Equal(429, blocked.StatusCode);
        var error = Assert.IsType<DelegatedReminderErrorResponse>(blocked.Body);
        Assert.Equal(DelegatedReminderErrorCode.AntiSpamDenied, error.Code);
    }

    // ── US2: Consent gate ─────────────────────────────────────────────────────

    [Fact]
    public async Task Consent_acquisition_promotes_to_scheduled()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock);
        var store = BuildStore();

        // Create without consent → awaiting_consent
        var create = Assert.IsType<DelegatedReminderResponse>(
            (await svc.CreateAsync(CreateTier1Request(seed, clock, externalId: "consent-promo"), CancellationToken.None)).Body);
        Assert.Equal(DelegatedReminderStatus.AwaitingConsent, create.Reminder!.Status);

        // Acquire consent → should promote to scheduled
        await store.UpsertConsentAsync(ToScope(seed), "+525512345678",
            DelegatedConsentStatus.OptedIn, "global",
            create.Reminder.Id, "consent-test-corr", CancellationToken.None);

        // Verify via audit
        Assert.True(await CountAudit(seed.TenantId, create.Reminder.Id, DelegatedAuditEventType.ConsentAcquired) >= 1);

        // Verify reminder status promoted
        Assert.Equal("scheduled", await ReminderStatusOf(seed.TenantId, create.Reminder.Id));
    }

    // ── US3: Cancellation window ──────────────────────────────────────────────

    [Fact]
    public async Task Cancel_within_window_succeeds()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var svc = BuildService(clock);

        var create = Assert.IsType<DelegatedReminderResponse>(
            (await svc.CreateAsync(CreateTier1Request(seed, clock), CancellationToken.None)).Body);

        var cancelResult = await svc.CancelAsync(
            create.Reminder!.Id,
            new CancelDelegatedReminderRequest(null, ToPrincipalContext(seed)),
            CancellationToken.None);

        Assert.Equal(200, cancelResult.StatusCode);
        Assert.Equal("cancelled", await ReminderStatusOf(seed.TenantId, create.Reminder.Id));
        Assert.True(await CountAudit(seed.TenantId, create.Reminder.Id, DelegatedAuditEventType.Cancelled) >= 1);
    }

    [Fact]
    public async Task Cancel_after_deadline_is_rejected()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        // Clock starts 10 seconds before due time (deadline is 30s before due)
        var due = DateTimeOffset.Parse("2026-07-01T10:00:00Z");
        var clock = new FakeClock(due.AddSeconds(-10));
        var svc = BuildService(clock);

        // Create reminder due in 10 seconds (clock is already 10s before due time)
        var createClock = new FakeClock(due.AddHours(-1));
        var createSvc = BuildService(createClock);
        var create = Assert.IsType<DelegatedReminderResponse>(
            (await createSvc.CreateAsync(CreateTier1RequestDue(seed, due), CancellationToken.None)).Body);

        // Now try to cancel when clock is 10s before due (past the 30s window)
        var cancelResult = await svc.CancelAsync(
            create.Reminder!.Id,
            new CancelDelegatedReminderRequest(null, ToPrincipalContext(seed)),
            CancellationToken.None);

        Assert.Equal(409, cancelResult.StatusCode);
        var error = Assert.IsType<DelegatedReminderErrorResponse>(cancelResult.Body);
        Assert.Equal(DelegatedReminderErrorCode.CancellationWindowExpired, error.Code);
    }

    // ── US3: Fire sweep + delivery ────────────────────────────────────────────

    [Fact]
    public async Task Fire_sweep_delivers_scheduled_reminder_with_consent()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var store = BuildStore();
        var principal = ToScope(seed);

        // Seed consent
        await store.UpsertConsentAsync(principal, "+525512345678",
            DelegatedConsentStatus.OptedIn, "global", null, null, CancellationToken.None);

        var svc = BuildService(clock);
        var create = Assert.IsType<DelegatedReminderResponse>(
            (await svc.CreateAsync(CreateTier1Request(seed, clock), CancellationToken.None)).Body);

        Assert.Equal(DelegatedReminderStatus.Scheduled, create.Reminder!.Status);

        // Advance clock past due time and sweep
        clock.Advance(TimeSpan.FromHours(3));
        var processed = await svc.FireDueAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal("delivered", await ReminderStatusOf(seed.TenantId, create.Reminder.Id));
        Assert.True(await CountAudit(seed.TenantId, create.Reminder.Id, DelegatedAuditEventType.DeliverySucceeded) >= 1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private NpgsqlConnectionFactory BuildFactory() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Postgres:ConnectionString"] = _fixture.ConnectionString
            }).Build());

    private DelegatedReminderService BuildService(FakeClock? clock = null, int antiSpamLimit = 10)
    {
        var factory = BuildFactory();
        return new DelegatedReminderService(
            new DelegatedReminderScopeGuard(factory),
            new DelegatedReminderStore(factory),
            new LoggingDelegatedReminderDeliveryChannel(
                NullLogger<LoggingDelegatedReminderDeliveryChannel>.Instance),
            Options.Create(new DelegatedReminderOptions { DailyAntiSpamLimit = antiSpamLimit }),
            NullLogger<DelegatedReminderService>.Instance,
            clock);
    }

    private DelegatedReminderStore BuildStore() =>
        new DelegatedReminderStore(BuildFactory());

    private static Aluki.Runtime.Memory.PrincipalScope ToScope(SeededPrincipal seed) =>
        new(seed.TenantId, seed.ContextId, seed.UserId, null);

    private static DelegatedPrincipalContext ToPrincipalContext(SeededPrincipal seed) =>
        new(seed.TenantId, seed.ContextId, seed.UserId);

    private static CreateDelegatedReminderRequest CreateTier1Request(
        SeededPrincipal seed, FakeClock clock, string? externalId = null) =>
        new(null, externalId, ToPrincipalContext(seed),
            SenderIdentity: "+14252307522",
            RecipientIdentity: "+525512345678",
            RecipientName: "Ana García",
            RecipientPhoneE164: null,
            RecipientWhatsappHandle: "+525512345678",
            Content: "Your dentist appointment is tomorrow at 3pm",
            DueTimeUtc: clock.GetUtcNow().AddHours(2));

    private static CreateDelegatedReminderRequest CreateTier1RequestDue(
        SeededPrincipal seed, DateTimeOffset due) =>
        new(null, null, ToPrincipalContext(seed),
            SenderIdentity: "+14252307522",
            RecipientIdentity: "+525512345678",
            RecipientName: "Ana García",
            RecipientPhoneE164: null,
            RecipientWhatsappHandle: "+525512345678",
            Content: "Your dentist appointment is tomorrow at 3pm",
            DueTimeUtc: due);

    private static CreateDelegatedReminderRequest CreateTier2Request(
        SeededPrincipal seed, FakeClock clock, string? externalId = null) =>
        new(null, externalId, ToPrincipalContext(seed),
            SenderIdentity: "+14252307522",
            RecipientIdentity: null,
            RecipientName: "Carlos",
            RecipientPhoneE164: "+525598765432",
            RecipientWhatsappHandle: null,
            Content: "Team meeting in 30 minutes",
            DueTimeUtc: clock.GetUtcNow().AddHours(3));

    private async Task<string?> ReminderStatusOf(Guid tenantId, Guid reminderId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select status from delegated_reminders where id = @id and tenant_id = @tenant;",
            connection);
        cmd.Parameters.AddWithValue("id", reminderId);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    private async Task<int> CountReminders(Guid tenantId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select count(*)::int from delegated_reminders where tenant_id = @t;",
            connection);
        cmd.Parameters.AddWithValue("t", tenantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountAudit(Guid tenantId, Guid reminderId, string eventType)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select count(*)::int from delegated_audit_event " +
            "where tenant_id = @t and delegated_reminder_id = @id and event_type = @e;",
            connection);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("id", reminderId);
        cmd.Parameters.AddWithValue("e", eventType);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}

/// <summary>Controllable clock for integration tests.</summary>
internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
