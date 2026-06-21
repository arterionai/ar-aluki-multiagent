using System.Diagnostics;
using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Acknowledgment latency / telemetry checks for valid non-blocking events
/// (T028). Validates the synchronous fast path produces an accepted outcome well
/// within the P95 &lt;= 2s budget when persistence is non-blocking (SC-006/SC-007).
/// A full load baseline is captured separately in the quickstart evidence.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CaptureSlaTelemetryTests
{
    [Fact]
    public async Task Valid_event_acknowledges_within_latency_budget()
    {
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 0);
        var coordinator = CoordinatorTestHarness.Build(factory, principal);

        var stopwatch = Stopwatch.StartNew();
        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope(), CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(CaptureOutcomeKind.Accepted, outcome.Kind);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Ack latency {stopwatch.ElapsedMilliseconds}ms exceeded the 2s budget.");
    }

    [Fact]
    public async Task Accepted_outcome_carries_idempotency_key_and_canonical_id()
    {
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 0);
        var coordinator = CoordinatorTestHarness.Build(factory, principal);

        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope("wamid-sla"), CancellationToken.None);

        Assert.Equal(CaptureAuditEvent.Accepted, outcome.AuditEvent);
        Assert.False(string.IsNullOrWhiteSpace(outcome.IdempotencyKey));
        Assert.Contains("whatsapp|wamid-sla", outcome.IdempotencyKey);
        Assert.NotNull(outcome.CanonicalMessageId);
    }
}
