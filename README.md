# ar-aluki-multiagent

Initial implementation bootstrap for the Aluki runtime.

## Current runnable baseline

- Solution: Aluki.Runtime.slnx
- Contracts library: src/Aluki.Runtime.Abstractions
- Runtime host: src/Aluki.Runtime.Host

## Build and run

1. dotnet restore Aluki.Runtime.slnx
2. dotnet build Aluki.Runtime.slnx
3. dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj

## Azure Key Vault for tests

The runtime host is configured to load secrets from Azure Key Vault using `DefaultAzureCredential`.

- Vault: `kvaralukidev6155`
- URI: `https://kvaralukidev6155.vault.azure.net/`
- Subscription: `33bbf1e1-134d-42e3-a370-0dfb1da16cff`

### Available secret names

- `AiExtraction--ApiKey`
- `AiExtraction--Endpoint`
- `BlobConnectionString`
- `ConnectivityProbe`
- `Meta--AccessToken`
- `Meta--AppSecret`
- `Meta--PhoneNumberId`
- `Meta--VerifyToken`
- `PostgresConnectionString`
- `Speech--ApiKey`
- `Speech--Endpoint`
- `TwilioAccountSid`
- `TwilioAuthToken`
- `TwilioWhatsAppFrom`

### Local prerequisites

1. Sign in with Azure CLI: `az login`
2. Ensure your identity has Key Vault data-plane read access (for example, `Key Vault Secrets User`) on `kvaralukidev6155`.
3. Run the host as usual.

### Notes

- Secret names with `--` map to configuration sections using `:` (example: `Meta--AccessToken` -> `Meta:AccessToken`).
- Key Vault settings are under `KeyVault` in `src/Aluki.Runtime.Host/appsettings.json`.
- `KeyVault:Optional` is enabled so local startup still works if Key Vault is temporarily unavailable.

## GitHub and Copilot automation access

Repository secrets expected by CI workflows:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Federated credentials configured in Microsoft Entra for this repository:

- `repo:arterionai/ar-aluki-multiagent:ref:refs/heads/main`
- `repo:arterionai/ar-aluki-multiagent:pull_request`

Validation workflow:

- File: `.github/workflows/azure-keyvault-access-check.yml`
- Trigger: `workflow_dispatch`, `push` to `main`, and `pull_request`
- Checks OIDC login and read access to `ConnectivityProbe` in `kvaralukidev6155`.

