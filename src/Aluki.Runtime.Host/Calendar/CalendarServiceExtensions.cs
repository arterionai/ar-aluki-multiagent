using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Audit;
using Aluki.Runtime.Host.Calendar.Observability;
using Aluki.Runtime.Host.Calendar.Persistence;
using Aluki.Runtime.Host.Calendar.Security;

namespace Aluki.Runtime.Host.Calendar;

internal static class CalendarServiceExtensions
{
    public static IServiceCollection AddCalendarIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CalendarOptions>(configuration.GetSection("Calendar"));

        services.AddScoped<PostgresCalendarRepository>();
        services.AddScoped<ICalendarConnectionRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IOAuthCallbackStateRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IEventCreationRequestRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IDeduplicationRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<ICalendarOutcomeRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());

        services.AddScoped<ICalendarAuditRepository, PostgresCalendarAuditRepository>();
        services.AddScoped<ICalendarScopeGuard, DefaultCalendarScopeGuard>();
        services.AddScoped<CalendarAuditWriter>();
        services.AddSingleton<CalendarTelemetry>();

        return services;
    }
}
