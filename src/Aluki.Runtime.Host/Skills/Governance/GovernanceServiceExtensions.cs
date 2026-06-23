using Aluki.Runtime.Abstractions.Governance;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Host.Skills.Governance;

public static class GovernanceServiceExtensions
{
    public static IServiceCollection AddGovernance(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<NpgsqlConnectionFactory>();
        services.AddScoped<GovernanceRepository>();
        services.AddScoped<IGovernanceRepository>(sp => sp.GetRequiredService<GovernanceRepository>());
        services.AddScoped<ConsentManager>();
        services.AddScoped<IConsentManager>(sp => sp.GetRequiredService<ConsentManager>());
        services.AddScoped<PolicyDecisionEngine>();
        services.AddScoped<IPolicyDecisionEngine>(sp => sp.GetRequiredService<PolicyDecisionEngine>());
        return services;
    }
}
