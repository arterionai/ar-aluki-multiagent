using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Reminders.Configuration;
using Aluki.Runtime.Reminders.Delivery;
using Aluki.Runtime.Reminders.Persistence;
using Aluki.Runtime.Reminders.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Reminders;

/// <summary>Registers the SB-005 scheduled reminders capability.</summary>
public static class ReminderServiceCollectionExtensions
{
    public static IServiceCollection AddReminders(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<NpgsqlConnectionFactory>();

        // Default delivery channel until a real outbound channel is wired.
        services.TryAddSingleton<IReminderDeliveryChannel, LoggingReminderDeliveryChannel>();

        services.AddSingleton<ReminderScopeGuard>();
        services.AddSingleton<ReminderStore>();
        services.AddSingleton<ReminderService>();

        services.Configure<ReminderOptions>(configuration.GetSection(ReminderOptions.SectionName));

        return services;
    }
}
