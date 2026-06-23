using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Capture.Observability;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aluki.Runtime.Capture.Skills;

/// <summary>
/// Emits the immutable <c>capture.scope_denied</c> audit record for every denied
/// capture attempt (FR-006, FR-008, FR-016, SC-004, SC-005). When a tenant scope
/// is available the record is persisted under an audit-only session scope; when
/// even the tenant cannot be resolved, the sender and denial reason are written to
/// <c>capture_unresolved_denial</c> (no RLS, no tenant FK) for forensic backfill.
/// </summary>
public sealed class WriteScopeDeniedAuditSkill
{
    private readonly ICaptureUnitOfWorkFactory _unitOfWorkFactory;
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly ILogger<WriteScopeDeniedAuditSkill> _logger;

    public WriteScopeDeniedAuditSkill(
        ICaptureUnitOfWorkFactory unitOfWorkFactory,
        NpgsqlConnectionFactory connectionFactory,
        ILogger<WriteScopeDeniedAuditSkill> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _connectionFactory = connectionFactory;
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
        CancellationToken cancellationToken,
        string? senderExternalId = null)
    {
        if (tenantId is null)
        {
            _logger.LogWarning(
                "capture.scope_denied (tenant unresolved): sender={Sender} correlation_id={CorrelationId} reason={FailureCategory}",
                senderExternalId ?? "(unknown)",
                correlationId,
                failureCategory);

            await WriteUnresolvedDenialAsync(senderExternalId, sourceChannel, correlationId, failureCategory, cancellationToken);
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
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    SenderExternalId: senderExternalId),
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

    private async Task WriteUnresolvedDenialAsync(
        string? senderExternalId,
        string sourceChannel,
        string correlationId,
        string failureCategory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(senderExternalId))
            return;

        try
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                """
                insert into capture_unresolved_denial (sender_external_id, source_channel, correlation_id, failure_reason)
                values (@sender, @channel, @correlation, @reason)
                """,
                connection);
            cmd.Parameters.AddWithValue("sender", senderExternalId);
            cmd.Parameters.AddWithValue("channel", sourceChannel);
            cmd.Parameters.AddWithValue("correlation", correlationId);
            cmd.Parameters.AddWithValue("reason", failureCategory);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist capture_unresolved_denial for sender={Sender}",
                senderExternalId);
        }
    }
}
