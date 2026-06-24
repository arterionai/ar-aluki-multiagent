using Microsoft.Extensions.DependencyInjection;

namespace Aluki.Runtime.Host.Skills.Tenancy;

/// <summary>
/// Registers generic tenancy services: channel routing, sub-tenant creation,
/// and member assignment. Any ORGANIZATION tenant can use these; no domain-specific
/// logic is included here.
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.AddSingleton<TenancyRepository>();
        return services;
    }
}
