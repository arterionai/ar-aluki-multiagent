using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.LinkCapture;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Host.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Host.Skills.LinkCapture;

public static class LinkCaptureServiceExtensions
{
    public static IServiceCollection AddLinkCapture(this IServiceCollection services, IConfiguration configuration)
    {
        // Connection factory (idempotent — other features may register it first)
        services.TryAddSingleton<NpgsqlConnectionFactory>();

        // Repository
        services.AddScoped<LinkCaptureRepository>();
        services.AddScoped<ILinkCaptureRepository>(sp => sp.GetRequiredService<LinkCaptureRepository>());

        // Policy evaluator
        services.AddSingleton<ILinkEnrichmentPolicyEvaluator, LinkEnrichmentPolicyEvaluator>();

        // HTTP client for enrichment
        services.AddHttpClient("link-enrichment").ConfigureHttpClient(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(5); // outer safety; inner uses CancellationToken
            c.DefaultRequestHeaders.Add("User-Agent", "AlukiBot/1.0 (+https://aluki.app)");
        });

        // Services
        services.AddScoped<LinkEnrichmentRunner>();
        services.AddScoped<LinkCaptureService>();
        services.AddScoped<LinkConfirmationService>();
        services.AddScoped<LinkRecallService>();

        return services;
    }
}
