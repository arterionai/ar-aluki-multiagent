using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Observability;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Capture.Skills;

/// <summary>
/// Emits <c>capture.retry_scheduled</c> and <c>capture.failed_terminal</c> audit
/// events with attempt number and failure category (FR-008, FR-016, FR-017,
/// SC-005, SC-009). Written in a dedicated transaction so the record survives the
/// rollback of the failed persistence attempt.
/// </summary>
public sealed class WriteRetryAuditSkill
{
    private readonly ICaptureUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<WriteRetryAuditSkill> _logger;

    public WriteRetryAuditSkill(
        ICaptureUnitOfWorkFactory unitOfWorkFactory,
        ILogger<WriteRetryAuditSkill> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _logger = logger;
    }

    public Task WriteRetryScheduledAsync(
        PrincipalContext principal,
        string? providerMessageId,
        int attemptNumber,
        string failureCategory,
        CancellationToken cancellationToken) =>
        WriteAsync(
            principal,
            CaptureAuditEvent.RetryScheduled,
            CaptureObservability.Status.Scheduled,
            providerMessageId,
            attemptNumber,
            failureCategory,
            cancellationToken);

    public Task WriteFailedTerminalAsync(
        PrincipalContext principal,
        string? providerMessageId,
        int attemptNumber,
        string failureCategory,
        CancellationToken cancellationToken) =>
        WriteAsync(
            principal,
            CaptureAuditEvent.FailedTerminal,
            CaptureObservability.Status.Failed,
            providerMessageId,
            attemptNumber,
            failureCategory,
            cancellationToken);

    private async Task WriteAsync(
        PrincipalContext principal,
        string eventName,
        string status,
        string? providerMessageId,
        int attemptNumber,
        string failureCategory,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var uow = await _unitOfWorkFactory.BeginAuditScopeAsync(
                principal.TenantId,
                principal.UserId,
                cancellationToken);

            await uow.Audit.InsertAsync(
                new CaptureAuditEventRow(
                    AuditId: Guid.NewGuid(),
                    TenantId: principal.TenantId,
                    ContextId: principal.ContextId,
                    UserId: principal.UserId,
                    SourceChannel: principal.SourceChannel,
                    EventName: eventName,
                    EventStatus: status,
                    CorrelationId: principal.CorrelationId,
                    ProviderMessageId: providerMessageId,
                    AttemptNumber: attemptNumber,
                    FailureCategory: failureCategory,
                    PayloadRef: null,
                    OccurredAtUtc: DateTimeOffset.UtcNow),
                cancellationToken);

            await uow.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist {EventName} audit. correlation_id={CorrelationId}",
                eventName,
                principal.CorrelationId);
        }
    }
}
