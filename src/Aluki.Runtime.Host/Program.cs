using Azure.Identity;
using Aluki.Runtime.Host;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
