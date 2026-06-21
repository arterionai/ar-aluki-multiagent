using Aluki.Runtime.Functions.Channels.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<InMemoryCaptureStore>();
    })
    .Build();

await host.RunAsync();
