using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aluki.Runtime.Capture.Security;

/// <summary>
/// Resolves and validates a tenant/context principal scope from channel identity
/// before any capture side effect (FR-005, FR-006, FR-014). Fails closed: any
/// unresolved, mismatched, or unauthorized derivation yields a structured denial.
/// Auto-provisions new users: when a sender is unknown, an INDIVIDUAL tenant +
/// user profile + DM context are created atomically so the first message is handled.
/// </summary>
public sealed class PrincipalContextResolver : IPrincipalContextResolver
{
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly ILogger<PrincipalContextResolver> _logger;

    public PrincipalContextResolver(NpgsqlConnectionFactory connectionFactory, ILogger<PrincipalContextResolver> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<PrincipalResolution> ResolveAsync(
        ChannelIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var user = await FindUserAsync(connection, identity.SenderExternalId, cancellationToken);
        if (user is null)
        {
            _logger.LogInformation("PrincipalContextResolver: auto-provisioning new user {Sender}", identity.SenderExternalId);
            user = await ProvisionNewPrincipalAsync(connection, identity.SenderExternalId, cancellationToken);
            if (user is null)
                return PrincipalResolution.Deny(
                    ScopeDenialReason.MembershipNotFound,
                    "Auto-provisioning failed for new sender.");
            _logger.LogInformation("PrincipalContextResolver: provisioned new principal for {Sender} userId={UserId}", identity.SenderExternalId, user);
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

    /// <summary>
    /// Atomically creates tenant + user_profile + membership + DM context + context_access
    /// for a first-time sender. Idempotent: concurrent requests for the same sender converge
    /// to the same principal via ON CONFLICT DO NOTHING.
    /// </summary>
    private static async Task<Guid?> ProvisionNewPrincipalAsync(
        NpgsqlConnection connection,
        string senderExternalId,
        CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            // 1. Upsert user_profile (external_auth_id is UNIQUE).
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO users_profile (id, external_auth_id, phone)
                VALUES (gen_random_uuid(), @sender, @sender)
                ON CONFLICT (external_auth_id) DO NOTHING
                """, connection, tx))
            {
                cmd.Parameters.AddWithValue("sender", senderExternalId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            Guid userId;
            await using (var cmd = new NpgsqlCommand("""
                SELECT id FROM users_profile WHERE external_auth_id = @sender
                """, connection, tx))
            {
                cmd.Parameters.AddWithValue("sender", senderExternalId);
                var result = await cmd.ExecuteScalarAsync(ct);
                if (result is null or DBNull) return null;
                userId = (Guid)result;
            }

            // 2. Check if user already has a tenant — avoid double-provisioning on concurrent calls.
            await using (var cmd = new NpgsqlCommand("""
                SELECT m.tenant_id FROM memberships m
                JOIN tenants t ON t.id = m.tenant_id
                WHERE m.user_id = @user AND m.status = 'ACTIVE' AND t.tenant_type = 'INDIVIDUAL'
                LIMIT 1
                """, connection, tx))
            {
                cmd.Parameters.AddWithValue("user", userId);
                var existing = await cmd.ExecuteScalarAsync(ct);
                if (existing is not null and not DBNull)
                {
                    await tx.RollbackAsync(ct);
                    return userId;
                }
            }

            // 3. Create INDIVIDUAL tenant.
            Guid tenantId;
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO tenants (id, tenant_type, display_name)
                VALUES (gen_random_uuid(), 'INDIVIDUAL', @name)
                RETURNING id
                """, connection, tx))
            {
                cmd.Parameters.AddWithValue("name", senderExternalId);
                tenantId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
            }

            // 4. Create membership (OWNER).
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO memberships (tenant_id, user_id, role)
                VALUES (@tenant, @user, 'OWNER')
                ON CONFLICT (tenant_id, user_id) DO NOTHING
                """, connection, tx))
            {
                cmd.Parameters.AddWithValue("tenant", tenantId);
                cmd.Parameters.AddWithValue("user", userId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 5. Create DM context.
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO contexts (id, tenant_id, context_type, external_context_id, title)
                VALUES (gen_random_uuid(), @tenant, 'DM', @sender, 'Personal')
                ON CONFLICT (tenant_id, context_type, external_context_id) DO NOTHING
                """, connection, tx))
            {
                cmd.Parameters.AddWithValue("tenant", tenantId);
                cmd.Parameters.AddWithValue("sender", senderExternalId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            Guid contextId;
            await using (var cmd = new NpgsqlCommand("""
                SELECT id FROM contexts
                WHERE tenant_id = @tenant AND context_type = 'DM' AND external_context_id = @sender
                """, connection, tx))
            {
                cmd.Parameters.AddWithValue("tenant", tenantId);
                cmd.Parameters.AddWithValue("sender", senderExternalId);
                var result = await cmd.ExecuteScalarAsync(ct);
                if (result is null or DBNull) return null;
                contextId = (Guid)result;
            }

            // 6. Grant context_access.
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO context_access (context_id, user_id, access_role)
                VALUES (@ctx, @user, 'OWNER')
                ON CONFLICT (context_id, user_id) DO NOTHING
                """, connection, tx))
            {
                cmd.Parameters.AddWithValue("ctx", contextId);
                cmd.Parameters.AddWithValue("user", userId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return userId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
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
