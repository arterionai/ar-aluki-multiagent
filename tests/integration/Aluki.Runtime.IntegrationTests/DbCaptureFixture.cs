using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

/// <summary>
/// Provides a migrated PostgreSQL database and a configured web host for the
/// database-backed capture integration tests. Tests are skipped when the
/// <c>ALUKI_TEST_POSTGRES</c> connection string is not provided. The target
/// database must have the <c>vector</c> extension available (migration 002).
/// </summary>
public sealed class DbCaptureFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }

    public bool Available => !string.IsNullOrWhiteSpace(ConnectionString);

    public CaptureDbWebApplicationFactory Factory { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("ALUKI_TEST_POSTGRES");
        if (!Available)
        {
            return;
        }

        await ApplyMigrationsAsync(ConnectionString!);
        Factory = new CaptureDbWebApplicationFactory(ConnectionString!);
    }

    public Task DisposeAsync()
    {
        Factory?.Dispose();
        return Task.CompletedTask;
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Seeds a tenant, user, membership, DM context, and context access.</summary>
    public async Task<SeededPrincipal> SeedPrincipalAsync(string tenantType = "INDIVIDUAL")
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var externalId = $"wa-{Guid.NewGuid():N}";

        await using var connection = await OpenAsync();
        await Exec(connection,
            "insert into tenants (id, tenant_type, display_name) values (@id, @type, @name);",
            ("id", tenantId), ("type", tenantType), ("name", "Test Tenant"));
        await Exec(connection,
            "insert into users_profile (id, external_auth_id, phone) values (@id, @ext, @phone);",
            ("id", userId), ("ext", externalId), ("phone", externalId));
        await Exec(connection,
            "insert into memberships (tenant_id, user_id, role) values (@tenant, @user, 'OWNER');",
            ("tenant", tenantId), ("user", userId));
        await Exec(connection,
            "insert into contexts (id, tenant_id, context_type, external_context_id, title) " +
            "values (@id, @tenant, 'DM', @ext, 'DM');",
            ("id", contextId), ("tenant", tenantId), ("ext", externalId));
        await Exec(connection,
            "insert into context_access (context_id, user_id, access_role) values (@ctx, @user, 'OWNER');",
            ("ctx", contextId), ("user", userId));

        return new SeededPrincipal(tenantId, userId, contextId, externalId);
    }

    public async Task<int> CountMessagesAsync(Guid tenantId, string providerMessageId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from unified_message_artifact where tenant_id = @t and provider_message_id = @p;",
            connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("p", providerMessageId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<int> CountMediaAsync(Guid tenantId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from media_artifact where tenant_id = @t;",
            connection);
        command.Parameters.AddWithValue("t", tenantId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<int> CountAuditAsync(Guid tenantId, string eventName)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from capture_audit_event where tenant_id = @t and event_name = @e;",
            connection);
        command.Parameters.AddWithValue("t", tenantId);
        command.Parameters.AddWithValue("e", eventName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ApplyMigrationsAsync(string connectionString)
    {
        var migrationsDir = LocateMigrationsDir();
        var files = new[]
        {
            "001_init_tenancy.sql",
            "002_init_artifacts.sql",
            "003_enable_rls.sql",
            "004_whatsapp_capture_foundation.sql",
            "005_whatsapp_capture_rls.sql",
            "006_document_support.sql",
            "007_personal_memory.sql",
            "008_calendar_integration.sql",
            "009_ai_extraction.sql",
            "010_reminders.sql",
            "011_reminder_retries.sql",
            "012_delegated_reminders.sql",
            "013_link_capture.sql",
            "014_youtube_link_capture.sql",
            "015_feedback_suggestions.sql",
            "016_suggestions_admin.sql",
            "017_dispatch_audit.sql",
            "018_governance_security.sql",
            "019_semantic_graph.sql",
            "020_conversational_response.sql",
            "021_billing.sql",
            "022_calendar_oauth_tokens.sql"
        };

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var file in files)
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(migrationsDir, file));
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string LocateMigrationsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "db", "migrations");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate db/migrations from the test base directory.");
    }

    private static async Task Exec(NpgsqlConnection connection, string sql, params (string Name, object Value)[] args)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}

public sealed record SeededPrincipal(Guid TenantId, Guid UserId, Guid ContextId, string ExternalId);

public sealed class CaptureDbWebApplicationFactory : WebApplicationFactory<Program>
{
    public CaptureDbWebApplicationFactory(string connectionString)
    {
        // Set before the host is created: Program reads these at startup via the
        // default environment-variable configuration source.
        Environment.SetEnvironmentVariable("KeyVault__Enabled", "false");
        Environment.SetEnvironmentVariable("Postgres__ConnectionString", connectionString);
    }
}
