using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Reminders.Configuration;
using Aluki.Runtime.Reminders.Delivery;
using Aluki.Runtime.Reminders.Dispatch;
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

        // WhatsApp delivery channel (routing encoded in delivery_channel by ReminderDomainAgent).
        // Falls back to the logging stub only when WhatsApp messenger is unavailable.
        services.TryAddSingleton<IReminderDeliveryChannel, WhatsAppReminderDeliveryChannel>();

        services.AddSingleton<ReminderScopeGuard>();
        services.AddSingleton<ReminderStore>();
        services.AddSingleton<ReminderService>();

        // WhatsApp scheduling agent (priority 60).
        services.AddSingleton<ReminderIntentParser>();
        services.AddSingleton<ReminderDomainAgent>();
        services.AddSingleton<IDomainAgent>(sp => sp.GetRequiredService<ReminderDomainAgent>());

        services.Configure<ReminderOptions>(configuration.GetSection(ReminderOptions.SectionName));

        return services;
    }
}
