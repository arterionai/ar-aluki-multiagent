using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Aluki.Runtime.Capture.Persistence;

/// <summary>
/// Creates open PostgreSQL connections from a single pooled <see cref="NpgsqlDataSource"/>.
/// The connection string is resolved from the first configured of:
/// <c>Postgres:ConnectionString</c>, <c>Postgres:AppConnection</c>,
/// <c>Postgres:AdminConnection</c>, or the flat Key Vault-backed
/// <c>PostgresConnectionString</c>. The deployed Function App supplies
/// <c>Postgres__AppConnection</c> as a Key Vault reference.
/// Registered as a singleton, so the data source (and its pool) is shared by every
/// store in the worker instead of re-parsing the connection string per open.
/// </summary>
public sealed class NpgsqlConnectionFactory : IAsyncDisposable
{
    private static readonly string[] ConnectionStringKeys =
    [
        "Postgres:ConnectionString",
        "Postgres:AppConnection",
        "Postgres:AdminConnection",
        "PostgresConnectionString"
    ];

    private readonly NpgsqlDataSource? _dataSource;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        foreach (var key in ConnectionStringKeys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                _dataSource = new NpgsqlDataSourceBuilder(value).Build();
                break;
            }
        }
    }

    /// <summary>True when a connection string is available.</summary>
    public bool IsConfigured => _dataSource is not null;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is null)
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is not configured. Set one of " +
                "'Postgres:ConnectionString', 'Postgres:AppConnection', " +
                "'Postgres:AdminConnection', or the 'PostgresConnectionString' secret.");
        }

        return await _dataSource.OpenConnectionAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _dataSource?.DisposeAsync() ?? ValueTask.CompletedTask;
}
