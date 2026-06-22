using Aluki.Runtime.Capture;
using Aluki.Runtime.Host;
using Aluki.Runtime.Host.Calendar;
using Aluki.Runtime.Host.Channels.WhatsApp;
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

// WhatsApp capture pipeline (shared with the Functions deployment).
builder.Services.AddWhatsAppCapture(builder.Configuration);

// Calendar integration skills, repositories, and telemetry.
builder.Services.AddCalendarIntegration(builder.Configuration);

// Background heartbeat (existing runtime worker)
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "aluki-runtime-host" }));
app.MapWhatsAppInbound();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program { }
