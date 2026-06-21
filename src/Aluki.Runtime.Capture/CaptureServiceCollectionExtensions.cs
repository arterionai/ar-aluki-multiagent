using Aluki.Runtime.Abstractions.Orchestration;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills;
using Aluki.Runtime.Capture.Configuration;
using Aluki.Runtime.Capture.Observability;
using Aluki.Runtime.Capture.Retry;
using Aluki.Runtime.Capture.Skills;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Capture.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aluki.Runtime.Capture;

/// <summary>
/// Single registration entry point for the WhatsApp capture pipeline so the
/// Host (ASP.NET Core) and the Functions (isolated worker) deployments wire the
/// exact same components and behavior.
/// </summary>
public static class CaptureServiceCollectionExtensions
{
    public static IServiceCollection AddWhatsAppCapture(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CaptureOptions>(configuration.GetSection(CaptureOptions.SectionName));

        // Persistence
        services.AddSingleton<NpgsqlConnectionFactory>();
        services.AddSingleton<ICaptureUnitOfWorkFactory, CaptureUnitOfWorkFactory>();

        // Security
        services.AddSingleton<IPrincipalContextResolver, PrincipalContextResolver>();
        services.AddSingleton<IConsentStopPolicy, ConsentStopPolicyService>();

        // Observability + reliability
        services.AddSingleton<CaptureTelemetry>();
        services.AddSingleton<CaptureRetryPolicy>();

        // Capture skills (concrete + exposed via ISkill for the dispatcher)
        services.AddSingleton<ScopeGuardSkill>();
        services.AddSingleton<NormalizeWhatsAppInboundSkill>();
        services.AddSingleton<IdempotencyGuardSkill>();
        services.AddSingleton<PersistCaptureSkill>();
        services.AddSingleton<PersistUnsupportedCaptureSkill>();
        services.AddSingleton<WriteCaptureAuditSkill>();
        services.AddSingleton<ISkill>(sp => sp.GetRequiredService<ScopeGuardSkill>());
        services.AddSingleton<ISkill>(sp => sp.GetRequiredService<NormalizeWhatsAppInboundSkill>());
        services.AddSingleton<ISkill>(sp => sp.GetRequiredService<IdempotencyGuardSkill>());
        services.AddSingleton<ISkill>(sp => sp.GetRequiredService<PersistCaptureSkill>());
        services.AddSingleton<ISkill>(sp => sp.GetRequiredService<PersistUnsupportedCaptureSkill>());
        services.AddSingleton<ISkill>(sp => sp.GetRequiredService<WriteCaptureAuditSkill>());
        services.AddSingleton<SkillDispatcher>();

        // Audit side-effect skills (own transaction scope)
        services.AddSingleton<WriteScopeDeniedAuditSkill>();
        services.AddSingleton<WriteRetryAuditSkill>();

        // Coordinator
        services.AddSingleton<WhatsAppCaptureCoordinator>();
        services.AddSingleton<IAgentCoordinator>(sp => sp.GetRequiredService<WhatsAppCaptureCoordinator>());

        return services;
    }
}
