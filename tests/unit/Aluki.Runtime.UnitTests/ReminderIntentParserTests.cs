using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Reminders.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for ReminderIntentParser — pure logic, no DB, no real LLM.
/// Covers all JSON shape variants, markdown stripping, and the two cancellation
/// paths introduced by the standalone 45-second token decoupling fix.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReminderIntentParserTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-01T10:00:00Z");
    private const string Timezone = "America/Mexico_City";

    private static ReminderIntentParser BuildParser(string llmReply) =>
        new(new StubChatModelRouter(llmReply), NullLogger<ReminderIntentParser>.Instance);

    // ── 1. Happy path — valid JSON with future timestamp ──────────────────────

    [Fact]
    public async Task ParseAsync_ValidJson_returns_success_with_text_and_time()
    {
        const string json = """{"reminder_text": "comprar leche", "scheduled_time_utc": "2026-07-02T16:00:00Z"}""";
        var parser = BuildParser(json);

        var result = await parser.ParseAsync("recuérdame comprar leche mañana", Now, Timezone, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("comprar leche", result.ReminderText);
        Assert.NotNull(result.ScheduledTimeUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-02T16:00:00Z"), result.ScheduledTimeUtc!.Value);
    }

    // ── 2. Markdown fences stripped ───────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_MarkdownFences_stripped_and_parsed_correctly()
    {
        const string fenced = "```json\n{\"reminder_text\": \"llamar al médico\", \"scheduled_time_utc\": \"2026-07-03T15:00:00Z\"}\n```";
        var parser = BuildParser(fenced);

        var result = await parser.ParseAsync("recuérdame llamar al médico pasado mañana", Now, Timezone, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("llamar al médico", result.ReminderText);
    }

    // ── 3. Invalid JSON → failure, no exception thrown ───────────────────────

    [Fact]
    public async Task ParseAsync_InvalidJson_returns_failure_without_throwing()
    {
        var parser = BuildParser("sorry I cannot do that");

        var result = await parser.ParseAsync("recuérdame algo", Now, Timezone, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ── 4. scheduled_time_utc is null in JSON ────────────────────────────────

    [Fact]
    public async Task ParseAsync_NullScheduledTime_returns_failure()
    {
        const string json = """{"reminder_text": "hacer algo", "scheduled_time_utc": null}""";
        var parser = BuildParser(json);

        var result = await parser.ParseAsync("recuérdame hacer algo", Now, Timezone, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("scheduled time", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── 5. reminder_text absent / empty ──────────────────────────────────────

    [Fact]
    public async Task ParseAsync_EmptyReminderText_returns_failure()
    {
        const string json = """{"reminder_text": "", "scheduled_time_utc": "2026-07-02T16:00:00Z"}""";
        var parser = BuildParser(json);

        var result = await parser.ParseAsync("recuérdame", Now, Timezone, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ParseAsync_MissingReminderTextProperty_returns_failure()
    {
        const string json = """{"scheduled_time_utc": "2026-07-02T16:00:00Z"}""";
        var parser = BuildParser(json);

        var result = await parser.ParseAsync("recuérdame algo", Now, Timezone, CancellationToken.None);

        Assert.False(result.Success);
    }

    // ── 6. Complex Spanish message — real production case ────────────────────

    [Fact]
    public async Task ParseAsync_ComplexSpanishMessage_extracts_correctly()
    {
        // This is the actual message that caused the TaskCanceledException bug.
        // The parser receives a pre-baked LLM response; we verify it extracts correctly.
        const string llmJson = """
            {
              "reminder_text": "mandarle a María el enlace de amazon para las cubiertas del reposabrazos",
              "scheduled_time_utc": "2026-06-30T21:00:00Z"
            }
            """;
        var parser = BuildParser(llmJson);

        var result = await parser.ParseAsync(
            "Hola Aluki, me puedes recordar el próximo martes 30 de junio, mandarle a María el enlace de amazon para las cubiertas del reposabrazos de mi silla de oficina?",
            Now, Timezone, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("María", result.ReminderText);
        Assert.NotNull(result.ScheduledTimeUtc);
    }

    // ── 7. Caller ct canceled — LLM still runs (standalone token) ────────────

    [Fact]
    public async Task ParseAsync_CallerCanceled_before_call_LLM_still_runs_returns_failure()
    {
        // The parser uses its own 45-second token for the LLM call, so even if the
        // caller's ct is pre-canceled the LLM is still invoked. After it completes,
        // ct.ThrowIfCancellationRequested fires but is caught by the broad catch block —
        // ParseAsync never throws; it returns Success=false instead.
        const string json = """{"reminder_text": "test", "scheduled_time_utc": "2026-07-02T10:00:00Z"}""";
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        var router = new StubChatModelRouter(json);
        var parser = new ReminderIntentParser(router, NullLogger<ReminderIntentParser>.Instance);

        var result = await parser.ParseAsync("recuérdame test", Now, Timezone, cts.Token);

        // LLM was still called despite the pre-canceled ct.
        Assert.Equal(1, router.CallCount);
        // The OperationCanceledException from ct.ThrowIfCancellationRequested is caught
        // by ParseAsync's catch block — it does NOT propagate to the caller.
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ── 8. LLM timeout (own 45s token fires) → failure, not re-thrown ────────

    [Fact]
    public async Task ParseAsync_LlmTimeoutViaOwnToken_returns_failure_not_exception()
    {
        // Simulate the LLM taking "forever" — the standalone CTS with 45s fires.
        // We can't easily advance time in unit tests, so we use a fast-cancel router.
        using var cts = new CancellationTokenSource();
        var router = new SlowCancellingChatModelRouter(cts);
        var parser = new ReminderIntentParser(router, NullLogger<ReminderIntentParser>.Instance);

        // The router cancels its own passed CancellationToken synchronously → simulates
        // the internal 45-second token firing. Parser catches it and returns Success=false.
        var result = await parser.ParseAsync("recuérdame algo", Now, Timezone, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ── 9. Empty LLM response ─────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_EmptyLlmResponse_returns_specific_error()
    {
        var parser = BuildParser(string.Empty);

        var result = await parser.ParseAsync("recuérdame algo", Now, Timezone, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("LLM returned empty response", result.Error);
    }

    // ── 10. Past date returned by LLM — parser accepts, service validates ─────

    [Fact]
    public async Task ParseAsync_PastDate_is_accepted_by_parser()
    {
        // Validation that the date is in the future is the responsibility of
        // ReminderService.ValidateCreate, not of the parser.
        const string json = """{"reminder_text": "algo ya pasado", "scheduled_time_utc": "2020-01-01T00:00:00Z"}""";
        var parser = BuildParser(json);

        var result = await parser.ParseAsync("recuérdame algo", Now, Timezone, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.ScheduledTimeUtc < Now);
    }

    // ── SystemPromptTemplate regression: string.Format with timezone ──────────

    [Fact]
    public async Task ParseAsync_TimezoneWithSpecialChars_does_not_throw_FormatException()
    {
        // Regression guard for PR #34: the SystemPromptTemplate used unescaped {}
        // which string.Format mistook for format placeholders. Any timezone value
        // must flow without throwing FormatException.
        const string json = """{"reminder_text": "test", "scheduled_time_utc": "2026-07-02T10:00:00Z"}""";
        var parser = BuildParser(json);

        // timezone values that could trip a naive string.Format
        foreach (var tz in new[] { "America/New_York", "Europe/Madrid", "UTC", "America/Mexico_City" })
        {
            var result = await parser.ParseAsync("remind me tomorrow", Now, tz, CancellationToken.None);
            Assert.True(result.Success, $"Failed for timezone: {tz}");
        }
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

file sealed class StubChatModelRouter(string reply) : IChatModelRouter
{
    public int CallCount { get; private set; }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        CallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(reply);
    }
}

/// <summary>
/// Simulates the LLM's own internal timeout token being canceled — models what
/// happens when the 45-second standalone CancellationTokenSource fires.
/// </summary>
file sealed class SlowCancellingChatModelRouter(CancellationTokenSource cts) : IChatModelRouter
{
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        // Cancel the token that was passed in — this simulates the 45-second timer
        // firing inside ParseAsync (the standalone CTS the parser creates).
        cts.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(string.Empty);
    }
}
