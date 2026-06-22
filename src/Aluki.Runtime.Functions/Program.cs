using Aluki.Runtime.Capture;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Capture.Media;
using Aluki.Runtime.Extraction;
using Aluki.Runtime.Functions.Media;
using Aluki.Runtime.Memory;
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
        services.AddReminders(context.Configuration);

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
