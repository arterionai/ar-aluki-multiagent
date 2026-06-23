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
        services.AddDelegatedReminders(context.Configuration);
        services.AddLinkCapture(context.Configuration);
        services.AddYouTubeLinkCapture(context.Configuration);
        services.AddFeedbackCapture(context.Configuration);
        services.AddSuggestionsAdmin(context.Configuration);
        services.AddGovernance(context.Configuration);
        services.AddSemanticGraph(context.Configuration);
        services.AddBilling(context.Configuration);
        services.AddConversationalResponse(context.Configuration);

        // Outbound delivery feedback (read receipt + typing indicator) via Graph API.
        services.AddHttpClient<IWhatsAppMessenger, MetaWhatsAppMessenger>();

        // Async media download (Graph API -> Blob). Overrides the no-op queue.
        services.AddHttpClient<IMetaMediaClient, MetaMediaClient>();
        services.AddSingleton<IMediaBlobStore, BlobMediaStore>();
        services.AddSingleton<IMediaDownloadQueue, StorageQueueMediaDownloadQueue>();
        services.AddSingleton<IMediaRefUpdater, MediaRefUpdater>();
        services.AddSingleton<MediaDownloadProcessor>();
    })
    .Build();

await host.RunAsync();
