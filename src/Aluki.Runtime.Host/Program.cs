using Aluki.Runtime.Capture;
using Aluki.Runtime.Host;
using Aluki.Runtime.Host.Calendar;
using Aluki.Runtime.Host.Skills.Billing;
using Aluki.Runtime.Host.Skills.Feedback;
using Aluki.Runtime.Host.Skills.Governance;
using Aluki.Runtime.Host.Skills.LinkCapture;
using Aluki.Runtime.Host.Skills.SemanticGraph;
using Aluki.Runtime.Host.Skills.SuggestionsAdmin;
using Aluki.Runtime.Host.Skills.YouTubeLinks;
using Aluki.Runtime.Host.Channels.WhatsApp;
using Aluki.Runtime.Host.Endpoints;
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

// Link capture, enrichment, confirmation, and recall.
builder.Services.AddLinkCapture(builder.Configuration);

// YouTube link save and classification (SB-008B).
builder.Services.AddYouTubeLinkCapture(builder.Configuration);

// Feedback suggestions capture (SB-007).
builder.Services.AddFeedbackCapture(builder.Configuration);
builder.Services.AddSuggestionsAdmin(builder.Configuration);

// Governance & Security (SB-012): consent, policy rules, decision engine.
builder.Services.AddGovernance(builder.Configuration);

// Semantic Graph (SB-011): entity resolution, relationship extraction, graph traversal.
builder.Services.AddSemanticGraph(builder.Configuration);

// Billing & Package Management (SB-010): entitlement, ledger, invoices, credits.
builder.Services.AddBilling(builder.Configuration);

// Background heartbeat (existing runtime worker)
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "aluki-runtime-host" }));
app.MapWhatsAppInbound();
app.MapCalendarEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program { }
