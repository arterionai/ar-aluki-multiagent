using Microsoft.AspNetCore.Mvc.Testing;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Test host that disables Key Vault loading so contract tests can boot without
/// Azure credentials or network access. Key Vault is gated via an environment
/// variable because <c>Program</c> reads it during startup, before
/// <see cref="WebApplicationFactory{TEntryPoint}"/> configuration callbacks run.
/// </summary>
public sealed class CaptureWebApplicationFactory : WebApplicationFactory<Program>
{
    public CaptureWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("KeyVault__Enabled", "false");
    }
}
