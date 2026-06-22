using Aluki.Runtime.Abstractions.Billing;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Host.Skills.Billing;

public static class BillingServiceExtensions
{
    public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<NpgsqlConnectionFactory>();
        services.AddScoped<BillingRepository>();
        services.AddScoped<IBillingRepository>(sp => sp.GetRequiredService<BillingRepository>());
        services.AddScoped<EntitlementService>();
        services.AddScoped<IEntitlementService>(sp => sp.GetRequiredService<EntitlementService>());
        services.AddScoped<BillingCycleService>();
        services.AddScoped<IBillingCycleService>(sp => sp.GetRequiredService<BillingCycleService>());
        return services;
    }
}
