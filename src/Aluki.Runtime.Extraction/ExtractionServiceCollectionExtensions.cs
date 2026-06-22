using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Extraction.Configuration;
using Aluki.Runtime.Extraction.Persistence;
using Aluki.Runtime.Extraction.Providers;
using Aluki.Runtime.Extraction.Security;
using Aluki.Runtime.Memory.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Extraction;

/// <summary>Registers the SB-004 AI extraction capability.</summary>
public static class ExtractionServiceCollectionExtensions
{
    public static IServiceCollection AddAiExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        // Shared connection factory (also registered by capture/memory).
        services.TryAddSingleton<NpgsqlConnectionFactory>();

        // Azure-only inference: transcription via Azure OpenAI (Whisper), structured
        // extraction via the Azure AI Foundry model-router chat client.
        services.TryAddSingleton<IChatModelRouter, FoundryChatModelRouter>();
        services.AddSingleton<ITranscriptionProvider, AzureOpenAiTranscriptionProvider>();
        services.AddSingleton<IStructuredTextExtractionProvider, FoundryStructuredTextExtractionProvider>();

        services.AddSingleton<ExtractionScopeGuard>();
        services.AddSingleton<ExtractionStore>();
        services.AddSingleton<ExtractionCoordinator>();

        services.Configure<ExtractionOptions>(configuration.GetSection(ExtractionOptions.SectionName));

        return services;
    }
}
