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

    /// <summary>
    /// Same GUC application as <see cref="ApplyAsync"/> but as a batch command, so
    /// callers can fold it into the same round-trip as their first real statement
    /// (prepend it to an <see cref="NpgsqlBatch"/> and skip its result set with
    /// <c>reader.NextResultAsync</c>).
    /// </summary>
    public static NpgsqlBatchCommand CreateApplyBatchCommand(Guid tenantId, Guid? userId)
    {
        var command = new NpgsqlBatchCommand(
            "select set_config('app.current_tenant', @tenant, true), " +
            "       set_config('app.current_user_id', @user, true);");
        command.Parameters.AddWithValue("tenant", tenantId.ToString());
        command.Parameters.AddWithValue("user", userId?.ToString() ?? string.Empty);
        return command;
    }
}
