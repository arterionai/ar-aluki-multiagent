using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Memory.Configuration;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Recall;
using Aluki.Runtime.Memory.Persistence;
using Aluki.Runtime.Memory.Security;
using Aluki.Runtime.Memory.Skills;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Memory;

/// <summary>Registers the SB-002 personal memory capability.</summary>
public static class MemoryServiceCollectionExtensions
{
    public static IServiceCollection AddPersonalMemory(this IServiceCollection services, IConfiguration configuration)
    {
        // Shared connection factory (also registered by AddWhatsAppCapture).
        services.TryAddSingleton<NpgsqlConnectionFactory>();

        services.AddSingleton<IEmbeddingClient, AzureOpenAIEmbeddingClient>();
        services.AddSingleton<IChatModelRouter, FoundryChatModelRouter>();
        services.AddSingleton<MemoryIntentClassifierSkill>();
        services.AddSingleton<MemoryScopeGuard>();
        services.AddSingleton<MemoryStore>();
        services.AddSingleton<MemoryRecallService>();
        services.AddSingleton<MemoryInteractionCoordinator>();

        services.Configure<MemoryOptions>(configuration.GetSection(MemoryOptions.SectionName));

        return services;
    }
}
