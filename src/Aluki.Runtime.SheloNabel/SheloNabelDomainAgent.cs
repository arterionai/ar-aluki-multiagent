using System.Globalization;
using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Capture.Media;
using Aluki.Runtime.Extraction.Providers;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Memory.Recall;
using Aluki.Runtime.Reminders;
using Aluki.Runtime.Reminders.Dispatch;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Domain agent (priority 40) that turns every WhatsApp message from the
/// authorized owner number into a Sheló NABEL sales assistant interaction.
/// Intercepts before Calendar (50), Reminder (60), and the catch-all
/// ConversationalResponseAgent (100).
///
/// Capabilities:
/// - Product knowledge: doses, combinations, contraindications.
/// - Customer profile-based recommendations via personal memory.
/// - Sales script generation (WhatsApp message or in-person pitch).
/// - Reminder + product recommendation when order reminder is detected.
/// - Sale recording: auto-creates a 30-day reorder follow-up reminder when
///   a completed sale is mentioned ("le vendí X a Y").
/// - Voice note transcription (Whisper) before processing.
/// - Responds in Spanish, English, or regional LATAM variants.
/// </summary>
public sealed class SheloNabelDomainAgent : IDomainAgent
{
    public const string Id = "shelonabel.sales_assistant";

    // wa_id of the authorized owner (no '+' prefix, as sent by Meta).
    private const string AuthorizedWaId = "14252307522";
    private const string DefaultTimezone = "America/Mexico_City";
    private const int ReorderDays = 30;

    private readonly ReminderService _reminderService;
    private readonly ReminderIntentParser _parser;
    private readonly IMemoryRecallService _recallService;
    private readonly IMemoryIngestionSink _ingestionSink;
    private readonly IChatModelRouter _chatRouter;
    private readonly IWhatsAppMessenger _messenger;
    private readonly IMetaMediaClient _mediaClient;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly IConversationHistoryStore _historyStore;
    private readonly IOutboundMessageStore _outboundStore;
    private readonly SheloNabelPromptBuilder _promptBuilder;
    private readonly ILogger<SheloNabelDomainAgent> _logger;

    public SheloNabelDomainAgent(
        ReminderService reminderService,
        ReminderIntentParser parser,
        IMemoryRecallService recallService,
        IMemoryIngestionSink ingestionSink,
        IChatModelRouter chatRouter,
        IWhatsAppMessenger messenger,
        IMetaMediaClient mediaClient,
        ITranscriptionProvider transcriptionProvider,
        IConversationHistoryStore historyStore,
        IOutboundMessageStore outboundStore,
        SheloNabelPromptBuilder promptBuilder,
        ILogger<SheloNabelDomainAgent> logger)
    {
        _reminderService = reminderService;
        _parser = parser;
        _recallService = recallService;
        _ingestionSink = ingestionSink;
        _chatRouter = chatRouter;
        _messenger = messenger;
        _mediaClient = mediaClient;
        _transcriptionProvider = transcriptionProvider;
        _historyStore = historyStore;
        _outboundStore = outboundStore;
        _promptBuilder = promptBuilder;
        _logger = logger;
    }

    public string AgentId => Id;
    public int Priority => 40;
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
        => message.ChannelType == ChannelType.WhatsApp
           && message.SenderExternalId == AuthorizedWaId
           && !string.IsNullOrWhiteSpace(message.PhoneNumberId);

    public async Task<AgentHandleResult> HandleAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        var phoneNumberId = message.PhoneNumberId!;
        var recipientWaId = message.SenderExternalId!;
        var correlationId = message.CorrelationId ?? message.MessageId;

        var scope = new PrincipalScope(
            TenantId: principal.TenantId,
            ContextId: principal.ContextId,
            UserId: principal.UserId,
            Roles: principal.Roles);

