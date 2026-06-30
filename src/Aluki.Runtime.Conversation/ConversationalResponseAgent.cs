using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Feedback;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Capture.Media;
using Aluki.Runtime.Extraction.Providers;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Memory.Recall;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IMemoryRecallService _recallService;
    private readonly IChatModelRouter _chatRouter;
    private readonly IWhatsAppMessenger _messenger;
    private readonly IConversationHistoryStore _historyStore;
    private readonly IOutboundMessageStore _outboundStore;
    private readonly ConversationPromptBuilder _promptBuilder;
    private readonly IMetaMediaClient _mediaClient;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConversationOptions _options;
    private readonly ILogger<ConversationalResponseAgent> _logger;

    public ConversationalResponseAgent(
        IMemoryIngestionSink ingestionSink,
        IFeedbackCaptureSink feedbackSink,
        IMemoryRecallService recallService,
        IChatModelRouter chatRouter,
        IWhatsAppMessenger messenger,
        IConversationHistoryStore historyStore,
        IOutboundMessageStore outboundStore,
        ConversationPromptBuilder promptBuilder,
        IMetaMediaClient mediaClient,
        ITranscriptionProvider transcriptionProvider,
        IServiceScopeFactory scopeFactory,
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
        _mediaClient = mediaClient;
        _transcriptionProvider = transcriptionProvider;
        _scopeFactory = scopeFactory;
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

        // --- Audio messages: acknowledge, transcribe via Whisper, then respond ---
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

            var audioRef = message.MediaRefs.First(r => r.MediaKind == "audio");
            string? transcribedText = null;
            try
            {
                var content = await _mediaClient.DownloadAsync(audioRef.MediaId, ct);
                var encoding = ExtractAudioEncoding(audioRef.MimeType ?? content.ContentType);
                var transcription = await _transcriptionProvider.TranscribeAsync(
                    content.Bytes, encoding, "es", ct);
                transcribedText = transcription.FullTranscription?.Trim();
                _logger.LogInformation(
                    "Audio transcribed. message_id={MessageId} chars={Chars}",
                    message.MessageId, transcribedText?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audio transcription failed. message_id={MessageId}", message.MessageId);
            }

            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                await SendResponseAsync(
                    phoneNumberId, recipientWaId,
                    "No pude entender el audio. ¿Podrías repetirlo o escribirme? 🙏",
                    OutboundStatus.Delivered,
                    errorReason: null,
                    principal, correlationId, ct);
                return new AgentHandleResult(false, OutcomeCode: "audio_transcription_failed");
            }

            // Re-dispatch the transcribed text as a synthetic text-only message so that
            // domain agents with higher priority (CalendarDomainAgent, ReminderDomainAgent, etc.)
            // get a chance to act on the content before falling back to conversational LLM.
            // IMessageDispatcher is resolved lazily to avoid a circular singleton dependency.
            var syntheticMessage = message with
            {
                Text = transcribedText,
                MediaRefs = Array.Empty<UnifiedMediaRef>()
            };

            using var dispatchScope = _scopeFactory.CreateScope();
            var dispatcher = dispatchScope.ServiceProvider.GetRequiredService<IMessageDispatcher>();
            var dispatchResult = await dispatcher.DispatchAsync(syntheticMessage, principal, ct);

            return new AgentHandleResult(
                dispatchResult.Outcome == DispatchOutcome.Dispatched,
                OutcomeCode: $"audio_redispatched:{dispatchResult.Outcome}:{dispatchResult.SelectedAgentId}");
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

        return await ProcessTextAsync(
            text, message, principal, scope,
            phoneNumberId, recipientWaId, correlationId, ct);
    }

    private async Task<AgentHandleResult> ProcessTextAsync(
        string text,
        UnifiedMessage message,
        PrincipalContext principal,
        PrincipalScope scope,
        string phoneNumberId,
        string recipientWaId,
        string correlationId,
        CancellationToken ct)
    {
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

            // 3a. Link save intent: bypass LLM, confirm save (memory ingestion already ran above).
            if (LinkCanonicalization.IsLinkSaveIntent(text))
            {
                var url = LinkCanonicalization.ExtractFirstUrl(text)!;
                var label = LinkCanonicalization.ExtractLabelText(text, url)?.Trim();
                var saveReply = string.IsNullOrWhiteSpace(label)
                    ? $"Guardado 🔗\n{url}"
                    : $"Guardado 🔗 *{label}*\n{url}";
                await SendResponseAsync(
                    phoneNumberId, recipientWaId,
                    saveReply, OutboundStatus.Delivered,
                    errorReason: null,
                    principal, correlationId, ct);
                return new AgentHandleResult(true, OutcomeCode: "link_saved");
            }

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
            // CancellationToken.None: reply must reach the user even if the webhook ct fired.
            await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, body, CancellationToken.None);
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

    // "audio/ogg; codecs=opus" → "ogg", "audio/mp4" → "mp4"
    private static string ExtractAudioEncoding(string? mimeType)
    {
        var mime = (mimeType ?? string.Empty).Split(';')[0].Trim();
        var slash = mime.LastIndexOf('/');
        return slash >= 0 ? mime[(slash + 1)..] : "wav";
    }
}
