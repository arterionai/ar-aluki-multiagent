using Npgsql;

namespace Aluki.Runtime.Capture.Persistence;

/// <summary>
/// Applies the PostgreSQL session GUCs required by row-level security before any
/// scoped read/write. Uses transaction-local <c>set_config(..., true)</c> so the
/// scope is automatically cleared at transaction end.
/// </summary>
public static class ScopedSessionContextSetter
{
    public static async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select set_config('app.current_tenant', @tenant, true), " +
            "       set_config('app.current_user_id', @user, true);",
            connection,
            transaction);

        command.Parameters.AddWithValue("tenant", tenantId.ToString());
        command.Parameters.AddWithValue("user", userId?.ToString() ?? string.Empty);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
