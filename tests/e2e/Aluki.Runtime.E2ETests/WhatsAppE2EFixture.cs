using Npgsql;
using Xunit;

namespace Aluki.Runtime.E2ETests;

/// <summary>
/// One-time setup for all E2E tests: reads env vars, opens Postgres connection, verifies the
/// seeded principal exists, and runs a preflight probe to detect SheloNabel routing.
/// </summary>
public sealed class WhatsAppE2EFixture : IAsyncLifetime
{
    public const string SenderWaId = "14252307522";
    public const string SeededUserId = "55555555-5555-5555-5555-555555555555";

    public HttpClient Http { get; private set; } = null!;
    public NpgsqlDataSource Db { get; private set; } = null!;
    public string PhoneNumberId { get; private set; } = null!;
    public string? MetaAppSecret { get; private set; }

    // True when preflight detected SheloNabelDomainAgent intercepts this wa_id.
    public bool SheloNabelRouting { get; private set; }

    public async Task InitializeAsync()
    {
        var functionUrl = Environment.GetEnvironmentVariable("E2E_FUNCTION_URL")
            ?? "https://func-araluki-dev-6155.azurewebsites.net";
        var pgConn = Environment.GetEnvironmentVariable("E2E_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("E2E_POSTGRES_CONNECTION is required.");
        PhoneNumberId = Environment.GetEnvironmentVariable("E2E_META_PHONE_NUMBER_ID")
            ?? throw new InvalidOperationException("E2E_META_PHONE_NUMBER_ID is required.");
        MetaAppSecret = Environment.GetEnvironmentVariable("E2E_META_APP_SECRET");

        Http = new HttpClient { BaseAddress = new Uri(functionUrl) };
        Db = NpgsqlDataSource.Create(pgConn);

        await VerifyPrincipalAsync();
        await RunSheloNabelPreflightAsync();
    }

    private async Task VerifyPrincipalAsync()
    {
        await using var cmd = Db.CreateCommand(
            "SELECT COUNT(1) FROM app.users_profile WHERE external_auth_id = $1");
        cmd.Parameters.AddWithValue(SenderWaId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        if (count == 0)
            throw new InvalidOperationException(
                $"Seeded principal for wa_id {SenderWaId} not found. " +
                "Run the 'db-seed-test-principal' GitHub Actions workflow first.");
    }

    private async Task RunSheloNabelPreflightAsync()
    {
        // Send a probe message and observe which agent handles it.
        var wamid = E2EHelpers.NewWamid();
        await E2EHelpers.SendWebhookAsync(Http, MetaAppSecret, PhoneNumberId, SenderWaId, wamid,
            "e2e_preflight_probe_ignore");

        var agentId = await E2EHelpers.WaitForAuditAsync(Db, wamid, TimeSpan.FromSeconds(15));
        SheloNabelRouting = agentId?.StartsWith("shelonabel.", StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task DisposeAsync()
    {
        Http.Dispose();
        await Db.DisposeAsync();
    }
}
