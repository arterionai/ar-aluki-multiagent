using Aluki.Runtime.Abstractions.Orchestration;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills;
using Aluki.Runtime.Host;
using Aluki.Runtime.Host.Capture;
using Aluki.Runtime.Host.Capture.Retry;
using Aluki.Runtime.Host.Capture.Skills;
using Aluki.Runtime.Host.Channels.WhatsApp;
using Aluki.Runtime.Host.Configuration;
using Aluki.Runtime.Host.Observability;
using Aluki.Runtime.Host.Persistence;
using Aluki.Runtime.Host.Security;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

var keyVaultSection = builder.Configuration.GetSection("KeyVault");
var keyVaultEnabled = keyVaultSection.GetValue("Enabled", true);
var keyVaultOptional = keyVaultSection.GetValue("Optional", true);
var keyVaultUriText = keyVaultSection["VaultUri"];

if (keyVaultEnabled && Uri.TryCreate(keyVaultUriText, UriKind.Absolute, out var keyVaultUri))
{
    try
    {
        builder.Configuration.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential());
    }
    catch (Exception ex) when (keyVaultOptional)
    {
        Console.Error.WriteLine($"Key Vault unavailable. Continuing with local configuration. Reason: {ex.Message}");
    }
}

// Configuration
builder.Services.Configure<CaptureOptions>(builder.Configuration.GetSection(CaptureOptions.SectionName));

// Persistence
builder.Services.AddSingleton<NpgsqlConnectionFactory>();
builder.Services.AddSingleton<ICaptureUnitOfWorkFactory, CaptureUnitOfWorkFactory>();

// Security
builder.Services.AddSingleton<IPrincipalContextResolver, PrincipalContextResolver>();
builder.Services.AddSingleton<IConsentStopPolicy, ConsentStopPolicyService>();

// Observability + reliability
builder.Services.AddSingleton<CaptureTelemetry>();
builder.Services.AddSingleton<CaptureRetryPolicy>();

// Capture skills (registered as concrete types and exposed via ISkill for the dispatcher)
builder.Services.AddSingleton<ScopeGuardSkill>();
builder.Services.AddSingleton<NormalizeWhatsAppInboundSkill>();
builder.Services.AddSingleton<IdempotencyGuardSkill>();
builder.Services.AddSingleton<PersistCaptureSkill>();
builder.Services.AddSingleton<PersistUnsupportedCaptureSkill>();
builder.Services.AddSingleton<WriteCaptureAuditSkill>();
builder.Services.AddSingleton<ISkill>(sp => sp.GetRequiredService<ScopeGuardSkill>());
builder.Services.AddSingleton<ISkill>(sp => sp.GetRequiredService<NormalizeWhatsAppInboundSkill>());
builder.Services.AddSingleton<ISkill>(sp => sp.GetRequiredService<IdempotencyGuardSkill>());
builder.Services.AddSingleton<ISkill>(sp => sp.GetRequiredService<PersistCaptureSkill>());
builder.Services.AddSingleton<ISkill>(sp => sp.GetRequiredService<PersistUnsupportedCaptureSkill>());
builder.Services.AddSingleton<ISkill>(sp => sp.GetRequiredService<WriteCaptureAuditSkill>());
builder.Services.AddSingleton<SkillDispatcher>();

// Audit side-effect skills (own transaction scope)
builder.Services.AddSingleton<WriteScopeDeniedAuditSkill>();
builder.Services.AddSingleton<WriteRetryAuditSkill>();

// Coordinator
builder.Services.AddSingleton<WhatsAppCaptureCoordinator>();
builder.Services.AddSingleton<IAgentCoordinator>(sp => sp.GetRequiredService<WhatsAppCaptureCoordinator>());

// Background heartbeat (existing runtime worker)
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "aluki-runtime-host" }));
app.MapWhatsAppInbound();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program { }
