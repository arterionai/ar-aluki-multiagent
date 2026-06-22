using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar.Connect;
using Aluki.Runtime.Calendar.Skills;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Calendar.Dispatch;

/// <summary>
/// Domain agent that handles WhatsApp scheduling requests (priority 50, ahead of the
/// catch-all conversational agent). When the user asks to create a calendar event:
/// runs <see cref="CalendarCreateSkill"/> and, if no provider is connected, replies
/// with a secure consent link (from <see cref="ICalendarConnectLinkService"/>); on
/// success confirms; otherwise asks for clarification.
/// </summary>
public sealed class CalendarDomainAgent : IDomainAgent
{
    public const string Id = "calendar.scheduling";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICalendarConnectLinkService _links;
    private readonly IWhatsAppMessenger _messenger;
    private readonly CalendarOptions _options;
    private readonly ILogger<CalendarDomainAgent> _logger;

    public CalendarDomainAgent(
        IServiceScopeFactory scopeFactory,
        ICalendarConnectLinkService links,
        IWhatsAppMessenger messenger,
        IOptions<CalendarOptions> options,
        ILogger<CalendarDomainAgent> logger)
    {
        _scopeFactory = scopeFactory;
        _links = links;
        _messenger = messenger;
        _options = options.Value;
        _logger = logger;
    }

    public string AgentId => Id;
    public int Priority => 50; // evaluated before ConversationalResponseAgent (100)
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
        => message.ChannelType == ChannelType.WhatsApp
           && !string.IsNullOrWhiteSpace(message.SenderExternalId)
           && !string.IsNullOrWhiteSpace(message.PhoneNumberId)
           && CalendarSchedulingDetector.LooksLikeScheduling(message.Text);

    public async Task<AgentHandleResult> HandleAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        var phoneNumberId = message.PhoneNumberId!;
        var recipientWaId = message.SenderExternalId!;
        var correlationId = message.CorrelationId ?? message.MessageId;
        var providerHint = CalendarSchedulingDetector.DetectProviderHint(message.Text);

        // CalendarCreateSkill is scoped (Postgres repos); resolve it in a fresh scope.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var createSkill = scope.ServiceProvider.GetRequiredService<CalendarCreateSkill>();

        var result = await createSkill.ExecuteAsync(new CalendarCreateRequest(
            TenantId: principal.TenantId,
            ContextId: principal.ContextId,
            UserId: principal.UserId,
            NaturalLanguageInput: message.Text!,
            ProviderHint: providerHint,
            CorrelationId: correlationId), ct);

        var reply = result.OutcomeType switch
        {
            CalendarOutcomeType.Created or CalendarOutcomeType.PreviouslyCreated =>
                CalendarSchedulingReply.Confirmation(result.FinalTitle, result.FinalStartUtc, result.FinalTimezone),
            CalendarOutcomeType.ReconnectRequired =>
                CalendarSchedulingReply.ConnectPrompt(BuildConnectLinks(principal, providerHint, result.SelectedProvider)),
            CalendarOutcomeType.ClarificationRequired =>
                CalendarSchedulingReply.Clarification(result.ClarificationQuestion),
            CalendarOutcomeType.Denied => CalendarSchedulingReply.Denied(),
            _ => CalendarSchedulingReply.Failed(),
        };

        await _messenger.SendTextMessageAsync(phoneNumberId, recipientWaId, reply, ct);

        return new AgentHandleResult(true, OutcomeCode: result.OutcomeType.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// Connect links to offer: the provider the user named (if any), else every enabled
    /// provider. Keeps us from sending a Google link when only Outlook is configured.
    /// </summary>
    private IReadOnlyList<(CalendarProvider Provider, string Url)> BuildConnectLinks(
        PrincipalContext principal, string? providerHint, CalendarProvider? selectedProvider)
    {
        var targets = new List<CalendarProvider>();

        if (providerHint == "outlook") targets.Add(CalendarProvider.Outlook);
        else if (providerHint == "google") targets.Add(CalendarProvider.Google);
        else
        {
            if (_options.Outlook.Enabled) targets.Add(CalendarProvider.Outlook);
            if (_options.Google.Enabled) targets.Add(CalendarProvider.Google);
            if (targets.Count == 0 && selectedProvider is not null) targets.Add(selectedProvider.Value);
        }

        var links = new List<(CalendarProvider, string)>();
        foreach (var provider in targets)
        {
            try
            {
                links.Add((provider, _links.CreateStartUrl(
                    principal.TenantId, principal.ContextId, principal.UserId, provider)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mint connect link for {Provider}.", provider);
            }
        }
        return links;
    }
}
