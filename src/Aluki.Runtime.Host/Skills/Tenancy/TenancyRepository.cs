using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Host.Skills.Tenancy;

/// <summary>
/// Generic tenancy operations usable by any ORGANIZATION tenant:
/// - WA Business channel registration (phone_number_id → tenant routing)
/// - Sub-tenant creation (parent_tenant_id hierarchy)
/// - Member assignment (generic: maps a contact to a responsible member)
/// </summary>
public sealed class TenancyRepository
{
    private readonly NpgsqlConnectionFactory _db;

    public TenancyRepository(NpgsqlConnectionFactory db) => _db = db;

    // -------------------------------------------------------------------------
    // Channel registration: any org registers its WA Business phone_number_id
    // -------------------------------------------------------------------------

    public async Task RegisterWhatsAppChannelAsync(
        string phoneNumberId,
        Guid tenantId,
        string? displayName,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into tenant_whatsapp_channels (phone_number_id, tenant_id, display_name)
            values (@phone, @tenant, @name)
            on conflict (phone_number_id) do update
                set tenant_id    = excluded.tenant_id,
                    display_name = coalesce(excluded.display_name, tenant_whatsapp_channels.display_name),
                    status       = 'active';
            """,
            conn);
        cmd.Parameters.AddWithValue("phone", phoneNumberId);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("name", displayName is not null ? (object)displayName : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Guid?> LookupChannelTenantAsync(string phoneNumberId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select tenant_id
            from tenant_whatsapp_channels
            where phone_number_id = @phone and status = 'active'
            limit 1;
            """,
            conn);
        cmd.Parameters.AddWithValue("phone", phoneNumberId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (Guid)result;
    }

    // -------------------------------------------------------------------------
    // Sub-tenant hierarchy: create a child tenant under a parent
    // -------------------------------------------------------------------------

    public async Task<Guid> CreateSubTenantAsync(
        Guid parentTenantId,
        string displayName,
        string tenantType,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into tenants (tenant_type, display_name, parent_tenant_id)
            values (@type, @name, @parent)
            returning id;
            """,
            conn);
        cmd.Parameters.AddWithValue("type", tenantType);
        cmd.Parameters.AddWithValue("name", displayName);
        cmd.Parameters.AddWithValue("parent", parentTenantId);
        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    // -------------------------------------------------------------------------
    // Member assignments: assign a contact (external_id) to a responsible member
    // Works for any org — vendedoras in Sheló, trainers in a gym, etc.
    // -------------------------------------------------------------------------

    public async Task<MemberAssignmentResult> AssignContactAsync(
        Guid tenantId,
        Guid ownerUserId,
        string clientExternalId,
        string? assignedToWaId,
        string? notes,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        Guid? assignedUserId = null;
        if (assignedToWaId is not null)
        {
            await using var findCmd = new NpgsqlCommand(
                "select id from users_profile where external_auth_id = @waid limit 1;",
                conn);
            findCmd.Parameters.AddWithValue("waid", assignedToWaId);
            var found = await findCmd.ExecuteScalarAsync(ct);
            if (found is Guid g) assignedUserId = g;
        }

        await using var upsert = new NpgsqlCommand(
            """
            insert into vendedora_assignments
                (tenant_id, owner_user_id, client_external_id, assigned_to_user_id, assigned_to_wa_id, notes)
            values (@tenant, @owner, @client, @assigned, @waId, @notes)
            on conflict (tenant_id, client_external_id) do update
                set assigned_to_user_id = excluded.assigned_to_user_id,
                    assigned_to_wa_id   = excluded.assigned_to_wa_id,
                    notes               = coalesce(excluded.notes, vendedora_assignments.notes),
                    updated_at          = now(),
                    status              = 'active';
            """,
            conn);
        upsert.Parameters.AddWithValue("tenant", tenantId);
        upsert.Parameters.AddWithValue("owner", ownerUserId);
        upsert.Parameters.AddWithValue("client", clientExternalId);
        upsert.Parameters.AddWithValue("assigned", assignedUserId.HasValue ? (object)assignedUserId.Value : DBNull.Value);
        upsert.Parameters.AddWithValue("waId", assignedToWaId is not null ? (object)assignedToWaId : DBNull.Value);
        upsert.Parameters.AddWithValue("notes", notes is not null ? (object)notes : DBNull.Value);
        await upsert.ExecuteNonQueryAsync(ct);

        return new MemberAssignmentResult(clientExternalId, assignedToWaId, assignedUserId);
    }

    public async Task<IReadOnlyList<MemberAssignmentRow>> GetAssignmentsForMemberAsync(
        Guid tenantId,
        Guid memberUserId,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select va.client_external_id,
                   va.assigned_to_wa_id,
                   up.phone,
                   va.notes,
                   va.created_at
            from vendedora_assignments va
            left join users_profile up on up.external_auth_id = va.client_external_id
            where va.tenant_id = @tenant
              and va.assigned_to_user_id = @member
              and va.status = 'active'
            order by va.created_at desc;
            """,
            conn);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("member", memberUserId);

