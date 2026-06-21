using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Orchestration;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills;
using Aluki.Runtime.Capture.Media;
using Aluki.Runtime.Capture.Retry;
using Aluki.Runtime.Capture.Skills;
using Aluki.Runtime.Capture.Observability;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Capture;

/// <summary>
/// Orchestrates the WhatsApp capture pipeline: principal resolution, consent/scope
/// guard, normalization, idempotent transactional persistence with bounded retry,
/// and mandatory lifecycle auditing/telemetry. Returns a single controlled
/// <see cref="CaptureOutcome"/> without leaking internal failures (T018).
/// </summary>
public sealed class WhatsAppCaptureCoordinator : IAgentCoordinator
{
    private static readonly string[] CaptureSkillSequence =
    [
        ScopeGuardSkill.SkillName,
        NormalizeWhatsAppInboundSkill.SkillName,
        IdempotencyGuardSkill.SkillName,
        PersistCaptureSkill.SkillName,
        WriteCaptureAuditSkill.SkillName
    ];

    private readonly IPrincipalContextResolver _principalResolver;
    private readonly ICaptureUnitOfWorkFactory _unitOfWorkFactory;
    private readonly CaptureRetryPolicy _retryPolicy;
    private readonly CaptureTelemetry _telemetry;
    private readonly ScopeGuardSkill _scopeGuard;
    private readonly NormalizeWhatsAppInboundSkill _normalize;
    private readonly IdempotencyGuardSkill _idempotencyGuard;
    private readonly PersistCaptureSkill _persistCapture;
    private readonly PersistUnsupportedCaptureSkill _persistUnsupported;
    private readonly WriteCaptureAuditSkill _writeCaptureAudit;
    private readonly WriteScopeDeniedAuditSkill _writeScopeDeniedAudit;
    private readonly WriteRetryAuditSkill _writeRetryAudit;
    private readonly IMediaDownloadQueue _mediaDownloadQueue;
    private readonly ILogger<WhatsAppCaptureCoordinator> _logger;

    public WhatsAppCaptureCoordinator(
        IPrincipalContextResolver principalResolver,
        ICaptureUnitOfWorkFactory unitOfWorkFactory,
        CaptureRetryPolicy retryPolicy,
        CaptureTelemetry telemetry,
        ScopeGuardSkill scopeGuard,
        NormalizeWhatsAppInboundSkill normalize,
        IdempotencyGuardSkill idempotencyGuard,
        PersistCaptureSkill persistCapture,
        PersistUnsupportedCaptureSkill persistUnsupported,
        WriteCaptureAuditSkill writeCaptureAudit,
        WriteScopeDeniedAuditSkill writeScopeDeniedAudit,
        WriteRetryAuditSkill writeRetryAudit,
        IMediaDownloadQueue mediaDownloadQueue,
        ILogger<WhatsAppCaptureCoordinator> logger)
    {
        _principalResolver = principalResolver;
        _unitOfWorkFactory = unitOfWorkFactory;
        _retryPolicy = retryPolicy;
        _telemetry = telemetry;
        _scopeGuard = scopeGuard;
        _normalize = normalize;
        _idempotencyGuard = idempotencyGuard;
        _persistCapture = persistCapture;
        _persistUnsupported = persistUnsupported;
        _writeCaptureAudit = writeCaptureAudit;
        _writeScopeDeniedAudit = writeScopeDeniedAudit;
        _writeRetryAudit = writeRetryAudit;
        _mediaDownloadQueue = mediaDownloadQueue;
        _logger = logger;
    }

    /// <summary>Declares the canonical capture skill plan (governance/observability).</summary>
    public Task<CoordinatorPlan> PlanAsync(
        PrincipalContext principal,
        IReadOnlyDictionary<string, object?> request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new CoordinatorPlan(CaptureSkillSequence, RequiresLongRunningWorkflow: false));

