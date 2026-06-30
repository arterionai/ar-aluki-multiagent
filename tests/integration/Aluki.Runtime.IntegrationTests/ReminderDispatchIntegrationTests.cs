using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Capture.Security;
using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Reminders;
using Aluki.Runtime.Reminders.Configuration;
using Aluki.Runtime.Reminders.Delivery;
using Aluki.Runtime.Reminders.Dispatch;
using Aluki.Runtime.Reminders.Persistence;
using Aluki.Runtime.Reminders.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Integration tests for the ReminderDomainAgent dispatch pipeline and principal resolution.
/// HandleAsync scenarios (19–29 of the test coverage plan) are here because ReminderService /
/// ReminderScopeGuard / ReminderStore are sealed concrete types that require a real PostgreSQL
/// connection. Also covers principal resolution (scenarios 30–32) and idempotency (41).
/// Scenarios 1–18 are in the unit/contract layers; scenarios 35–39 (full multi-agent dispatch)
/// are covered in MessageDispatchIntegrationTests combined with ClaimsIntent contract tests.
/// Tests self-skip when ALUKI_TEST_POSTGRES is not configured.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class ReminderDispatchIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public ReminderDispatchIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    // ── HandleAsync — happy paths ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ParseSuccess_creates_reminder_and_sends_confirmation()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var futureUtc = "2026-07-02T16:00:00Z";
        var llmJson = $$"""{"reminder_text": "comprar leche", "scheduled_time_utc": "{{futureUtc}}"}""";

        var messenger = new RecordingWhatsAppMessenger();
        var agent = BuildAgent(clock, llmJson, messenger, seed);
        var msg = MakeMessage("recuérdame comprar leche mañana", seed);
        var principal = MakePrincipal(seed);

        var result = await agent.HandleAsync(msg, principal, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("reminder_scheduled", result.OutcomeCode);
        Assert.Equal(1, messenger.SendCount);
        Assert.Contains("✅", messenger.LastMessage);
        Assert.Equal(1, await CountReminders(seed.TenantId));
    }

    [Fact]
    public async Task HandleAsync_ParseSuccess_no_timezone_in_memory_appends_city_question()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var llmJson = """{"reminder_text": "tomar pastilla", "scheduled_time_utc": "2026-07-02T09:00:00Z"}""";

        var messenger = new RecordingWhatsAppMessenger();
        // MemoryRecallService is null → NullReferenceException caught → uses default timezone (fromMemory=false)
        var agent = BuildAgent(clock, llmJson, messenger, seed);
        var msg = MakeMessage("recuérdame tomar la pastilla mañana a las 9am", seed);

        await agent.HandleAsync(msg, MakePrincipal(seed), CancellationToken.None);

        Assert.Equal(1, messenger.SendCount);
        Assert.Contains("¿En qué ciudad vives?", messenger.LastMessage);
    }

    [Fact]
    public async Task HandleAsync_StatusCode200_idempotent_create_also_sends_confirmation()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var correlationId = Guid.NewGuid().ToString("N");
        var llmJson = """{"reminder_text": "llamar a mamá", "scheduled_time_utc": "2026-07-02T16:00:00Z"}""";
        var messenger = new RecordingWhatsAppMessenger();

        var agent = BuildAgent(clock, llmJson, messenger, seed);
        var msg = MakeMessage("recuérdame llamar a mamá mañana", seed, correlationId: correlationId);

        // First call → 201 Created
        var r1 = await agent.HandleAsync(msg, MakePrincipal(seed), CancellationToken.None);
        Assert.True(r1.Success);

        // Second call (same correlationId = same reminderId logic in HandleAsync → duplicate → 200)
        var r2 = await agent.HandleAsync(msg, MakePrincipal(seed), CancellationToken.None);
        Assert.True(r2.Success);
        Assert.Equal(2, messenger.SendCount);
        Assert.Contains("✅", messenger.LastMessage);
    }

    // ── HandleAsync — error paths ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ParseFail_sends_clarification_and_creates_no_reminder()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var llmJson = """{"reminder_text": "algo", "scheduled_time_utc": null}""";
        var messenger = new RecordingWhatsAppMessenger();

        var agent = BuildAgent(clock, llmJson, messenger, seed);
        var msg = MakeMessage("recuérdame algo", seed);

        var result = await agent.HandleAsync(msg, MakePrincipal(seed), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("reminder_clarification_needed", result.OutcomeCode);
        Assert.Equal(1, messenger.SendCount);
        Assert.Contains("No pude entender cuándo", messenger.LastMessage);
        Assert.Equal(0, await CountReminders(seed.TenantId));
    }

    [Fact]
    public async Task HandleAsync_QuotaExceeded_sends_quota_message()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        await SetQuotaLimit(seed.TenantId, 1);
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));

        // Fill the quota with a direct service call
        var service = BuildService(clock);
        await service.CreateAsync(
            new CreateReminderRequest(null, "fill-1", ReminderPrincipalOf(seed),
                "fill quota", clock.GetUtcNow().AddHours(1), "America/Mexico_City",
                ReminderType.OneShot, null, "in_app"),
            CancellationToken.None);

        var llmJson = """{"reminder_text": "recordatorio extra", "scheduled_time_utc": "2026-07-02T16:00:00Z"}""";
        var messenger = new RecordingWhatsAppMessenger();
        var agent = BuildAgent(clock, llmJson, messenger, seed);
        var msg = MakeMessage("recuérdame hacer algo mañana", seed);

        var result = await agent.HandleAsync(msg, MakePrincipal(seed), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("reminder_failed", result.OutcomeCode);
        Assert.Equal(1, messenger.SendCount);
        Assert.Contains("demasiados recordatorios activos", messenger.LastMessage);
    }

    [Fact]
    public async Task HandleAsync_PastDateFromLlm_sends_past_date_message()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        // LLM returns a date clearly in the past — ReminderService returns 400 and
        // the real DateTimeOffset.UtcNow also confirms the parsed date is past.
        var llmJson = """{"reminder_text": "algo", "scheduled_time_utc": "2020-01-01T00:00:00Z"}""";
        var messenger = new RecordingWhatsAppMessenger();

        var agent = BuildAgent(clock, llmJson, messenger, seed);
        var msg = MakeMessage("recuérdame algo en 2020", seed);

        var result = await agent.HandleAsync(msg, MakePrincipal(seed), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("reminder_failed", result.OutcomeCode);
        Assert.Equal(1, messenger.SendCount);
        Assert.Contains("La fecha que mencionaste ya pasó", messenger.LastMessage);
    }

    [Fact]
    public async Task HandleAsync_LlmException_sends_fallback_with_None_token()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var messenger = new RecordingWhatsAppMessenger();

        // ThrowingChatModelRouter simulates the LLM going down — TaskCanceledException
        // wraps a SocketException (the real-world bug) or any other runtime failure.
        var parser = new ReminderIntentParser(
            new ThrowingChatModelRouter(new TaskCanceledException("simulated LLM timeout")),
            NullLogger<ReminderIntentParser>.Instance);
        var agent = new ReminderDomainAgent(
            BuildService(clock), parser, null!, messenger, NullLogger<ReminderDomainAgent>.Instance);
        var msg = MakeMessage("recuérdame algo mañana", seed);

        var result = await agent.HandleAsync(msg, MakePrincipal(seed), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("reminder_exception", result.ErrorCode);
        // The catch block MUST use CancellationToken.None so the send is not skipped
        // even when the webhook ct is already canceled.
        Assert.Equal(1, messenger.SendCount);
        Assert.Contains("No pude crear el recordatorio", messenger.LastMessage);
        Assert.Equal(0, await CountReminders(seed.TenantId));
    }

    [Fact]
    public async Task HandleAsync_WebhookCtPreCanceled_still_delivers_reply()
    {
        if (!_fixture.Available) return;

        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var llmJson = """{"reminder_text": "test", "scheduled_time_utc": "2026-07-02T10:00:00Z"}""";
        var messenger = new RecordingWhatsAppMessenger();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // simulate webhook ct already canceled before HandleAsync runs

        var agent = BuildAgent(clock, llmJson, messenger, seed);
        var msg = MakeMessage("recuérdame test", seed);

        // Even with a pre-canceled ct, the LLM parser uses its own standalone token and
        // the reply sends use CancellationToken.None — so the user always gets a response.
        // The caller ct cancellation surfaces as an OperationCanceledException from the parser
        // (ct.ThrowIfCancellationRequested after LLM completes), caught by the agent's catch block.
        var result = await agent.HandleAsync(msg, MakePrincipal(seed), cts.Token);

        // The catch block fires (ct was canceled), sends fallback with CancellationToken.None.
        Assert.Equal(1, messenger.SendCount);
    }

    // ── Principal resolution ──────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_KnownSender_14252307522_resolves_to_SheloNabel_tenant()
    {
        if (!_fixture.Available) return;

        var resolver = BuildResolver();
        var identity = new ChannelIdentity(
            SourceChannel: "whatsapp",
            SenderExternalId: "14252307522",
            TenantHint: null,
            ContextId: null,
            CorrelationId: Guid.NewGuid().ToString("N"));

        var resolution = await resolver.ResolveAsync(identity, CancellationToken.None);

        Assert.True(resolution.Success);
        Assert.NotNull(resolution.Principal);
        // Migration 026 seeds this sender under the Sheló NABEL tenant.
        Assert.Equal(Guid.Parse("c0c0c0c0-5e10-4000-a000-000000000001"), resolution.Principal!.TenantId);
    }

    [Fact]
    public async Task ResolveAsync_UnknownSender_autoprovisions_new_principal()
    {
        if (!_fixture.Available) return;

        var resolver = BuildResolver();
        var newWaId = $"test-{Guid.NewGuid():N}";
        var identity = new ChannelIdentity(
            SourceChannel: "whatsapp",
            SenderExternalId: newWaId,
            TenantHint: null,
            ContextId: null,
            CorrelationId: Guid.NewGuid().ToString("N"));

        var resolution = await resolver.ResolveAsync(identity, CancellationToken.None);

        Assert.True(resolution.Success);
        Assert.NotNull(resolution.Principal);
        Assert.NotEqual(Guid.Empty, resolution.Principal!.TenantId);
        Assert.NotEqual(Guid.Empty, resolution.Principal.UserId);
        // Second resolve of the same sender must return the same principal (idempotent).
        var resolution2 = await resolver.ResolveAsync(identity, CancellationToken.None);
        Assert.True(resolution2.Success);
        Assert.Equal(resolution.Principal.UserId, resolution2.Principal!.UserId);
        Assert.Equal(resolution.Principal.TenantId, resolution2.Principal.TenantId);
    }

    [Fact]
    public async Task ResolveAsync_ConcurrentAutoProvisioning_is_idempotent()
    {
        if (!_fixture.Available) return;

        var newWaId = $"concurrent-{Guid.NewGuid():N}";
        var identity = new ChannelIdentity("whatsapp", newWaId, null, null, "corr-concurrent");

        // Run two concurrent resolutions for the same unknown sender.
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => BuildResolver().ResolveAsync(identity, CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // All must succeed and resolve to the same user/tenant (ON CONFLICT DO NOTHING idempotency).
        Assert.All(results, r => Assert.True(r.Success));
        var distinctUsers = results.Select(r => r.Principal!.UserId).Distinct().ToList();
        Assert.Single(distinctUsers);
        var distinctTenants = results.Select(r => r.Principal!.TenantId).Distinct().ToList();
        Assert.Single(distinctTenants);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_reminders_different_correlationIds_both_persisted()
    {
        if (!_fixture.Available) return;

        // Reminder dedup is keyed by explicit ReminderId (or correlationId), NOT by text.
        // Two identical-text requests with different correlationIds → 2 distinct rows.
        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var service = BuildService(clock);

        var req1 = new CreateReminderRequest(null, "corr-a", ReminderPrincipalOf(seed),
            "comprar pan", clock.GetUtcNow().AddHours(1), "America/Mexico_City",
            ReminderType.OneShot, null, "whatsapp:10827382989:14252307522");
        var req2 = new CreateReminderRequest(null, "corr-b", ReminderPrincipalOf(seed),
            "comprar pan", clock.GetUtcNow().AddHours(2), "America/Mexico_City",
            ReminderType.OneShot, null, "whatsapp:10827382989:14252307522");

        var r1 = await service.CreateAsync(req1, CancellationToken.None);
        var r2 = await service.CreateAsync(req2, CancellationToken.None);

        Assert.Equal(201, r1.StatusCode);
        Assert.Equal(201, r2.StatusCode);
        Assert.Equal(2, await CountReminders(seed.TenantId));
    }

    // ── Regression guards ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_TimezoneWithSpecialChars_does_not_throw_FormatException()
    {
        if (!_fixture.Available) return;

        // Regression guard for the string.Format bug: SystemPromptTemplate used unescaped {}
        // which string.Format mistook for placeholders — causing FormatException in production.
        var seed = await _fixture.SeedPrincipalAsync();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-01T10:00:00Z"));
        var llmJson = """{"reminder_text": "test", "scheduled_time_utc": "2026-07-02T10:00:00Z"}""";
        var messenger = new RecordingWhatsAppMessenger();
        var agent = BuildAgent(clock, llmJson, messenger, seed);

        // Use timezones that historically tripped the naive string.Format path.
        foreach (var tz in new[] { "America/New_York", "Europe/Madrid", "UTC", "America/Mexico_City" })
        {
            var exception = await Record.ExceptionAsync(() =>
                agent.HandleAsync(MakeMessage("recuérdame test mañana", seed), MakePrincipal(seed), CancellationToken.None));
            Assert.Null(exception); // must never throw; reminder result may vary
        }
    }

    // ── Builder helpers ───────────────────────────────────────────────────────

    private ReminderDomainAgent BuildAgent(
        TimeProvider clock,
        string llmReply,
        IWhatsAppMessenger messenger,
        SeededPrincipal seed)
    {
        var parser = new ReminderIntentParser(
            new StubChatModelRouter(llmReply),
            NullLogger<ReminderIntentParser>.Instance);
        // MemoryRecallService is null → NullReferenceException in ResolveUserTimezoneAsync is caught
        // → falls back to DefaultTimezone ("America/Mexico_City"). Safe because the try/catch is inside
        // the agent and returning (DefaultTimezone, false) is the explicit fallback path.
        return new ReminderDomainAgent(
            BuildService(clock), parser, null!, messenger, NullLogger<ReminderDomainAgent>.Instance);
    }

    private ReminderService BuildService(TimeProvider clock) =>
        new(new ReminderScopeGuard(BuildFactory()),
            new ReminderStore(BuildFactory()),
            new RecordingReminderChannel(),
            Options.Create(new ReminderOptions()),
            NullLogger<ReminderService>.Instance,
            clock);

    private PrincipalContextResolver BuildResolver() =>
        new(BuildFactory(), NullLogger<PrincipalContextResolver>.Instance);

    private NpgsqlConnectionFactory BuildFactory() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = _fixture.ConnectionString })
            .Build());

    private static PrincipalContext MakePrincipal(SeededPrincipal seed) =>
        new(UserId: seed.UserId, TenantId: seed.TenantId, ContextId: seed.ContextId,
            Roles: ["OWNER"], SourceChannel: "whatsapp", CorrelationId: Guid.NewGuid().ToString("N"));

    private static ReminderPrincipalContext ReminderPrincipalOf(SeededPrincipal seed) =>
        new(seed.TenantId, seed.ContextId, seed.UserId);

    private static UnifiedMessage MakeMessage(string text, SeededPrincipal seed, string? correlationId = null) =>
        new(MessageId: Guid.NewGuid().ToString("N"),
            ChannelType: ChannelType.WhatsApp,
            Text: text,
            MediaRefs: [],
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: correlationId ?? Guid.NewGuid().ToString("N"),
            SenderExternalId: seed.ExternalId,
            PhoneNumberId: "10827382989");

    // ── DB helpers ────────────────────────────────────────────────────────────

    private async Task<int> CountReminders(Guid tenantId)
    {
        await using var connection = await _fixture.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from reminders where tenant_id = @t;", connection);
        command.Parameters.AddWithValue("t", tenantId);
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

    // ── Private test doubles ──────────────────────────────────────────────────

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class RecordingReminderChannel : IReminderDeliveryChannel
    {
        public Task<ReminderDeliveryResult> DeliverAsync(ReminderDeliveryRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ReminderDeliveryResult(DeliveryStatus.Delivered, $"notif-{request.ReminderId:N}"));
    }

    private sealed class RecordingWhatsAppMessenger : IWhatsAppMessenger
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;
        public int SendCount => _messages.Count;
        public string? LastMessage => _messages.Count > 0 ? _messages[^1] : null;

        public Task MarkReadAndShowTypingAsync(string phoneNumberId, string messageId, CancellationToken ct)
            => Task.CompletedTask;

        public Task SendTextMessageAsync(string phoneNumberId, string recipientWaId, string text, CancellationToken ct)
        {
            _messages.Add(text);
            return Task.CompletedTask;
        }
    }
}

// ── File-scoped test doubles ──────────────────────────────────────────────────

file sealed class StubChatModelRouter(string reply) : IChatModelRouter
{
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        => Task.FromResult(reply);
}

file sealed class ThrowingChatModelRouter(Exception toThrow) : IChatModelRouter
{
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        => Task.FromException<string>(toThrow);
}