        var rows = new List<MemberAssignmentRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new MemberAssignmentRow(
                ClientExternalId: reader.GetString(0),
                AssignedToWaId: reader.IsDBNull(1) ? null : reader.GetString(1),
                Phone: reader.IsDBNull(2) ? null : reader.GetString(2),
                Notes: reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return rows;
    }

    // -------------------------------------------------------------------------
    // Add a new member to a tenant by wa_id.
    // Creates users_profile + membership + DM context + context_access atomically.
    // Fully idempotent: safe to call multiple times for the same wa_id.
    // -------------------------------------------------------------------------

    public async Task<AddMemberResult> AddMemberToTenantAsync(
        Guid tenantId,
        string waId,
        string phone,
        string role,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // 1. Upsert user profile
            await using (var cmd = new NpgsqlCommand(
                """
                insert into users_profile (id, external_auth_id, phone)
                values (gen_random_uuid(), @waid, @phone)
                on conflict (external_auth_id) do nothing;
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("waid", waId);
                cmd.Parameters.AddWithValue("phone", phone);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            Guid userId;
            await using (var cmd = new NpgsqlCommand(
                "select id from users_profile where external_auth_id = @waid;",
                conn, tx))
            {
                cmd.Parameters.AddWithValue("waid", waId);
                userId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
            }

            // 2. Upsert membership
            bool isNew;
            await using (var cmd = new NpgsqlCommand(
                """
                insert into memberships (tenant_id, user_id, role)
                values (@tenant, @user, @role)
                on conflict (tenant_id, user_id) do update set role = excluded.role
                returning (xmax = 0) as is_new;
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("tenant", tenantId);
                cmd.Parameters.AddWithValue("user", userId);
                cmd.Parameters.AddWithValue("role", role);
                isNew = (bool)(await cmd.ExecuteScalarAsync(ct))!;
            }

            // 3. Upsert DM context
            await using (var cmd = new NpgsqlCommand(
                """
                insert into contexts (id, tenant_id, context_type, external_context_id, title)
                values (gen_random_uuid(), @tenant, 'DM', @waid, 'DM')
                on conflict (tenant_id, context_type, external_context_id) do nothing;
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("tenant", tenantId);
                cmd.Parameters.AddWithValue("waid", waId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            Guid contextId;
            await using (var cmd = new NpgsqlCommand(
                """
                select id from contexts
                where tenant_id = @tenant and context_type = 'DM' and external_context_id = @waid;
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("tenant", tenantId);
                cmd.Parameters.AddWithValue("waid", waId);
                contextId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
            }

            // 4. Upsert context_access
            await using (var cmd = new NpgsqlCommand(
                """
                insert into context_access (context_id, user_id, access_role)
                values (@ctx, @user, 'OWNER')
                on conflict (context_id, user_id) do nothing;
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("ctx", contextId);
                cmd.Parameters.AddWithValue("user", userId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return new AddMemberResult(userId, tenantId, waId, role, isNew);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}

public sealed record MemberAssignmentResult(
    string ClientExternalId,
    string? AssignedToWaId,
    Guid? AssignedToUserId);

public sealed record MemberAssignmentRow(
    string ClientExternalId,
    string? AssignedToWaId,
    string? Phone,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record AddMemberResult(
    Guid UserId,
    Guid TenantId,
    string WaId,
    string Role,
    bool IsNew);
