using Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Host.Skills.SuggestionsAdmin;

public static class SuggestionsAdminServiceExtensions
{
    public static IServiceCollection AddSuggestionsAdmin(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<NpgsqlConnectionFactory>();
        services.AddScoped<SuggestionsAdminRepository>();
        services.AddScoped<ISuggestionsAdminRepository>(sp => sp.GetRequiredService<SuggestionsAdminRepository>());
        services.AddScoped<SuggestionsAdminService>();
        services.AddScoped<RewardLedgerService>();
        services.AddScoped<RewardNotificationSweepService>();
        return services;
    }
}
