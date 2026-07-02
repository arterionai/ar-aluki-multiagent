using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Domain agent (priority 55) that intercepts "person note" intent — messages like
/// "Recuérdame que Fer Amaro la conocí en galería" that express a fact about a person
/// without a future delivery time. Saves the note to personal memory via
/// <see cref="IMemoryIngestionSink"/> and confirms with a short reply.
///
/// Evaluated BEFORE <c>ReminderDomainAgent</c> (priority 60) so that
/// "recuérdame que [person] …" without a temporal expression is never treated as a
/// time-based reminder.
///
/// Recall ("¿Quién es Fer?") is served by the existing
/// <c>ConversationalResponseAgent</c> + <c>MemoryRecallService</c> pipeline.
/// </summary>
public sealed class PersonMemoryDomainAgent : IDomainAgent
{
    public const string Id = "memory.person_note";

    private readonly IMemoryIngestionSink _sink;
    private readonly IWhatsAppMessenger _messenger;
    private readonly IOutboundMessageStore _outboundStore;
    private readonly ILogger<PersonMemoryDomainAgent> _logger;

    public PersonMemoryDomainAgent(
        IMemoryIngestionSink sink,
        IWhatsAppMessenger messenger,
        IOutboundMessageStore outboundStore,
        ILogger<PersonMemoryDomainAgent> logger)
    {
        _sink = sink;
        _messenger = messenger;
        _outboundStore = outboundStore;
        _logger = logger;
    }

    public string AgentId => Id;
    public int Priority => 55;
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
        => message.ChannelType == ChannelType.WhatsApp
           && !string.IsNullOrWhiteSpace(message.SenderExternalId)
           && !string.IsNullOrWhiteSpace(message.PhoneNumberId)
           && PersonNoteDetector.LooksLikePersonNote(message.Text);

    public async Task<AgentHandleResult> HandleAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        var text = message.Text;
        if (string.IsNullOrWhiteSpace(text))
            return new AgentHandleResult(true, OutcomeCode: "no_text_skipped");

        var correlationId = message.CorrelationId ?? message.MessageId;
        var phoneNumberId = message.PhoneNumberId!;
        var recipientWaId = message.SenderExternalId!;

        // Persist to personal memory (best-effort — must never throw into caller).
        try
        {
            await _sink.IngestAsync(new MemoryIngestionItem(
                TenantId: principal.TenantId,
                ContextId: principal.ContextId,
                UserId: principal.UserId,
                SourceChannel: message.ChannelType,
                SourceIdentity: message.MessageId,
                ContentText: text,
                ProvenanceRef: $"{message.ChannelType}:{message.MessageId}",
                CorrelationId: correlationId),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PersonMemoryDomainAgent ingestion failed. message_id={MessageId}",
                message.MessageId);
        }

        // Confirmation reply (CancellationToken.None: reply must reach the user even
        // if the webhook CancellationToken has already fired).
        var preview = text.Length <= 200 ? text : text[..200] + "…";
        var body = $"¡Anotado! 📒 {preview}";

        try
        {
            await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, body, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PersonMemoryDomainAgent reply failed. recipient={RecipientWaId} correlation_id={CorrelationId}",
                recipientWaId, correlationId);
        }

        // Persist outbound record for conversation history (best-effort).
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
            _logger.LogWarning(ex, "PersonMemoryDomainAgent outbound persist failed.");
        }

        return new AgentHandleResult(true, OutcomeCode: "person_note_saved");
    }
}
