using System.Globalization;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Reminders.Dispatch;

/// <summary>
/// Domain agent (priority 60) that intercepts WhatsApp reminder requests ("recuérdame…")
/// ahead of the catch-all ConversationalResponseAgent (priority 100) but after the
/// CalendarDomainAgent (priority 50). Uses LLM intent parsing to extract the reminder
/// content and time, then calls ReminderService.CreateAsync and confirms via WhatsApp.
/// </summary>
public sealed class ReminderDomainAgent : IDomainAgent
{
    public const string Id = "reminders.whatsapp_scheduler";

    private readonly ReminderService _reminderService;
    private readonly ReminderIntentParser _parser;
    private readonly IWhatsAppMessenger _messenger;
    private readonly ILogger<ReminderDomainAgent> _logger;

    public ReminderDomainAgent(
        ReminderService reminderService,
        ReminderIntentParser parser,
        IWhatsAppMessenger messenger,
        ILogger<ReminderDomainAgent> logger)
    {
        _reminderService = reminderService;
        _parser = parser;
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

        var parsed = await _parser.ParseAsync(text, DateTimeOffset.UtcNow, ct);

        if (!parsed.Success || parsed.ScheduledTimeUtc is null)
        {
            const string clarify =
                "No pude entender cuándo quieres que te recuerde. "
                + "¿Puedes ser más específico? Por ejemplo: «recuérdame en 30 minutos revisar el correo» 🙏";
            await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, clarify, ct);
            return new AgentHandleResult(true, OutcomeCode: "reminder_clarification_needed");
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
            ScheduledTimeUtc: parsed.ScheduledTimeUtc,
            Timezone: "America/Mexico_City",
            ReminderType: ReminderType.OneShot,
            Recurrence: null,
            DeliveryChannel: deliveryChannel);

        var result = await _reminderService.CreateAsync(request, ct);

        string reply;
        if (result.StatusCode is 200 or 201)
        {
            reply = BuildConfirmation(parsed.ReminderText!, parsed.ScheduledTimeUtc!.Value);
        }
        else if (result.StatusCode == 409)
        {
            reply = "Ya tienes demasiados recordatorios activos. Cancela alguno primero y vuelve a intentarlo. 📋";
        }
        else
        {
            _logger.LogWarning(
                "ReminderDomainAgent: CreateAsync returned {StatusCode}. correlation_id={CorrelationId}",
                result.StatusCode, correlationId);
            reply = "No pude crear el recordatorio en este momento. Inténtalo de nuevo. 🙏";
        }

        await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, reply, ct);

        return new AgentHandleResult(
            result.StatusCode is 200 or 201,
            OutcomeCode: result.StatusCode is 200 or 201 ? "reminder_scheduled" : "reminder_failed");
    }

    private static string BuildConfirmation(string reminderText, DateTimeOffset scheduledUtc)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            var local = TimeZoneInfo.ConvertTime(scheduledUtc, tz);
            var formatted = local.ToString("HH:mm 'del' dddd d 'de' MMMM",
                CultureInfo.GetCultureInfo("es-MX"));
            return $"✅ ¡Listo! Te recuerdo «{reminderText}» a las {formatted}. 🔔";
        }
        catch
        {
            return $"✅ ¡Listo! Te recuerdo «{reminderText}» a las {scheduledUtc:HH:mm} UTC. 🔔";
        }
    }
}
