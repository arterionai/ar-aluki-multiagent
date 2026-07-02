using System.Globalization;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Recall;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Reminders.Dispatch;

/// <summary>
/// Domain agent (priority 60) that intercepts WhatsApp reminder requests ("recuérdame…")
/// ahead of the catch-all ConversationalResponseAgent (priority 100) but after the
/// CalendarDomainAgent (priority 50). Uses LLM intent parsing to extract the reminder
/// content and time, then calls ReminderService.CreateAsync and confirms via WhatsApp.
/// The user's timezone is resolved from personal memory (city recall) so confirmation
/// messages display the correct local time.
/// </summary>
public sealed class ReminderDomainAgent : IDomainAgent
{
    public const string Id = "reminders.whatsapp_scheduler";
    private const string DefaultTimezone = "America/Mexico_City";

    private readonly ReminderService _reminderService;
    private readonly ReminderIntentParser _parser;
    private readonly MemoryRecallService _recallService;
    private readonly IWhatsAppMessenger _messenger;
    private readonly ILogger<ReminderDomainAgent> _logger;

    public ReminderDomainAgent(
        ReminderService reminderService,
        ReminderIntentParser parser,
        MemoryRecallService recallService,
        IWhatsAppMessenger messenger,
        ILogger<ReminderDomainAgent> logger)
    {
        _reminderService = reminderService;
        _parser = parser;
        _recallService = recallService;
        _messenger = messenger;
        _logger = logger;
    }

    public string AgentId => Id;
    public int Priority => 60;
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
        => message.ChannelType == ChannelType.WhatsApp
           && !string.IsNullOrWhiteSpace(message.SenderExternalId)
           && !string.IsNullOrWhiteSpace(message.PhoneNumberId)
           && ReminderSchedulingDetector.LooksLikeReminder(message.Text);

    public async Task<AgentHandleResult> HandleAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        var phoneNumberId = message.PhoneNumberId!;
        var recipientWaId = message.SenderExternalId!;
        var correlationId = message.CorrelationId ?? message.MessageId;
        var text = message.Text ?? string.Empty;

        // Declared outside the try block so it can be included in catch-block diagnostics.
        ReminderParseResult? parsed = null;

        try
        {
            // Resolve user's timezone from memory (best-effort — falls back to default).
            var (userTimezone, timezoneFromMemory) = await ResolveUserTimezoneAsync(principal, correlationId, ct);

            parsed = await _parser.ParseAsync(text, DateTimeOffset.UtcNow, userTimezone, ct);

            if (!parsed.Success || parsed.ScheduledTimeUtc is null)
            {
                const string clarify =
                    "No pude entender cuándo quieres que te recuerde. "
                    + "¿Puedes ser más específico? Por ejemplo: «recuérdame en 30 minutos revisar el correo» 🙏";
                await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, clarify, CancellationToken.None);
                return new AgentHandleResult(true, OutcomeCode: "reminder_clarification_needed");
            }

            var scheduledUtc = parsed.ScheduledTimeUtc.Value;
            if (!parsed.TimeExplicit && scheduledUtc <= DateTimeOffset.UtcNow)
            {
                // The assumed default hour already passed (e.g. "recuérdame hoy…" sent in
                // the evening → 09:00 local is behind us). Roll forward deterministically
                // instead of rejecting the reminder with "la fecha ya pasó".
                scheduledUtc = DateTimeOffset.UtcNow.AddHours(1);
            }

            // Encode routing in delivery_channel so WhatsAppReminderDeliveryChannel can address the message.
            var deliveryChannel = $"whatsapp:{phoneNumberId}:{recipientWaId}";

            var request = new CreateReminderRequest(
                ReminderId: null,
                CorrelationId: correlationId,
                PrincipalContext: new ReminderPrincipalContext(
                    TenantId: principal.TenantId,
                    ContextId: principal.ContextId,
                    UserId: principal.UserId),
                ReminderText: parsed.ReminderText,
                ScheduledTimeUtc: scheduledUtc,
                Timezone: userTimezone,
                ReminderType: ReminderType.OneShot,
                Recurrence: null,
                DeliveryChannel: deliveryChannel);

            var result = await _reminderService.CreateAsync(request, ct);

