using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Host.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Security;

/// <summary>
/// Resolves and validates a tenant/context principal scope from channel identity
/// before any capture side effect (FR-005, FR-006, FR-014). Fails closed: any
/// unresolved, mismatched, or unauthorized derivation yields a structured denial.
/// </summary>
/// <remarks>
/// Tenant is derived from active channel membership; context is derived from
/// explicit metadata when present, otherwise the principal's default personal
/// context. <c>memberships</c>/<c>users_profile</c> are read without RLS; context
/// reads run under the resolved tenant/user session scope.
/// </remarks>
public sealed class PrincipalContextResolver : IPrincipalContextResolver
{
    private readonly NpgsqlConnectionFactory _connectionFactory;

    public PrincipalContextResolver(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PrincipalResolution> ResolveAsync(
        ChannelIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var user = await FindUserAsync(connection, identity.SenderExternalId, cancellationToken);
        if (user is null)
        {
            return PrincipalResolution.Deny(
                ScopeDenialReason.MembershipNotFound,
                "No user profile is mapped to the inbound sender identity.");
        }

        var membership = await ResolveMembershipAsync(connection, user.Value, identity.TenantHint, cancellationToken);
        if (membership is null)
        {
            return PrincipalResolution.Deny(
                ScopeDenialReason.MembershipNotFound,
                "No active membership resolved for the sender.",
                tenantId: identity.TenantHint,
                userId: user);
        }

        var (tenantId, role) = membership.Value;

        // Apply the resolved scope so context reads are RLS-enforced.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ScopedSessionContextSetter.ApplyAsync(connection, transaction, tenantId, user, cancellationToken);

        Guid contextId;
        if (identity.ContextId is { } explicitContext)
        {
            if (!await IsContextAuthorizedAsync(connection, transaction, explicitContext, user.Value, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return PrincipalResolution.Deny(
                    ScopeDenialReason.ContextNotAuthorized,
                    "Sender is not authorized for the requested context.",
                    tenantId: tenantId,
                    userId: user);
            }

            contextId = explicitContext;
        }
        else
        {
            var defaultContext = await ResolveDefaultContextAsync(connection, transaction, user.Value, cancellationToken);
            if (defaultContext is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return PrincipalResolution.Deny(
                    ScopeDenialReason.ContextNotFound,
                    "No default personal context available for the sender.",
                    tenantId: tenantId,
                    userId: user);
            }

            contextId = defaultContext.Value;
        }

        await transaction.RollbackAsync(cancellationToken);

        var principal = new PrincipalContext(
            UserId: user.Value,
            TenantId: tenantId,
            ContextId: contextId,
            Roles: [role],
            SourceChannel: identity.SourceChannel,
            CorrelationId: identity.CorrelationId);

        return PrincipalResolution.Allow(principal);
    }

    private static async Task<Guid?> FindUserAsync(
        NpgsqlConnection connection,
        string senderExternalId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select id
            from users_profile
            where (external_auth_id = @sender or phone = @sender)
              and status = 'ACTIVE'
            limit 1;
            """,
            connection);
        command.Parameters.AddWithValue("sender", senderExternalId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (Guid)result;
    }

    private static async Task<(Guid TenantId, string Role)?> ResolveMembershipAsync(
        NpgsqlConnection connection,
        Guid userId,
        Guid? tenantHint,
        CancellationToken cancellationToken)
    {
        if (tenantHint is { } hint)
        {
            await using var hinted = new NpgsqlCommand(
                """
                select role
                from memberships
                where tenant_id = @tenant and user_id = @user and status = 'ACTIVE'
                limit 1;
                """,
                connection);
            hinted.Parameters.AddWithValue("tenant", hint);
            hinted.Parameters.AddWithValue("user", userId);

            var role = await hinted.ExecuteScalarAsync(cancellationToken);
            return role is null or DBNull ? null : (hint, (string)role);
        }

        // No hint: prefer the personal (INDIVIDUAL) tenant, else a single membership.
        await using var command = new NpgsqlCommand(
            """
            select m.tenant_id, m.role
            from memberships m
            join tenants t on t.id = m.tenant_id
            where m.user_id = @user and m.status = 'ACTIVE'
            order by case when t.tenant_type = 'INDIVIDUAL' then 0 else 1 end, m.created_at
            limit 2;
            """,
            connection);
        command.Parameters.AddWithValue("user", userId);

        var memberships = new List<(Guid TenantId, string Role)>(2);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                memberships.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }

        return memberships.Count switch
        {
            0 => null,
            _ => memberships[0]
        };
    }

    private static async Task<bool> IsContextAuthorizedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid contextId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select exists (
                select 1
                from contexts c
                join context_access ca on ca.context_id = c.id
                where c.id = @context
                  and ca.user_id = @user
                  and ca.status = 'ACTIVE'
            );
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("context", contextId);
        command.Parameters.AddWithValue("user", userId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool authorized && authorized;
    }

    private static async Task<Guid?> ResolveDefaultContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select c.id
            from contexts c
            join context_access ca on ca.context_id = c.id
            where ca.user_id = @user and ca.status = 'ACTIVE'
            order by case when c.context_type = 'DM' then 0 else 1 end, c.created_at
            limit 1;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("user", userId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (Guid)result;
    }
}
