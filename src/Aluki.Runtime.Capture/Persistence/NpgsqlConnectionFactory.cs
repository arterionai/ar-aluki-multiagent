using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Aluki.Runtime.Capture.Persistence;

/// <summary>
/// Creates open PostgreSQL connections. The connection string is resolved from
/// the first configured of: <c>Postgres:ConnectionString</c>,
/// <c>Postgres:AppConnection</c>, <c>Postgres:AdminConnection</c>, or the flat
/// Key Vault-backed <c>PostgresConnectionString</c>. The deployed Function App
/// supplies <c>Postgres__AppConnection</c> as a Key Vault reference.
/// </summary>
public sealed class NpgsqlConnectionFactory
{
    private static readonly string[] ConnectionStringKeys =
    [
        "Postgres:ConnectionString",
        "Postgres:AppConnection",
        "Postgres:AdminConnection",
        "PostgresConnectionString"
    ];

    private readonly string? _connectionString;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        foreach (var key in ConnectionStringKeys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                _connectionString = value;
                break;
            }
        }
    }

    /// <summary>True when a connection string is available.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is not configured. Set one of " +
                "'Postgres:ConnectionString', 'Postgres:AppConnection', " +
                "'Postgres:AdminConnection', or the 'PostgresConnectionString' secret.");
        }

        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
