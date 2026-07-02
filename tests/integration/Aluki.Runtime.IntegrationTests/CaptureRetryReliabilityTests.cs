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
        // Claim-first pipeline: the claim failed twice and succeeded on the third
        // begin; artifact persistence then ran on its own transaction (4th begin)
        // and succeeded on its first attempt.
        Assert.Equal(1, outcome.AttemptCount);
        Assert.Equal(4, factory.PersistenceAttempts);
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

    [Fact]
    public async Task Duplicate_delivery_never_dispatches_a_second_reply()
    {
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 0, isNew: false);
        var dispatcher = new RecordingMessageDispatcher();
        var coordinator = CoordinatorTestHarness.Build(factory, principal, dispatcher: dispatcher);

        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope(), CancellationToken.None);

        Assert.Equal(CaptureOutcomeKind.DuplicateSuppressed, outcome.Kind);
        Assert.Equal(0, dispatcher.DispatchCount);
    }

    [Fact]
    public async Task New_message_dispatches_exactly_once_and_persists_before_returning()
    {
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 0);
        var dispatcher = new RecordingMessageDispatcher();
        var coordinator = CoordinatorTestHarness.Build(factory, principal, dispatcher: dispatcher);

        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope(), CancellationToken.None);

        Assert.Equal(CaptureOutcomeKind.Accepted, outcome.Kind);
        Assert.Equal(1, dispatcher.DispatchCount);
        // Persist (with its capture audit) is joined before CaptureAsync returns.
        Assert.Equal(2, factory.PersistenceAttempts); // claim begin + persist begin
    }

    [Fact]
    public async Task Claim_exhaustion_never_dispatches()
    {
        // If the idempotency claim cannot commit, dispatch must not run — otherwise a
        // Meta redelivery could produce a second user-visible reply.
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 99);
        var dispatcher = new RecordingMessageDispatcher();
        var coordinator = CoordinatorTestHarness.Build(factory, principal, maxAttempts: 3, dispatcher: dispatcher);

        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope(), CancellationToken.None);

        Assert.Equal(CaptureOutcomeKind.RetryExhausted, outcome.Kind);
        Assert.Equal(0, dispatcher.DispatchCount);
    }

    [Fact]
    public async Task Persist_failure_after_claim_still_dispatched_and_reports_terminal()
    {
        // Claim succeeds (begin #1); artifact persistence fails on every subsequent
        // begin. The user-visible dispatch already happened; the outcome faithfully
        // reports the capture-side terminal failure for replay.
        var principal = CoordinatorTestHarness.Principal();
        var factory = new FakeCaptureUnitOfWorkFactory(failuresBeforeSuccess: 0, failFromCall: 2);
        var dispatcher = new RecordingMessageDispatcher();
        var coordinator = CoordinatorTestHarness.Build(factory, principal, maxAttempts: 3, dispatcher: dispatcher);

        var outcome = await coordinator.CaptureAsync(
            CoordinatorTestHarness.TextEnvelope(), CancellationToken.None);

        Assert.Equal(CaptureOutcomeKind.RetryExhausted, outcome.Kind);
        Assert.Equal(1, dispatcher.DispatchCount);
        Assert.Contains(
            factory.AuditUnits,
            u => ((FakeAuditEventRepository)u.Audit).Inserted
                .Exists(e => e.EventName == CaptureAuditEvent.FailedTerminal));
    }
}
