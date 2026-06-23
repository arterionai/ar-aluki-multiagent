using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Memory.Security;

/// <summary>
/// Validates that a supplied principal scope is real and authorized before any
/// memory side effect: active tenant membership and active context access.
/// Reads membership/context_access (non-RLS) directly.
/// </summary>
public sealed class MemoryScopeGuard
{
    private readonly NpgsqlConnectionFactory _connectionFactory;

    public MemoryScopeGuard(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> IsAuthorizedAsync(PrincipalScope principal, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                exists (
                    select 1 from memberships
                    where tenant_id = @tenant and user_id = @user and status = 'ACTIVE'
                )
                and exists (
                    select 1 from context_access
                    where context_id = @context and user_id = @user and status = 'ACTIVE'
                );
            """,
            connection);
        command.Parameters.AddWithValue("tenant", principal.TenantId);
        command.Parameters.AddWithValue("user", principal.UserId);
        command.Parameters.AddWithValue("context", principal.ContextId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool ok && ok;
    }
}