        // --- Audio: acknowledge → transcribe → continue as text ---
        var isAudio = message.MediaRefs.Count > 0
                      && message.MediaRefs.Any(r => r.MediaKind == "audio");
        if (isAudio)
        {
            await _messenger.SendTextMessageAsync(
                phoneNumberId, recipientWaId,
                "🎧 Escuchando tu audio...",
                ct);

            string? transcribed = null;
            try
            {
                var audioRef = message.MediaRefs.First(r => r.MediaKind == "audio");
                var content = await _mediaClient.DownloadAsync(audioRef.MediaId, ct);
                var encoding = ExtractAudioEncoding(audioRef.MimeType ?? content.ContentType);
                var result = await _transcriptionProvider.TranscribeAsync(
                    content.Bytes, encoding, "es", ct);
                transcribed = result.FullTranscription?.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SheloNabelDomainAgent: audio transcription failed. message_id={MessageId}",
                    message.MessageId);
            }

            if (string.IsNullOrWhiteSpace(transcribed))
            {
                await _messenger.SendTextMessageAsync(
                    phoneNumberId, recipientWaId,
                    "No pude entender el audio. ¿Me lo escribes? 🙏",
                    CancellationToken.None);
                return new AgentHandleResult(false, OutcomeCode: "audio_transcription_failed");
            }

