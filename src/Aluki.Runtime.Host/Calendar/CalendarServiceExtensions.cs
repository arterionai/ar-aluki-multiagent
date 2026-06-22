using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Audit;
using Aluki.Runtime.Host.Calendar.Observability;
using Aluki.Runtime.Host.Calendar.Persistence;
using Aluki.Runtime.Host.Calendar.Providers;
using Aluki.Runtime.Host.Calendar.Security;
using Aluki.Runtime.Host.Calendar.Skills;

namespace Aluki.Runtime.Host.Calendar;

internal static class CalendarServiceExtensions
{
    public static IServiceCollection AddCalendarIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CalendarOptions>(configuration.GetSection("Calendar"));

        // Persistence
        services.AddScoped<PostgresCalendarRepository>();
        services.AddScoped<ICalendarConnectionRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IOAuthCallbackStateRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IEventCreationRequestRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IDeduplicationRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<ICalendarOutcomeRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<ICalendarAuditRepository, PostgresCalendarAuditRepository>();

        // Security + observability
        services.AddScoped<ICalendarScopeGuard, DefaultCalendarScopeGuard>();
        services.AddScoped<CalendarAuditWriter>();
        services.AddScoped<CalendarAuthorizationAuditSkill>();
        services.AddSingleton<CalendarTelemetry>();

        // Provider adapters
        services.AddScoped<OutlookCalendarProvider>();
        services.AddScoped<ICalendarProvider>(sp => sp.GetRequiredService<OutlookCalendarProvider>());

        // US1 skills
        services.AddScoped<CalendarConnectSkill>();
        services.AddScoped<CalendarCallbackSkill>();
        services.AddScoped<CalendarDisconnectSkill>();

        // US2 skills
        services.AddScoped<CalendarRequestClassifierSkill>();
        services.AddScoped<CalendarTimezoneResolverSkill>();
        services.AddScoped<CalendarClarificationSkill>();
        services.AddScoped<CalendarProviderSelectionSkill>();
        services.AddScoped<CalendarIdempotencyGuardSkill>();
        services.AddScoped<CalendarCreateSkill>();

        return services;
    }
}
