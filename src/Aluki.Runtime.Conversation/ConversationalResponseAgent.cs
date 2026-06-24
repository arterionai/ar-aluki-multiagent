using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Feedback;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Memory.Recall;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Conversation;

/// <summary>
/// Domain agent (priority 100) that responds to every WhatsApp message using
/// semantic memory recall + recent conversation history. Also ingests text messages
/// into personal memory (best-effort) alongside generating the response.
/// </summary>
public sealed class ConversationalResponseAgent : IDomainAgent
{
    public const string Id = "conversation.whatsapp_response";

    private readonly IMemoryIngestionSink _ingestionSink;
    private readonly IFeedbackCaptureSink _feedbackSink;
    private readonly MemoryRecallService _recallService;
    private readonly IChatModelRouter _chatRouter;
    private readonly IWhatsAppMessenger _messenger;
    private readonly IConversationHistoryStore _historyStore;
    private readonly IOutboundMessageStore _outboundStore;
    private readonly ConversationPromptBuilder _promptBuilder;
    private readonly ConversationOptions _options;
    private readonly ILogger<ConversationalResponseAgent> _logger;

    public ConversationalResponseAgent(
        IMemoryIngestionSink ingestionSink,
        IFeedbackCaptureSink feedbackSink,
        MemoryRecallService recallService,
        IChatModelRouter chatRouter,
        IWhatsAppMessenger messenger,
        IConversationHistoryStore historyStore,
        IOutboundMessageStore outboundStore,
        ConversationPromptBuilder promptBuilder,
        IOptions<ConversationOptions> options,
        ILogger<ConversationalResponseAgent> logger)
    {
        _ingestionSink = ingestionSink;
        _feedbackSink = feedbackSink;
        _recallService = recallService;
        _chatRouter = chatRouter;
        _messenger = messenger;
        _historyStore = historyStore;
        _outboundStore = outboundStore;
        _promptBuilder = promptBuilder;
        _options = options.Value;
        _logger = logger;
    }

    public string AgentId => Id;
    public int Priority => 100;
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Claims any message from a known WhatsApp sender with routing info available.
    /// </summary>
    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
        => message.ChannelType == ChannelType.WhatsApp
           && !string.IsNullOrWhiteSpace(message.SenderExternalId)
           && !string.IsNullOrWhiteSpace(message.PhoneNumberId);

    public async Task<AgentHandleResult> HandleAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        var phoneNumberId = message.PhoneNumberId!;
        var recipientWaId = message.SenderExternalId!;
        var correlationId = message.CorrelationId ?? message.MessageId;

        // Build the principal scope for memory operations.
        var scope = new PrincipalScope(
            TenantId: principal.TenantId,
            ContextId: principal.ContextId,
            UserId: principal.UserId,
            Roles: principal.Roles);

        // --- Audio messages: acknowledge and ingest, no LLM call ---
        var isAudio = message.MediaRefs.Count > 0
                      && message.MediaRefs.Any(r => r.MediaKind == "audio");
        if (isAudio)
        {
            await SendResponseAsync(
                phoneNumberId, recipientWaId,
                _options.AudioAcknowledgmentMessage,
                OutboundStatus.Delivered,
                errorReason: null,
                principal, correlationId, ct);

            return new AgentHandleResult(true, OutcomeCode: "audio_acknowledged");
        }

        // --- Image messages: acknowledge, no LLM call ---
        var isImage = message.MediaRefs.Count > 0
                      && message.MediaRefs.Any(r => r.MediaKind == "image");
        if (isImage)
        {
            await SendResponseAsync(
                phoneNumberId, recipientWaId,
                _options.ImageAcknowledgmentMessage,
                OutboundStatus.Delivered,
                errorReason: null,
                principal, correlationId, ct);

            return new AgentHandleResult(true, OutcomeCode: "image_acknowledged");
        }

