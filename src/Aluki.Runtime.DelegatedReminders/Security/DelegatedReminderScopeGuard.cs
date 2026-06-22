using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Memory;
using Npgsql;

namespace Aluki.Runtime.DelegatedReminders.Security;

/// <summary>
/// Validates that a supplied principal scope is real and authorized before any
/// delegated reminder side effect: active tenant membership, and (when a context
/// is supplied) active context access. Mirrors
/// <see cref="Aluki.Runtime.Reminders.Security.ReminderScopeGuard"/>.
/// </summary>
public sealed class DelegatedReminderScopeGuard
{
    private readonly NpgsqlConnectionFactory _connectionFactory;

    public DelegatedReminderScopeGuard(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> IsAuthorizedAsync(PrincipalScope principal, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var requireContext = principal.ContextId != Guid.Empty;
        var sql = requireContext
            ? """
              select
                  exists (select 1 from memberships
                          where tenant_id = @tenant and user_id = @user and status = 'ACTIVE')
                  and exists (select 1 from context_access
                          where context_id = @context and user_id = @user and status = 'ACTIVE');
              """
            : """
              select exists (select 1 from memberships
                      where tenant_id = @tenant and user_id = @user and status = 'ACTIVE');
              """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant", principal.TenantId);
        command.Parameters.AddWithValue("user", principal.UserId);
        if (requireContext)
        {
            command.Parameters.AddWithValue("context", principal.ContextId);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool ok && ok;
    }
}
