using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Skills;
using System.Diagnostics;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Integration tests verifying that key synchronous calendar operations complete
/// within acceptable latency bounds (T038). These are pure in-process operations
/// (no I/O) and must complete well under their budget even on CI.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CalendarLatencyIntegrationTests
{
    private readonly CalendarRequestClassifierSkill _classifier = new();
    private readonly CalendarTimezoneResolverSkill _resolver = new();
    private readonly CalendarClarificationSkill _clarification = new();
    private readonly CalendarProviderSelectionSkill _selection = new();
    private readonly CalendarProviderParityPolicy _policy = new();

    // ── Classifier ─────────────────────────────────────────────────────────

    [Fact]
    public void Classifier_completes_within_50ms()
    {
        var sw = Stopwatch.StartNew();
        _classifier.Classify("Team standup tomorrow at 10am PST in Outlook");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50,
            $"Classifier took {sw.ElapsedMilliseconds}ms — expected < 50ms.");
    }

    [Fact]
    public void Classifier_repeated_100_times_stays_under_2000ms()
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
            _classifier.Classify($"Meeting number {i} tomorrow at {i % 12 + 1}pm PST");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"100 classifications took {sw.ElapsedMilliseconds}ms — expected < 2000ms.");
    }

    // ── Timezone resolver ──────────────────────────────────────────────────

    [Fact]
    public void Timezone_resolve_completes_within_20ms()
    {
        var sw = Stopwatch.StartNew();
        _resolver.Resolve("PST", null);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 20,
            $"Timezone resolve took {sw.ElapsedMilliseconds}ms — expected < 20ms.");
    }

    [Fact]
    public void DST_ambiguity_check_completes_within_50ms()
    {
        var sw = Stopwatch.StartNew();
        _resolver.IsDstAmbiguous("America/New_York", "2024-11-03T01:30:00");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50,
            $"DST ambiguity check took {sw.ElapsedMilliseconds}ms — expected < 50ms.");
    }

    [Fact]
    public void UTC_conversion_completes_within_20ms()
    {
        var sw = Stopwatch.StartNew();
        _resolver.ToUtc("America/Los_Angeles", "2024-06-15T14:00:00");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 20,
            $"UTC conversion took {sw.ElapsedMilliseconds}ms — expected < 20ms.");
    }

    // ── Clarification skill ────────────────────────────────────────────────

    [Fact]
    public void Clarification_evaluate_completes_within_20ms()
    {
        var classified = _classifier.Classify("Team sync 2024-06-15T14:00:00 PST");
        var timezone = _resolver.Resolve("PST", null);

        var sw = Stopwatch.StartNew();
        _clarification.Evaluate(classified, timezone);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 20,
            $"Clarification evaluate took {sw.ElapsedMilliseconds}ms — expected < 20ms.");
    }

    // ── Provider selection ─────────────────────────────────────────────────

    [Fact]
    public void Provider_selection_completes_within_5ms()
    {
        var connections = new[]
        {
            new CalendarConnectionRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                CalendarProvider.Outlook, ConnectionStatus.Connected, DateTimeOffset.UtcNow, null,
                "u@outlook.com", false, Guid.NewGuid().ToString()),
            new CalendarConnectionRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                CalendarProvider.Google, ConnectionStatus.Connected, DateTimeOffset.UtcNow, null,
                "u@gmail.com", true, Guid.NewGuid().ToString())
        };

        var sw = Stopwatch.StartNew();
        _selection.Select(null, connections);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5,
            $"Provider selection took {sw.ElapsedMilliseconds}ms — expected < 5ms.");
    }

    // ── Parity policy ──────────────────────────────────────────────────────

    [Fact]
    public void Parity_policy_validate_completes_within_5ms()
    {
        var result = new ProviderCreateResult(Success: true, ProviderEventRef: "evt-abc", ReconnectRequired: false, ErrorMessage: null);

        var sw = Stopwatch.StartNew();
        _policy.Validate(result);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5,
            $"Parity policy validate took {sw.ElapsedMilliseconds}ms — expected < 5ms.");
    }
}