        // --- Text messages ---
        var text = message.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AgentHandleResult(true, OutcomeCode: "no_text_skipped");
        }

        // Short-circuit: security/privacy questions get a fixed response — no LLM call.
        if (SecurityPrivacyDetector.LooksLikeSecurityQuestion(text))
        {
            await SendResponseAsync(
                phoneNumberId, recipientWaId,
                _options.SecurityPrivacyResponse.Trim(), OutboundStatus.Delivered,
                errorReason: null,
                principal, correlationId, ct);
            return new AgentHandleResult(true, OutcomeCode: "security_privacy_response");
        }

        // 1. Memory ingestion + feedback capture (best-effort, non-blocking — fire and forget).
        _ = Task.Run(async () =>
        {
            try
            {
                await _ingestionSink.IngestAsync(new MemoryIngestionItem(
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
                _logger.LogWarning(ex, "Background memory ingestion failed. message_id={MessageId}", message.MessageId);
            }

            await _feedbackSink.TryCaptureAsync(
                principal.TenantId, principal.UserId,
                message.MessageId, text,
                CancellationToken.None);
        }, CancellationToken.None);

        // 2. Fetch history and recall in parallel.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.LlmTimeoutSeconds));

        try
        {
            var historyTask = _historyStore.GetRecentAsync(
                principal.TenantId, principal.UserId, _options.HistoryWindowSize, timeoutCts.Token);

            var recallTask = _recallService.RecallAsync(
                scope, text, correlationId, timeoutCts.Token);

            await Task.WhenAll(historyTask, recallTask);

            var history = historyTask.Result;
            var recallOutcome = recallTask.Result;
            var recall = recallOutcome.Status != MemoryStatus.NoResult ? recallOutcome.Recall : null;

            // 3. Build prompt and call LLM.
            var isFirstMessage = history.Count == 0;
            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                isFirstMessage ? _options.OnboardingInstruction : null);
            var userPrompt = _promptBuilder.BuildUserPrompt(text, history, recall);

            var responseText = await _chatRouter.CompleteAsync(systemPrompt, userPrompt, timeoutCts.Token);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                responseText = _options.ErrorFallbackMessage;
            }

            // Append suffix when memory has no relevant data.
            if (recallOutcome.Status == MemoryStatus.NoResult
                && !isFirstMessage
                && !responseText.Contains(_options.NoMemoryMessageSuffix, StringComparison.OrdinalIgnoreCase))
            {
                responseText += _options.NoMemoryMessageSuffix;
            }

            // 4. Send response.
            await SendResponseAsync(
                phoneNumberId, recipientWaId,
                responseText, OutboundStatus.Delivered,
                errorReason: null,
                principal, correlationId, ct);

            return new AgentHandleResult(true, OutcomeCode: "responded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ConversationalResponseAgent failed. message_id={MessageId} correlation_id={CorrelationId}",
                message.MessageId, correlationId);

            // Graceful degradation: use CancellationToken.None so the fallback send
            // is never skipped even when the LLM timeout fired and cancelled timeoutCts.
            await SendResponseAsync(
                phoneNumberId, recipientWaId,
                _options.ErrorFallbackMessage, OutboundStatus.ErrorFallback,
                errorReason: ex.Message,
                principal, correlationId, CancellationToken.None);

            return new AgentHandleResult(false, ErrorCode: "response_failed", ErrorMessage: ex.Message);
        }
    }

    private async Task SendResponseAsync(
        string phoneNumberId,
        string recipientWaId,
        string body,
        string status,
        string? errorReason,
        PrincipalContext principal,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send WhatsApp response. recipient={RecipientWaId} correlation_id={CorrelationId}",
                recipientWaId, correlationId);
            status = OutboundStatus.ErrorFallback;
            errorReason = ex.Message;
        }

        // Persist outbound message record (best-effort).
        try
        {
            var outbound = new OutboundMessage(
                Id: Guid.NewGuid(),
                TenantId: principal.TenantId,
                UserId: principal.UserId,
                CorrelationMessageId: correlationId,
                Channel: ChannelType.WhatsApp,
                RecipientWaId: recipientWaId,
                Body: body,
                Status: status,
                ErrorReason: errorReason,
                CreatedAt: DateTimeOffset.UtcNow,
                DeliveredAt: status == OutboundStatus.Delivered ? DateTimeOffset.UtcNow : null);

            await _outboundStore.TryPersistAsync(outbound, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Outbound message persistence failed. correlation_id={CorrelationId}",
                correlationId);
        }
    }
}
