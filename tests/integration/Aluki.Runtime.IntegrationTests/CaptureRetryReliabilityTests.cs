using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Reliability tests for bounded retry and terminal failure semantics (T027):
/// max 5 attempts, eventual persistence on transient faults, terminal outcome on
/// exhaustion (FR-009, FR-017, SC-001, SC-009).
/// </summary>
[Trait("Category", "Integration")]
public sealed class CaptureRetryReliabilityTests
{
    [Fact]
    public async Task Eventually_persists_after_transient_failures()
    {
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 2);
        var coordinator = CoordinatorTestHarness.Build(factory, principal, maxAttempts: 5);

        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope(), CancellationToken.None);

        Assert.Equal(CaptureOutcomeKind.Accepted, outcome.Kind);
        Assert.Equal(3, outcome.AttemptCount); // failed twice, succeeded on third
        Assert.Equal(3, factory.PersistenceAttempts);
    }

    [Fact]
    public async Task Stops_after_max_attempts_with_terminal_failure()
    {
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 99);
        var coordinator = CoordinatorTestHarness.Build(factory, principal, maxAttempts: 5);

        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope(), CancellationToken.None);

        Assert.Equal(CaptureOutcomeKind.RetryExhausted, outcome.Kind);
        Assert.Equal(5, outcome.AttemptCount);
        Assert.Equal(5, factory.PersistenceAttempts); // capped at max attempts (SC-009)
        Assert.Equal(CaptureAuditEvent.FailedTerminal, outcome.AuditEvent);

        // A terminal failure audit was emitted via the audit scope.
        Assert.Contains(
            factory.AuditUnits,
            u => ((FakeAuditEventRepository)u.Audit).Inserted
                .Exists(e => e.EventName == CaptureAuditEvent.FailedTerminal));
    }

    [Fact]
    public async Task Suppresses_duplicate_without_extra_persistence()
    {
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 0, isNew: false);
        var coordinator = CoordinatorTestHarness.Build(factory, principal, maxAttempts: 5);

        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope(), CancellationToken.None);

        Assert.Equal(CaptureOutcomeKind.DuplicateSuppressed, outcome.Kind);
        Assert.Equal(CaptureAuditEvent.DuplicateSuppressed, outcome.AuditEvent);
    }
}
