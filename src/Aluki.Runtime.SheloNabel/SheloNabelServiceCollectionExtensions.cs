using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Capture.Media;
using Aluki.Runtime.Extraction.Providers;
using Aluki.Runtime.Host.Skills.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.SheloNabel;

/// <summary>Registers the Sheló NABEL sales assistant domain agent.</summary>
public static class SheloNabelServiceCollectionExtensions
{
    public static IServiceCollection AddSheloNabelAssistant(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
            services.Configure<SheloNabelOptions>(configuration.GetSection(SheloNabelOptions.Section));
        else
            services.AddOptions<SheloNabelOptions>();

        // Fallback stubs — overridden by real registrations in Functions (registered before this call).
        services.TryAddSingleton<IMetaMediaClient, NullMetaMediaClient>();
        services.TryAddSingleton<ITranscriptionProvider, NullTranscriptionProvider>();

        // Generic tenancy layer (channel routing, sub-tenants, member assignments).
        services.AddTenancy();

        services.AddSingleton<SheloNabelPromptBuilder>();
        services.AddSingleton<SheloNabelCrmService>();
        services.AddSingleton<SheloNabelDomainAgent>();
        services.AddSingleton<IDomainAgent>(sp => sp.GetRequiredService<SheloNabelDomainAgent>());

        return services;
    }
}
