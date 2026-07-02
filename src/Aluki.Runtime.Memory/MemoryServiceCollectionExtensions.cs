using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Memory.Configuration;
using Aluki.Runtime.Memory.Dispatch;
using Aluki.Runtime.Memory.Embeddings;
using Aluki.Runtime.Memory.Ingestion;
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
        services.AddSingleton<TopicGroupingSkill>();
        services.AddSingleton<MemoryRecallResponseAssembler>();
        services.AddSingleton<MemoryRecallService>();
        services.AddSingleton<IMemoryRecallService>(sp => sp.GetRequiredService<MemoryRecallService>());
        services.AddSingleton<MemoryInteractionCoordinator>();

        // Replace the capture pipeline's no-op bridge with the real sink so captured
        // WhatsApp messages are promoted into recall-able personal memory.
        services.Replace(ServiceDescriptor.Singleton<IMemoryIngestionSink, MemoryIngestionSink>());

        // Register the person-note domain agent (priority 55, before reminders).
        services.AddSingleton<PersonMemoryDomainAgent>();
        services.AddSingleton<IDomainAgent>(sp => sp.GetRequiredService<PersonMemoryDomainAgent>());

        // Register the note-deletion domain agent (priority 57, after save, before lookup).
        services.AddSingleton<INoteDeletionService, NoteDeletionService>();
        services.AddSingleton<NoteDeletionDomainAgent>();
        services.AddSingleton<IDomainAgent>(sp => sp.GetRequiredService<NoteDeletionDomainAgent>());

        // Register the person-lookup domain agent (priority 58, after save, before reminders).
        services.AddSingleton<IPersonLookupService, PersonLookupService>();
        services.AddSingleton<PersonLookupDomainAgent>();
        services.AddSingleton<IDomainAgent>(sp => sp.GetRequiredService<PersonLookupDomainAgent>());

        // Register the catch-all fallback domain agent (dispatched when no specific agent claims intent).
        services.AddSingleton<MemoryDomainAgent>();
        services.AddSingleton<IDomainAgent>(sp => sp.GetRequiredService<MemoryDomainAgent>());

        services.Configure<MemoryOptions>(configuration.GetSection(MemoryOptions.SectionName));

        return services;
    }
}
