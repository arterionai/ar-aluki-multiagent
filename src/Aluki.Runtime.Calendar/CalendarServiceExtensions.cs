using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Aluki.Runtime.Calendar.Audit;
using Aluki.Runtime.Calendar.Observability;
using Aluki.Runtime.Calendar.Persistence;
using Aluki.Runtime.Calendar.Providers;
using Aluki.Runtime.Calendar.Security;
using Aluki.Runtime.Calendar.Skills;

namespace Aluki.Runtime.Calendar;

public static class CalendarServiceExtensions
{
    public static IServiceCollection AddCalendarIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CalendarOptions>(configuration.GetSection("Calendar"));

        // Shared Postgres connection factory (no-op if the host already registered it).
        services.TryAddSingleton<NpgsqlConnectionFactory>();

        // Persistence
        services.AddScoped<PostgresCalendarRepository>();
        services.AddScoped<ICalendarConnectionRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IOAuthCallbackStateRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IEventCreationRequestRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<IDeduplicationRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<ICalendarOutcomeRepository>(sp => sp.GetRequiredService<PostgresCalendarRepository>());
        services.AddScoped<ICalendarAuditRepository, PostgresCalendarAuditRepository>();
        services.AddScoped<ICalendarTokenStore, PostgresCalendarTokenStore>();

        // Security + observability
        services.AddScoped<ICalendarScopeGuard, DefaultCalendarScopeGuard>();
        services.AddScoped<CalendarAuditWriter>();
        services.AddScoped<CalendarAuthorizationAuditSkill>();
        services.AddSingleton<CalendarTelemetry>();
        services.AddSingleton<ICalendarTokenProtector, AesGcmCalendarTokenProtector>();

        // OAuth token exchange (code↔token, refresh, account resolution) — one per provider.
        services.AddHttpClient<OutlookOAuthTokenExchanger>();
        services.AddScoped<IOAuthTokenExchanger>(sp => sp.GetRequiredService<OutlookOAuthTokenExchanger>());
        services.AddHttpClient<GoogleOAuthTokenExchanger>();
        services.AddScoped<IOAuthTokenExchanger>(sp => sp.GetRequiredService<GoogleOAuthTokenExchanger>());
        services.AddScoped<ICalendarTokenService, CalendarTokenService>();

        // Provider adapters (typed HttpClients so they call the real Graph/Google APIs).
        services.AddHttpClient<OutlookCalendarProvider>();
        services.AddScoped<ICalendarProvider>(sp => sp.GetRequiredService<OutlookCalendarProvider>());
        services.AddHttpClient<GoogleCalendarProvider>();
        services.AddScoped<ICalendarProvider>(sp => sp.GetRequiredService<GoogleCalendarProvider>());

        // Cross-cutting provider policy
        services.AddSingleton<CalendarProviderParityPolicy>();

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
