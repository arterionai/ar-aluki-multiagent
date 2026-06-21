using Aluki.Runtime.Capture;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddWhatsAppCapture(context.Configuration);
    })
    .Build();

await host.RunAsync();
