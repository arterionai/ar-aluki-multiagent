using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Domain agent (priority 57) that deletes saved notes on explicit command
/// ("borra lo de Fer", "olvida lo de la galería"). Soft delete only
/// (<c>deleted_at_utc</c>) — recoverable, and recall already reports a
/// deletion-caused evidence gap. The reply echoes exactly what was deleted so a
/// mistaken match can be re-saved immediately. No LLM on this path.
///
/// Evaluated after <see cref="PersonMemoryDomainAgent"/> (55, save) and before
/// <see cref="PersonLookupDomainAgent"/> (58) and <c>ReminderDomainAgent</c> (60).
/// </summary>
public sealed class NoteDeletionDomainAgent : IDomainAgent
{
    public const string Id = "memory.note_deletion";

    private const int MaxNoteLength = 150;

    private readonly INoteDeletionService _deletionService;
    private readonly IWhatsAppMessenger _messenger;
    private readonly IOutboundMessageStore _outboundStore;
    private readonly ILogger<NoteDeletionDomainAgent> _logger;

    public NoteDeletionDomainAgent(
        INoteDeletionService deletionService,
        IWhatsAppMessenger messenger,
        IOutboundMessageStore outboundStore,
        ILogger<NoteDeletionDomainAgent> logger)
    {
        _deletionService = deletionService;
        _messenger = messenger;
        _outboundStore = outboundStore;
        _logger = logger;
    }

    public string AgentId => Id;
    public int Priority => 57;
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
        => message.ChannelType == ChannelType.WhatsApp
           && !string.IsNullOrWhiteSpace(message.SenderExternalId)
           && !string.IsNullOrWhiteSpace(message.PhoneNumberId)
           && NoteDeletionDetector.TryExtractDeletion(message.Text, out _);

    public async Task<AgentHandleResult> HandleAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        if (!NoteDeletionDetector.TryExtractDeletion(message.Text, out var topic))
            return new AgentHandleResult(true, OutcomeCode: "no_deletion_skipped");

        var correlationId = message.CorrelationId ?? message.MessageId;
        var phoneNumberId = message.PhoneNumberId!;
        var recipientWaId = message.SenderExternalId!;
        var scope = new PrincipalScope(principal.TenantId, principal.ContextId, principal.UserId, principal.Roles);

        string body;
        string outcome;
        try
        {
            var deleted = await _deletionService.DeleteNotesAsync(scope, topic, correlationId, ct);
            if (deleted.Count == 0)
            {
                body = $"No encontré notas sobre *{topic}*.";
                outcome = "note_deletion_no_match";
            }
            else
            {
                var lines = deleted.Select(n =>
                {
                    var note = n.Trim();
                    return "• " + (note.Length <= MaxNoteLength ? note : note[..MaxNoteLength] + "…");
                });
                body = "Olvidado 🗑️\n" + string.Join("\n", lines);
                outcome = "note_deletion_done";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "NoteDeletionDomainAgent deletion failed. topic={Topic} correlation_id={CorrelationId}",
                topic, correlationId);
            body = "No pude borrar la nota ahora mismo, inténtalo de nuevo en un momento 🙏";
            outcome = "note_deletion_error";
        }

        // CancellationToken.None: reply must reach the user even if the webhook ct fired.
        try
        {
            await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, body, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "NoteDeletionDomainAgent reply failed. recipient={RecipientWaId} correlation_id={CorrelationId}",
                recipientWaId, correlationId);
        }

        try
        {
            await _outboundStore.TryPersistAsync(new OutboundMessage(
                Id: Guid.NewGuid(),
                TenantId: principal.TenantId,
                UserId: principal.UserId,
                CorrelationMessageId: correlationId,
                Channel: ChannelType.WhatsApp,
                RecipientWaId: recipientWaId,
                Body: body,
                Status: OutboundStatus.Delivered,
                ErrorReason: null,
                CreatedAt: DateTimeOffset.UtcNow,
                DeliveredAt: DateTimeOffset.UtcNow),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoteDeletionDomainAgent outbound persist failed.");
        }

        return new AgentHandleResult(true, OutcomeCode: outcome);
    }
}
