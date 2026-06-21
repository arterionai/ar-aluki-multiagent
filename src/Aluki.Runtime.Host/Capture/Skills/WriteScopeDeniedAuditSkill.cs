using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Host.Observability;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Capture.Skills;

/// <summary>
/// Emits the immutable <c>capture.scope_denied</c> audit record for every denied
/// capture attempt (FR-006, FR-008, FR-016, SC-004, SC-005). When a tenant scope
/// is available the record is persisted under an audit-only session scope; when
/// even the tenant cannot be resolved, the denial is logged so it is never silent.
/// </summary>
public sealed class WriteScopeDeniedAuditSkill
{
    private readonly ICaptureUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<WriteScopeDeniedAuditSkill> _logger;

    public WriteScopeDeniedAuditSkill(
        ICaptureUnitOfWorkFactory unitOfWorkFactory,
        ILogger<WriteScopeDeniedAuditSkill> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _logger = logger;
    }

    public async Task WriteAsync(
        Guid? tenantId,
        Guid? userId,
        Guid? contextId,
        string sourceChannel,
        string correlationId,
        string? providerMessageId,
        string failureCategory,
        CancellationToken cancellationToken)
    {
        if (tenantId is null)
        {
            _logger.LogWarning(
                "capture.scope_denied could not be persisted (tenant unresolved). " +
                "correlation_id={CorrelationId} reason={FailureCategory}",
                correlationId,
                failureCategory);
            return;
        }

        try
        {
            await using var uow = await _unitOfWorkFactory.BeginAuditScopeAsync(
                tenantId.Value,
                userId,
                cancellationToken);

            await uow.Audit.InsertAsync(
                new CaptureAuditEventRow(
                    AuditId: Guid.NewGuid(),
                    TenantId: tenantId.Value,
                    ContextId: contextId,
                    UserId: userId,
                    SourceChannel: sourceChannel,
                    EventName: CaptureAuditEvent.ScopeDenied,
                    EventStatus: CaptureObservability.Status.Denied,
                    CorrelationId: correlationId,
                    ProviderMessageId: providerMessageId,
                    AttemptNumber: null,
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
                "Failed to persist capture.scope_denied audit. correlation_id={CorrelationId}",
                correlationId);
        }
    }
}
