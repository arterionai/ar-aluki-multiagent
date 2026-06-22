using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Conversation;

/// <summary>Registers the SB-000 conversational response capability.</summary>
public static class ConversationServiceCollectionExtensions
{
    public static IServiceCollection AddConversationalResponse(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Shared connection factory (also registered by AddWhatsAppCapture / AddPersonalMemory).
        services.TryAddSingleton<NpgsqlConnectionFactory>();

        services.AddSingleton<IConversationHistoryStore, ConversationHistoryStore>();
        services.AddSingleton<IOutboundMessageStore, OutboundMessageStore>();
        services.AddSingleton<ConversationPromptBuilder>();
        services.AddSingleton<ConversationalResponseAgent>();

        // Register as IDomainAgent so MessageDispatcher picks it up.
        services.AddSingleton<IDomainAgent>(sp => sp.GetRequiredService<ConversationalResponseAgent>());

        services.Configure<ConversationOptions>(configuration.GetSection(ConversationOptions.SectionName));

        return services;
    }
}
