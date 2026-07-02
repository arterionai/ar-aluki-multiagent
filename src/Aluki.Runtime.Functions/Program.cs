using Aluki.Runtime.Calendar;
using Aluki.Runtime.Capture;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Capture.Media;
using Aluki.Runtime.Extraction;
using Aluki.Runtime.Functions.Media;
using Aluki.Runtime.Host.Skills.Billing;
using Aluki.Runtime.Host.Skills.Feedback;
using Aluki.Runtime.Host.Skills.Governance;
using Aluki.Runtime.Host.Skills.LinkCapture;
using Aluki.Runtime.Host.Skills.SemanticGraph;
using Aluki.Runtime.Host.Skills.SuggestionsAdmin;
using Aluki.Runtime.Host.Skills.YouTubeLinks;
using Aluki.Runtime.Conversation;
using Aluki.Runtime.Memory;
using Aluki.Runtime.SheloNabel;
using Aluki.Runtime.DelegatedReminders;
using Aluki.Runtime.Reminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddWhatsAppCapture(context.Configuration);
        services.AddPersonalMemory(context.Configuration);
        services.AddAiExtraction(context.Configuration);
        services.AddCalendarIntegration(context.Configuration);
        services.AddReminders(context.Configuration);
        // Register real WhatsApp delivery before AddDelegatedReminders so the
        // TryAddSingleton fallback (LoggingDelegatedReminderDeliveryChannel) is skipped.
        services.AddSingleton<Aluki.Runtime.DelegatedReminders.Delivery.IDelegatedReminderDeliveryChannel,
            Aluki.Runtime.DelegatedReminders.Delivery.WhatsAppDelegatedReminderDeliveryChannel>();
        services.AddDelegatedReminders(context.Configuration);
        services.AddLinkCapture(context.Configuration);
        services.AddYouTubeLinkCapture(context.Configuration);
        services.AddFeedbackCapture(context.Configuration);
        services.AddSuggestionsAdmin(context.Configuration);
        services.AddGovernance(context.Configuration);
        services.AddSemanticGraph(context.Configuration);
        services.AddBilling(context.Configuration);

        // Register real media client before AddConversationalResponse so the
        // TryAddSingleton fallback (NullMetaMediaClient) inside it is skipped.
        // Short timeout: a hung Graph send must not ride the 100 s HttpClient default
        // on the reply path (sends are best-effort and retried by the user anyway).
        services.AddHttpClient<IWhatsAppMessenger, MetaWhatsAppMessenger>(
            client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient<IMetaMediaClient, MetaMediaClient>();
        services.AddSingleton<IMediaBlobStore, BlobMediaStore>();
        services.AddSingleton<IMediaDownloadQueue, StorageQueueMediaDownloadQueue>();
        services.AddSingleton<IMediaRefUpdater, MediaRefUpdater>();
        services.AddSingleton<MediaDownloadProcessor>();

        services.AddConversationalResponse(context.Configuration);

        // Sheló NABEL sales assistant — members of the shelo tenant.
        // Authorized wa_ids configurable via SheloNabel:AuthorizedWaIds app setting.
        // Real media/transcription clients are already registered above via AddHttpClient.
        services.AddSheloNabelAssistant(context.Configuration);
    })
    .Build();

await host.RunAsync();
