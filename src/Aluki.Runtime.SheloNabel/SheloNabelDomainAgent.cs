using System.Globalization;
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
/// - Creates reminders for orders when intent is detected, then appends
///   product recommendations in the same response.
/// - Answers product/catalog questions using embedded catalog + LLM.
/// - Recalls and updates customer profiles via the personal memory store.
/// - Transcribes voice notes (Whisper) before processing.
/// </summary>
public sealed class SheloNabelDomainAgent : IDomainAgent
{
    public const string Id = "shelonabel.sales_assistant";

    // wa_id of the authorized owner (no '+' prefix, as sent by Meta).
    private const string AuthorizedWaId = "14252307522";
    private const string DefaultTimezone = "America/Mexico_City";

    private readonly ReminderService _reminderService;
    private readonly ReminderIntentParser _parser;
    private readonly IMemoryRecallService _recallService;
    private readonly IMemoryIngestionSink _ingestionSink;
    private readonly IChatModelRouter _chatRouter;
    private readonly IWhatsAppMessenger _messenger;
    private readonly IMetaMediaClient _mediaClient;
    private readonly ITranscriptionProvider _transcriptionProvider;
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
            // Recall customer profiles from memory in parallel with timezone resolution.
            var recallTask = _recallService.RecallAsync(scope, text, correlationId, timeoutCts.Token);

            // Best-effort ingest so new customer info mentioned is saved for future queries.
            _ = Task.Run(() => IngestAsync(text, message, principal, correlationId), CancellationToken.None);

            var recallOutcome = await recallTask;
            var recall = recallOutcome.Status != MemoryStatus.NoResult ? recallOutcome.Recall : null;
            var customerMemory = BuildCustomerMemoryText(recall);
            var systemPrompt = _promptBuilder.BuildSystemPrompt(customerMemory);

            // --- Reminder intent: create reminder then append product recommendations ---
            if (ReminderSchedulingDetector.LooksLikeReminder(text))
            {
                return await HandleReminderWithRecommendationAsync(
                    text, principal, scope, systemPrompt,
                    phoneNumberId, recipientWaId, correlationId,
                    timeoutCts.Token);
            }

            // --- General Shelo Nabel query / recommendation ---
            var userPrompt = _promptBuilder.BuildQueryUserPrompt(text, recall);
            var response = await _chatRouter.CompleteAsync(systemPrompt, userPrompt, timeoutCts.Token);

            if (string.IsNullOrWhiteSpace(response))
                response = "Tuve un problema al procesar tu mensaje, inténtalo de nuevo 🙏";

            await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, response, ct);
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

    private async Task<AgentHandleResult> HandleReminderWithRecommendationAsync(
        string text,
        PrincipalContext principal,
        PrincipalScope scope,
        string systemPrompt,
        string phoneNumberId,
        string recipientWaId,
        string correlationId,
        CancellationToken ct)
    {
        var (timezone, _) = await ResolveTimezoneAsync(scope, correlationId, ct);
        var parsed = await _parser.ParseAsync(text, DateTimeOffset.UtcNow, timezone, ct);

        string reminderConfirmation;
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

            reminderConfirmation = result.StatusCode is 200 or 201
                ? BuildReminderConfirmation(parsed.ReminderText!, parsed.ScheduledTimeUtc!.Value, timezone)
                : "⚠️ No pude crear el recordatorio automáticamente (inténtalo de nuevo si es urgente).";
        }
        else
        {
            reminderConfirmation = "ℹ️ No detecté una hora específica para el recordatorio — te recuerdo que puedes ser más específico (ej: «recuérdame mañana a las 10am armar el pedido»).";
        }

        var userPrompt = _promptBuilder.BuildReminderUserPrompt(text, reminderConfirmation);
        var recommendation = await _chatRouter.CompleteAsync(systemPrompt, userPrompt, ct);

        if (string.IsNullOrWhiteSpace(recommendation))
            recommendation = "Para el pedido, revisa el catálogo completo de la línea Baba de Caracol.";

        await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, recommendation, CancellationToken.None);
        return new AgentHandleResult(true, OutcomeCode: "shelo_reminder_with_recommendation");
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

    private static string? BuildCustomerMemoryText(RecallResult? recall)
    {
        if (recall?.Claims is not { Count: > 0 } claims) return null;
        return string.Join("\n", claims.Select(c => $"- {c.Text}"));
    }

    private static string BuildReminderConfirmation(
        string reminderText, DateTimeOffset scheduledUtc, string timezone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var local = TimeZoneInfo.ConvertTime(scheduledUtc, tz);
            var formatted = local.ToString(
                "HH:mm 'del' dddd d 'de' MMMM",
                CultureInfo.GetCultureInfo("es-MX"));
            return $"✅ Recordatorio creado: «{reminderText}» — {formatted}";
        }
        catch
        {
            return $"✅ Recordatorio creado: «{reminderText}» — {scheduledUtc:HH:mm} UTC";
        }
    }

    private static string ExtractAudioEncoding(string? mimeType)
    {
        var mime = (mimeType ?? string.Empty).Split(';')[0].Trim();
        var slash = mime.LastIndexOf('/');
        return slash >= 0 ? mime[(slash + 1)..] : "wav";
    }
}