            return await ProcessTextAsync(
                transcribed, message, principal, scope,
                phoneNumberId, recipientWaId, correlationId, ct);
        }

        // --- Text ---
        var text = message.Text;
        if (string.IsNullOrWhiteSpace(text))
            return new AgentHandleResult(true, OutcomeCode: "no_text_skipped");

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
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            // Load history + two parallel recalls: (1) by current message, (2) by customer profile
            // query so stored facts (skin type, age, purchase history) surface even when the
            // current message doesn't semantically resemble them ("recomiéndame algo").
            var historyTask = _historyStore.GetRecentAsync(
                principal.TenantId, principal.UserId, limit: 10, timeoutCts.Token);
            var recallTask = _recallService.RecallAsync(
                scope, text, correlationId, timeoutCts.Token);
            var profileRecallTask = _recallService.RecallAsync(
                scope,
                "perfil cliente nombre tipo piel edad historial compras productos preferencias",
                correlationId + "_profile",
                timeoutCts.Token);

            await Task.WhenAll(historyTask, recallTask, profileRecallTask);

            var history = historyTask.Result;
            var recallOutcome = recallTask.Result;
            var profileOutcome = profileRecallTask.Result;

            var recall = recallOutcome.Status != MemoryStatus.NoResult ? recallOutcome.Recall : null;
            var profileRecall = profileOutcome.Status != MemoryStatus.NoResult ? profileOutcome.Recall : null;
            var customerMemory = BuildCustomerMemoryText(recall, profileRecall);
            var systemPrompt = _promptBuilder.BuildSystemPrompt(customerMemory);

            // Best-effort ingest so new customer info mentioned is saved for future queries.
            _ = Task.Run(() => IngestAsync(text, message, principal, correlationId), CancellationToken.None);

            // --- Path 1: Reminder intent — create reminder + product recommendations ---
            if (ReminderSchedulingDetector.LooksLikeReminder(text))
            {
                return await HandleReminderWithRecommendationAsync(
                    text, principal, scope, systemPrompt, history,
                    phoneNumberId, recipientWaId, correlationId,
                    timeoutCts.Token);
            }

            // --- Path 2: Sale record — auto-create 30-day reorder reminder + next order suggestions ---
            if (SheloNabelSaleDetector.LooksLikeSaleRecord(text))
            {
                return await HandleSaleRecordAsync(
                    text, principal, scope, systemPrompt, history,
                    phoneNumberId, recipientWaId, correlationId,
                    timeoutCts.Token);
            }

            // --- Path 3: General product/customer query or script request ---
            var userPrompt = _promptBuilder.BuildQueryUserPrompt(text, recall, history);
            var response = await _chatRouter.CompleteAsync(systemPrompt, userPrompt, timeoutCts.Token);

            if (string.IsNullOrWhiteSpace(response))
                response = "Tuve un problema al procesar tu mensaje, inténtalo de nuevo 🙏";

            await SendResponseAsync(
                phoneNumberId, recipientWaId, response,
                principal, correlationId, ct);
            return new AgentHandleResult(true, OutcomeCode: "shelo_response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SheloNabelDomainAgent failed. message_id={MessageId} correlation_id={CorrelationId}",
                message.MessageId, correlationId);

            await _messenger.SendTextMessageAsync(
                phoneNumberId, recipientWaId,
                "Tuve un problema al procesar tu mensaje, inténtalo de nuevo 🙏",
                CancellationToken.None);

            return new AgentHandleResult(false, ErrorCode: "shelo_exception", ErrorMessage: ex.Message);
        }
    }

    // --- Reminder + product recommendation ---

    private async Task<AgentHandleResult> HandleReminderWithRecommendationAsync(
        string text,
        PrincipalContext principal,
        PrincipalScope scope,
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string phoneNumberId,
        string recipientWaId,
        string correlationId,
        CancellationToken ct)
    {
        var (timezone, _) = await ResolveTimezoneAsync(scope, correlationId, ct);
        var parsed = await _parser.ParseAsync(text, DateTimeOffset.UtcNow, timezone, ct);

        string reminderStatus;
        if (parsed.Success && parsed.ScheduledTimeUtc is not null)
        {
            var deliveryChannel = $"whatsapp:{phoneNumberId}:{recipientWaId}";
            var request = new CreateReminderRequest(
                ReminderId: null,
                CorrelationId: correlationId,
                PrincipalContext: new ReminderPrincipalContext(
                    TenantId: principal.TenantId,
                    ContextId: principal.ContextId,
                    UserId: principal.UserId),
                ReminderText: parsed.ReminderText,
                ScheduledTimeUtc: parsed.ScheduledTimeUtc,
                Timezone: timezone,
                ReminderType: ReminderType.OneShot,
                Recurrence: null,
                DeliveryChannel: deliveryChannel);

            var result = await _reminderService.CreateAsync(request, ct);
            reminderStatus = result.StatusCode is 200 or 201
                ? BuildReminderConfirmation(parsed.ReminderText!, parsed.ScheduledTimeUtc!.Value, timezone)
                : "⚠️ No pude crear el recordatorio (inténtalo de nuevo si es urgente).";
        }
        else
        {
            reminderStatus = "ℹ️ No detecté una hora específica — puedes ser más preciso (ej: «mañana a las 10am»).";
        }

        var userPrompt = _promptBuilder.BuildReminderUserPrompt(text, reminderStatus, history);
        var recommendation = await _chatRouter.CompleteAsync(systemPrompt, userPrompt, ct);

        if (string.IsNullOrWhiteSpace(recommendation))
            recommendation = reminderStatus;

        await SendResponseAsync(phoneNumberId, recipientWaId, recommendation, principal, correlationId, CancellationToken.None);
        return new AgentHandleResult(true, OutcomeCode: "shelo_reminder_with_recommendation");
    }

    // --- Sale record: auto-create 30-day reorder reminder + next-order suggestions ---

    private async Task<AgentHandleResult> HandleSaleRecordAsync(
        string text,
        PrincipalContext principal,
        PrincipalScope scope,
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string phoneNumberId,
        string recipientWaId,
        string correlationId,
        CancellationToken ct)
    {
        var (timezone, _) = await ResolveTimezoneAsync(scope, correlationId, ct);
        var reorderDue = DateTimeOffset.UtcNow.AddDays(ReorderDays);
        var deliveryChannel = $"whatsapp:{phoneNumberId}:{recipientWaId}";

        // Extract a short reminder label from the sale message via LLM, or fall back to a generic one.
        var reminderLabel = await ExtractSaleReminderLabelAsync(text, ct)
                            ?? "Contactar cliente para reorden";

        var request = new CreateReminderRequest(
            ReminderId: null,
            CorrelationId: correlationId + "_reorder",
            PrincipalContext: new ReminderPrincipalContext(
                TenantId: principal.TenantId,
                ContextId: principal.ContextId,
                UserId: principal.UserId),
            ReminderText: reminderLabel,
            ScheduledTimeUtc: reorderDue,
            Timezone: timezone,
            ReminderType: ReminderType.OneShot,
            Recurrence: null,
            DeliveryChannel: deliveryChannel);

        var result = await _reminderService.CreateAsync(request, ct);

        string reorderStatus;
        if (result.StatusCode is 200 or 201)
        {
            var localDue = ConvertToLocalTime(reorderDue, timezone);
            reorderStatus = $"✅ Venta registrada. 🔔 Recordatorio de reorden: «{reminderLabel}» — {localDue}";
        }
        else
        {
            reorderStatus = "✅ Venta registrada. (No pude crear el recordatorio de reorden automáticamente.)";
        }

        var userPrompt = _promptBuilder.BuildSaleUserPrompt(text, reorderStatus, history);
        var response = await _chatRouter.CompleteAsync(systemPrompt, userPrompt, ct);

        if (string.IsNullOrWhiteSpace(response))
            response = reorderStatus;

        await SendResponseAsync(phoneNumberId, recipientWaId, response, principal, correlationId, CancellationToken.None);
        return new AgentHandleResult(true, OutcomeCode: "shelo_sale_recorded");
    }

    // --- Helpers ---

    private async Task SendResponseAsync(
        string phoneNumberId,
        string recipientWaId,
        string body,
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
                "SheloNabelDomainAgent: failed to send WhatsApp response. correlation_id={CorrelationId}",
                correlationId);
        }

        // Persist outbound record so it appears in conversation history for the next message.
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
            _logger.LogWarning(ex,
                "SheloNabelDomainAgent: outbound persistence failed. correlation_id={CorrelationId}",
                correlationId);
        }
    }

    private async Task<string?> ExtractSaleReminderLabelAsync(string text, CancellationToken ct)
    {
        try
        {
            using var labelCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, labelCts.Token);

            const string systemPrompt =
                "Extract from the following sale message a short reminder label (max 10 words) "
                + "in the form 'Contactar a [NAME] para reorden de [PRODUCT]'. "
                + "Reply with ONLY the label text, nothing else.";

            var label = await _chatRouter.CompleteAsync(systemPrompt, text, linked.Token);
            return string.IsNullOrWhiteSpace(label) ? null : label.Trim().TrimEnd('.');
        }
        catch
        {
            return null;
        }
    }

    private async Task<(string Timezone, bool FromMemory)> ResolveTimezoneAsync(
        PrincipalScope scope,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            using var tzCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, tzCts.Token);

            var outcome = await _recallService.RecallAsync(
                scope, "¿En qué ciudad o zona horaria vive este usuario?",
                correlationId, linked.Token);

            if (outcome.Recall?.Claims is { Count: > 0 } claims)
            {
                var allText = string.Join(" ", claims.Select(c => c.Text));
                var tz = CityTimezoneMapper.ResolveFromText(allText);
                if (tz is not null)
                    return (tz, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "SheloNabelDomainAgent: timezone recall failed. correlation_id={CorrelationId}",
                correlationId);
        }

        return (DefaultTimezone, false);
    }

    private async Task IngestAsync(
        string text,
        UnifiedMessage message,
        PrincipalContext principal,
        string correlationId)
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
            _logger.LogWarning(ex,
                "SheloNabelDomainAgent: background memory ingestion failed. message_id={MessageId}",
                message.MessageId);
        }
    }

    private static string? BuildCustomerMemoryText(RecallResult? recall, RecallResult? profileRecall)
    {
        // Merge claims from both recalls, deduplicated by text.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();

        foreach (var source in new[] { profileRecall, recall })
        {
            if (source?.Claims is not { Count: > 0 } claims) continue;
            foreach (var claim in claims)
            {
                if (seen.Add(claim.Text))
                    lines.Add($"- {claim.Text}");
            }
        }

        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    private static string BuildReminderConfirmation(
        string reminderText, DateTimeOffset scheduledUtc, string timezone)
    {
        return $"✅ Recordatorio creado: «{reminderText}» — {ConvertToLocalTime(scheduledUtc, timezone)}";
    }

    private static string ConvertToLocalTime(DateTimeOffset utc, string timezone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var local = TimeZoneInfo.ConvertTime(utc, tz);
            return local.ToString(
                "HH:mm 'del' dddd d 'de' MMMM",
                CultureInfo.GetCultureInfo("es-MX"));
        }
        catch
        {
            return utc.ToString("HH:mm 'UTC'");
        }
    }

    private static string ExtractAudioEncoding(string? mimeType)
    {
        var mime = (mimeType ?? string.Empty).Split(';')[0].Trim();
        var slash = mime.LastIndexOf('/');
        return slash >= 0 ? mime[(slash + 1)..] : "wav";
    }
}
