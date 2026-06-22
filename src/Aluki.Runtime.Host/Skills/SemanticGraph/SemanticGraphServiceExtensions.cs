using Aluki.Runtime.Abstractions.SemanticGraph;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Host.Skills.SemanticGraph;

public static class SemanticGraphServiceExtensions
{
    public static IServiceCollection AddSemanticGraph(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<NpgsqlConnectionFactory>();
        services.TryAddSingleton<IChatModelRouter, FoundryChatModelRouter>();

        services.AddScoped<SemanticGraphRepository>();
        services.AddScoped<ISemanticGraphRepository>(sp => sp.GetRequiredService<SemanticGraphRepository>());
        services.AddScoped<EntityResolutionService>();
        services.AddScoped<IEntityResolutionService>(sp => sp.GetRequiredService<EntityResolutionService>());
        services.AddScoped<GraphTraversalService>();
        services.AddScoped<IGraphTraversalService>(sp => sp.GetRequiredService<GraphTraversalService>());

        return services;
    }
}
