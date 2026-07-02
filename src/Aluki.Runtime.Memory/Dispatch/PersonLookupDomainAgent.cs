using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Domain agent (priority 58) that answers explicit person lookups
/// ("¿Quién es Fer?") with a contact-card reply listing the user's own saved
/// notes verbatim. No LLM on this path — deterministic formatting only.
///
/// Evaluated after <see cref="PersonMemoryDomainAgent"/> (55, save path) and
/// before <c>ReminderDomainAgent</c> (60) and the conversational fallback (100).
/// </summary>
public sealed class PersonLookupDomainAgent : IDomainAgent
{
    public const string Id = "memory.person_lookup";

    private const int MaxNoteLength = 150;

    private readonly IPersonLookupService _lookupService;
    private readonly IWhatsAppMessenger _messenger;
    private readonly IOutboundMessageStore _outboundStore;
    private readonly ILogger<PersonLookupDomainAgent> _logger;

    public PersonLookupDomainAgent(
        IPersonLookupService lookupService,
        IWhatsAppMessenger messenger,
        IOutboundMessageStore outboundStore,
        ILogger<PersonLookupDomainAgent> logger)
    {
        _lookupService = lookupService;
        _messenger = messenger;
        _outboundStore = outboundStore;
        _logger = logger;
    }

    public string AgentId => Id;
    public int Priority => 58;
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
        => message.ChannelType == ChannelType.WhatsApp
           && !string.IsNullOrWhiteSpace(message.SenderExternalId)
           && !string.IsNullOrWhiteSpace(message.PhoneNumberId)
           && PersonLookupDetector.TryExtractLookup(message.Text, out _);

    public async Task<AgentHandleResult> HandleAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        if (!PersonLookupDetector.TryExtractLookup(message.Text, out var personName))
            return new AgentHandleResult(true, OutcomeCode: "no_lookup_skipped");

        var correlationId = message.CorrelationId ?? message.MessageId;
        var phoneNumberId = message.PhoneNumberId!;
        var recipientWaId = message.SenderExternalId!;
        var scope = new PrincipalScope(principal.TenantId, principal.ContextId, principal.UserId, principal.Roles);

        string body;
        string outcome;
        try
        {
            var notes = await _lookupService.FindNotesAsync(scope, personName, correlationId, ct);
            if (notes.Count == 0)
            {
                body = $"No tengo notas sobre *{personName}*. Dime \"guarda que {personName}…\" y lo anoto 📒";
                outcome = "person_lookup_no_notes";
            }
            else
            {
                var lines = notes.Select(n => "• " + (n.Length <= MaxNoteLength ? n : n[..MaxNoteLength] + "…"));
                body = $"📇 *{personName}*\n" + string.Join("\n", lines);
                outcome = "person_lookup_answered";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PersonLookupDomainAgent search failed. person={PersonName} correlation_id={CorrelationId}",
                personName, correlationId);
            body = "No pude buscar en tus notas ahora mismo, inténtalo de nuevo en un momento 🙏";
            outcome = "person_lookup_error";
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
                "PersonLookupDomainAgent reply failed. recipient={RecipientWaId} correlation_id={CorrelationId}",
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
            _logger.LogWarning(ex, "PersonLookupDomainAgent outbound persist failed.");
        }

        return new AgentHandleResult(true, OutcomeCode: outcome);
    }
}
