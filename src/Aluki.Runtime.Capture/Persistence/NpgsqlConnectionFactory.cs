using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Aluki.Runtime.Capture.Persistence;

/// <summary>
/// Creates open PostgreSQL connections. The connection string is resolved from
/// <c>Postgres:ConnectionString</c> and falls back to the Key Vault-backed
/// <c>PostgresConnectionString</c> secret (mapped to a flat configuration key).
/// </summary>
public sealed class NpgsqlConnectionFactory
{
    private readonly string? _connectionString;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        var configured = configuration["Postgres:ConnectionString"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = configuration["PostgresConnectionString"];
        }

        _connectionString = configured;
    }

    /// <summary>True when a connection string is available.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is not configured. Set 'Postgres:ConnectionString' " +
                "or provide the 'PostgresConnectionString' secret.");
        }

        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