    public async Task<CaptureOutcome> CaptureAsync(
        WhatsAppInboundEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var correlationId = string.IsNullOrWhiteSpace(envelope.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : envelope.CorrelationId!;
        var providerMessageId = envelope.ProviderMessageId;

        // Stage 1: resolve and validate principal scope before any side effect.
        PrincipalResolution resolution;
        using (var stage = _telemetry.BeginStage(CaptureObservability.Stage.ScopeCheck, correlationId))
        {
            resolution = await _principalResolver.ResolveAsync(BuildIdentity(envelope, correlationId), cancellationToken);
            if (!resolution.Success)
            {
                stage.SetResult(CaptureObservability.Status.Denied, CaptureObservability.FailureCategory.ScopeDenied);
            }
        }

        if (!resolution.Success || resolution.Principal is null)
        {
            _telemetry.RecordOutcome(
                CaptureObservability.Stage.ScopeCheck,
                CaptureObservability.Status.Denied,
                CaptureObservability.FailureCategory.ScopeDenied);

            await _writeScopeDeniedAudit.WriteAsync(
                resolution.ResolvedTenantId,
                resolution.ResolvedUserId,
                contextId: null,
                sourceChannel: envelope.SourceChannel,
                correlationId: correlationId,
                providerMessageId: providerMessageId,
                failureCategory: CaptureObservability.FailureCategory.ScopeDenied,
                cancellationToken);

            return new CaptureOutcome(
                CaptureOutcomeKind.ScopeDenied,
                correlationId,
                AuditEvent: CaptureAuditEvent.ScopeDenied,
                ErrorCode: CaptureErrorCode.ScopeDenied,
                Message: resolution.DenialMessage ?? "Capture scope denied.");
        }

        var principal = resolution.Principal;
        var state = new CapturePipelineState(principal, envelope, correlationId);

        // Stage 2: consent-stop / scope guard before side effects.
        SkillResult guardResult;
        using (var stage = _telemetry.BeginStage(CaptureObservability.Stage.ScopeCheck, correlationId, principal.TenantId))
        {
            guardResult = await RunSkillAsync(_scopeGuard, state, cancellationToken);
            if (!guardResult.Success)
            {
                stage.SetResult(CaptureObservability.Status.Denied, CaptureObservability.FailureCategory.ConsentStop);
            }
        }

        if (!guardResult.Success)
        {
            await _writeScopeDeniedAudit.WriteAsync(
                principal.TenantId,
                principal.UserId,
                principal.ContextId,
                envelope.SourceChannel,
                correlationId,
                providerMessageId,
                CaptureObservability.FailureCategory.ConsentStop,
                cancellationToken);

            return new CaptureOutcome(
                CaptureOutcomeKind.ScopeDenied,
                correlationId,
                AuditEvent: CaptureAuditEvent.ScopeDenied,
                ErrorCode: CaptureErrorCode.ScopeDenied,
                Message: guardResult.ErrorMessage ?? "Capture scope denied.");
        }

        // Stage 3: normalize (classifies unsupported deterministically).
        using (_telemetry.BeginStage(CaptureObservability.Stage.Normalize, correlationId, principal.TenantId))
        {
            await RunSkillAsync(_normalize, state, cancellationToken);
        }

        // Stage 4: idempotent transactional persistence with bounded retry.
        return await PersistWithRetryAsync(state, providerMessageId, cancellationToken);
    }

    private async Task<CaptureOutcome> PersistWithRetryAsync(
        CapturePipelineState state,
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        var principal = state.Principal;

        for (var attempt = 1; attempt <= _retryPolicy.MaxAttempts; attempt++)
        {
            state.AttemptNumber = attempt;
            try
            {
                return await RunPersistenceAttemptAsync(state, cancellationToken);
            }
            catch (Exception ex) when (CaptureRetryPolicy.IsTransient(ex) && _retryPolicy.HasAttemptsRemaining(attempt))
            {
                _logger.LogWarning(
                    ex,
                    "Transient capture failure (attempt {Attempt}/{Max}). correlation_id={CorrelationId}",
                    attempt,
                    _retryPolicy.MaxAttempts,
                    state.CorrelationId);

                _telemetry.RecordRetry(attempt, CaptureObservability.FailureCategory.Transient);
                using (_telemetry.BeginStage(CaptureObservability.Stage.RetrySchedule, state.CorrelationId, principal.TenantId))
                {
                    await _writeRetryAudit.WriteRetryScheduledAsync(
                        principal, providerMessageId, attempt, CaptureObservability.FailureCategory.Transient, cancellationToken);
                }

                await Task.Delay(_retryPolicy.ComputeDelay(attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                var category = CaptureRetryPolicy.IsTransient(ex)
                    ? CaptureObservability.FailureCategory.Transient
                    : CaptureObservability.FailureCategory.Permanent;

                _logger.LogError(
                    ex,
                    "Terminal capture failure (attempt {Attempt}, category {Category}). correlation_id={CorrelationId}",
                    attempt,
                    category,
                    state.CorrelationId);

                return await TerminalFailureAsync(principal, providerMessageId, attempt, category, cancellationToken);
            }
        }

        // Retry budget exhausted without a successful attempt.
        return await TerminalFailureAsync(
            principal,
            providerMessageId,
            _retryPolicy.MaxAttempts,
            CaptureObservability.FailureCategory.Transient,
            cancellationToken);
    }

    private async Task<CaptureOutcome> RunPersistenceAttemptAsync(
        CapturePipelineState state,
        CancellationToken cancellationToken)
    {
        var principal = state.Principal;
        await using var uow = await _unitOfWorkFactory.BeginAsync(principal, cancellationToken);
        state.UnitOfWork = uow;

        using (_telemetry.BeginStage(CaptureObservability.Stage.Dedupe, state.CorrelationId, principal.TenantId))
        {
            await RunSkillAsync(_idempotencyGuard, state, cancellationToken);
        }

        if (state.IsDuplicate)
        {
            await RunSkillAsync(_writeCaptureAudit, state, cancellationToken);
            await uow.CommitAsync(cancellationToken);
            _telemetry.RecordOutcome(CaptureObservability.Stage.Dedupe, CaptureObservability.Status.Suppressed);

            return new CaptureOutcome(
                CaptureOutcomeKind.DuplicateSuppressed,
                state.CorrelationId,
                state.IdempotencyKey,
                state.CanonicalMessageId,
                CaptureAuditEvent.DuplicateSuppressed,
                AttemptCount: state.AttemptNumber);
        }

        using (_telemetry.BeginStage(CaptureObservability.Stage.Persist, state.CorrelationId, principal.TenantId))
        {
            if (state.IsUnsupported)
            {
                await RunSkillAsync(_persistUnsupported, state, cancellationToken);
            }
            else
            {
                await RunSkillAsync(_persistCapture, state, cancellationToken);
            }

            await RunSkillAsync(_writeCaptureAudit, state, cancellationToken);
        }

        await uow.CommitAsync(cancellationToken);
        _telemetry.RecordOutcome(CaptureObservability.Stage.Persist, CaptureObservability.Status.Success);

        // Queue async media binary download (best-effort; never fails the capture).
        if (state.PersistedMedia is { } pendingMedia)
        {
            try
            {
                await _mediaDownloadQueue.EnqueueAsync(
                    new MediaDownloadJob(
                        principal.TenantId,
                        principal.ContextId,
                        pendingMedia.MessageId,
                        pendingMedia.MediaId,
                        pendingMedia.ProviderMediaId,
                        pendingMedia.ContentType),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to enqueue media download. media_id={MediaId} correlation_id={CorrelationId}",
                    pendingMedia.MediaId,
                    state.CorrelationId);
            }
        }

        var kind = state.IsUnsupported ? CaptureOutcomeKind.AcceptedUnsupported : CaptureOutcomeKind.Accepted;
        var auditEvent = state.IsUnsupported ? CaptureAuditEvent.UnsupportedPayload : CaptureAuditEvent.Accepted;

        return new CaptureOutcome(
            kind,
            state.CorrelationId,
            state.IdempotencyKey,
            state.CanonicalMessageId,
            auditEvent,
            AttemptCount: state.AttemptNumber);
    }

    private async Task<CaptureOutcome> TerminalFailureAsync(
        PrincipalContext principal,
        string providerMessageId,
        int attempt,
        string failureCategory,
        CancellationToken cancellationToken)
    {
        using (_telemetry.BeginStage(CaptureObservability.Stage.TerminalFailure, principal.CorrelationId, principal.TenantId))
        {
            await _writeRetryAudit.WriteFailedTerminalAsync(
                principal, providerMessageId, attempt, failureCategory, cancellationToken);
        }

        _telemetry.RecordOutcome(
            CaptureObservability.Stage.TerminalFailure,
            CaptureObservability.Status.Failed,
            failureCategory);

        return new CaptureOutcome(
            CaptureOutcomeKind.RetryExhausted,
            principal.CorrelationId,
            AuditEvent: CaptureAuditEvent.FailedTerminal,
            ErrorCode: CaptureErrorCode.RetryExhausted,
            Message: "Capture failed after exhausting retries.",
            AttemptCount: attempt,
            FailureCategory: failureCategory);
    }

    private static ChannelIdentity BuildIdentity(WhatsAppInboundEnvelope envelope, string correlationId)
    {
        Guid? tenantHint = Guid.TryParse(envelope.ContextMetadata?.TenantHint, out var t) ? t : null;
        Guid? contextId = Guid.TryParse(envelope.ContextMetadata?.ContextId, out var c) ? c : null;

        return new ChannelIdentity(
            SourceChannel: envelope.SourceChannel,
            SenderExternalId: envelope.Sender.ExternalUserId,
            TenantHint: tenantHint,
            ContextId: contextId,
            CorrelationId: correlationId);
    }

    private static async Task<SkillResult> RunSkillAsync(
        ISkill skill,
        CapturePipelineState state,
        CancellationToken cancellationToken)
    {
        var input = new Dictionary<string, object?> { [CaptureInputKeys.State] = state };
        var context = new SkillExecutionContext(state.Principal, skill.Name, input, DateTimeOffset.UtcNow);
        return await skill.ExecuteAsync(context, cancellationToken);
    }
}