            string reply;
            if (result.StatusCode is 200 or 201)
            {
                reply = BuildConfirmation(parsed.ReminderText!, scheduledUtc, userTimezone, timeAssumed: !parsed.TimeExplicit);
                if (!timezoneFromMemory)
                    reply += "\n\n¿En qué ciudad vives? Así podré enviarte los próximos recordatorios a la hora local correcta. 🌎";
            }
            else if (result.StatusCode == 409)
            {
                reply = "Ya tienes demasiados recordatorios activos. Cancela alguno primero y vuelve a intentarlo. 📋";
            }
            else
            {
                _logger.LogWarning(
                    "ReminderDomainAgent: CreateAsync returned {StatusCode}. "
                    + "parsed_text={ParsedText} scheduled_utc={ScheduledUtc} now_utc={NowUtc} correlation_id={CorrelationId}",
                    result.StatusCode,
                    parsed.ReminderText,
                    scheduledUtc,
                    DateTimeOffset.UtcNow,
                    correlationId);

                // When the API rejected the scheduled time as past (likely the LLM returned
                // the wrong year), give the user a specific, actionable message.
                reply = result.StatusCode == 400 && scheduledUtc < DateTimeOffset.UtcNow
                    ? "La fecha que mencionaste ya pasó. ¿Puedes confirmar el año? "
                      + "Por ejemplo: «recuérdame el 1 de julio de 2026 a las 9am» 📅"
                    : "No pude crear el recordatorio en este momento. Inténtalo de nuevo. 🙏";
            }

            // CancellationToken.None: the webhook ct may already be cancelled by the time the
            // LLM + DB work completes; we must still deliver the reply to the user.
            await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, reply, CancellationToken.None);

            return new AgentHandleResult(
                result.StatusCode is 200 or 201,
                OutcomeCode: result.StatusCode is 200 or 201 ? "reminder_scheduled" : "reminder_failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ReminderDomainAgent failed. "
                + "message_id={MessageId} correlation_id={CorrelationId} "
                + "parsed_text={ParsedText} scheduled_utc={ScheduledUtc}",
                message.MessageId, correlationId,
                parsed?.ReminderText, parsed?.ScheduledTimeUtc);

            // Graceful degradation: CancellationToken.None so the fallback is never skipped
            // if a timeout on the LLM parse fired and cancelled ct.
            await _messenger.SendTextMessageAsync(
                phoneNumberId, recipientWaId,
                "No pude crear el recordatorio en este momento. Inténtalo de nuevo. 🙏",
                CancellationToken.None);

            return new AgentHandleResult(false, ErrorCode: "reminder_exception", ErrorMessage: ex.Message);
        }
    }

    private async Task<(string Timezone, bool FromMemory)> ResolveUserTimezoneAsync(
        PrincipalContext principal,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var scope = new PrincipalScope(
                TenantId: principal.TenantId,
                ContextId: principal.ContextId,
                UserId: principal.UserId,
                Roles: principal.Roles);

            var outcome = await _recallService.RecallAsync(
                scope,
                "¿En qué ciudad o zona horaria vive este usuario?",
                correlationId,
                timeoutCts.Token);

            if (outcome.Recall?.Claims is { Count: > 0 } claims)
            {
                var allText = string.Join(" ", claims.Select(c => c.Text));
                var tz = CityTimezoneMapper.ResolveFromText(allText);
                if (tz is not null)
                {
                    _logger.LogInformation(
                        "ReminderDomainAgent: resolved timezone {Timezone} from memory. correlation_id={CorrelationId}",
                        tz, correlationId);
                    return (tz, true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "ReminderDomainAgent: timezone recall failed, using default. correlation_id={CorrelationId}",
                correlationId);
        }

        return (DefaultTimezone, false);
    }

    private static string BuildConfirmation(string reminderText, DateTimeOffset scheduledUtc, string timezone, bool timeAssumed)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var local = TimeZoneInfo.ConvertTime(scheduledUtc, tz);
            var formatted = local.ToString("HH:mm 'del' dddd d 'de' MMMM",
                CultureInfo.GetCultureInfo("es-MX"));
            var reply = $"✅ ¡Listo! Te recuerdo «{reminderText}» a las {formatted}. 🔔";
            if (timeAssumed)
                reply += $"\n(Asumí las {local:HH:mm} porque no especificaste hora — si prefieres otra, dime y lo ajusto 🕘)";
            return reply;
        }
        catch
        {
            var reply = $"✅ ¡Listo! Te recuerdo «{reminderText}» a las {scheduledUtc:HH:mm} UTC. 🔔";
            if (timeAssumed)
                reply += "\n(Asumí esa hora porque no especificaste una — si prefieres otra, dime y lo ajusto 🕘)";
            return reply;
        }
    }
}
