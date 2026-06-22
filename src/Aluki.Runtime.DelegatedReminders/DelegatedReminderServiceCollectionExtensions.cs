using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.DelegatedReminders.Configuration;
using Aluki.Runtime.DelegatedReminders.Delivery;
using Aluki.Runtime.DelegatedReminders.Persistence;
using Aluki.Runtime.DelegatedReminders.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.DelegatedReminders;

/// <summary>Registers the SB-006 delegated reminders capability.</summary>
public static class DelegatedReminderServiceCollectionExtensions
{
    public static IServiceCollection AddDelegatedReminders(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<NpgsqlConnectionFactory>();

        services.TryAddSingleton<IDelegatedReminderDeliveryChannel, LoggingDelegatedReminderDeliveryChannel>();

        services.AddSingleton<DelegatedReminderScopeGuard>();
        services.AddSingleton<DelegatedReminderStore>();
        services.AddSingleton<DelegatedReminderService>();

        services.Configure<DelegatedReminderOptions>(
            configuration.GetSection(DelegatedReminderOptions.SectionName));

        return services;
    }
}
