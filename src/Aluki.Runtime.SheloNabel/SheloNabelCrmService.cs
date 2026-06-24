using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Data access layer for Sheló NABEL CRM features:
/// - Sales report (reorder reminders created/delivered in the org tenant)
/// - Pending reorder campaign (due reminders with whatsapp delivery channel)
/// - Vendedora assignment (insert into vendedora_assignments)
/// </summary>
public sealed class SheloNabelCrmService
{
    private readonly NpgsqlConnectionFactory _db;

    public SheloNabelCrmService(NpgsqlConnectionFactory db) => _db = db;

    // -------------------------------------------------------------------------
    // Sales report: reorder reminders created in the last N days for a tenant.
    // -------------------------------------------------------------------------

    public async Task<SalesReportData> GetSalesReportAsync(
        Guid tenantId,
        int lookbackDays,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = new List<ReorderReminderRow>();
        await using (var cmd = new NpgsqlCommand(
            """
            select r.reminder_text,
                   r.status,
                   r.scheduled_time_utc,
                   r.created_at_utc,
                   r.delivery_channel,
                   up.phone
            from reminders r
            left join users_profile up on up.id = r.user_id
            where r.tenant_id = @tenant
              and r.created_at_utc >= now() - (@days * interval '1 day')
            order by r.created_at_utc desc
            limit 50;
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenant", tenantId);
            cmd.Parameters.AddWithValue("days", lookbackDays);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ReorderReminderRow(
                    Text: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    Status: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ScheduledUtc: reader.IsDBNull(2) ? DateTimeOffset.MinValue : reader.GetFieldValue<DateTimeOffset>(2),
                    CreatedUtc: reader.IsDBNull(3) ? DateTimeOffset.MinValue : reader.GetFieldValue<DateTimeOffset>(3),
                    DeliveryChannel: reader.IsDBNull(4) ? null : reader.GetString(4),
                    Phone: reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        var totalCreated = rows.Count;
        var delivered = rows.Count(r => r.Status == "delivered");
        var pending = rows.Count(r => r.Status is "scheduled" or "firing");
        var failed = rows.Count(r => r.Status is "delivery_failed" or "expired_undelivered");

        return new SalesReportData(totalCreated, delivered, pending, failed, rows);
    }

    // -------------------------------------------------------------------------
    // Campaign: past-due reorder reminders with a whatsapp delivery channel.
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<PendingReorderItem>> GetPendingReordersAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var items = new List<PendingReorderItem>();
        await using var cmd = new NpgsqlCommand(
            """
            select r.reminder_id,
                   r.reminder_text,
                   r.delivery_channel,
                   r.scheduled_time_utc
            from reminders r
            where r.tenant_id = @tenant
              and r.status = 'scheduled'
              and r.delivery_channel like 'whatsapp:%'
              and r.scheduled_time_utc <= now()
            order by r.scheduled_time_utc
            limit 100;
            """, conn);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var channel = reader.GetString(2);
            var parts = channel.Split(':');
            var phoneNumberId = parts.Length > 1 ? parts[1] : null;
            var waId = parts.Length > 2 ? parts[2] : null;

            items.Add(new PendingReorderItem(
                ReminderId: reader.GetGuid(0),
                ReminderText: reader.GetString(1),
                PhoneNumberId: phoneNumberId,
                WaId: waId,
                ScheduledUtc: reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return items;
    }

    // -------------------------------------------------------------------------
    // Vendedora assignment
    // -------------------------------------------------------------------------

    public async Task<AssignmentResult> AssignClientAsync(
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

        return new AssignmentResult(clientExternalId, assignedToWaId, assignedUserId);
    }

    // -------------------------------------------------------------------------
    // Channel registration: register a WA business phone_number_id → tenant
    // -------------------------------------------------------------------------

    public async Task RegisterChannelAsync(
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
}

public sealed record ReorderReminderRow(
    string Text,
    string Status,
    DateTimeOffset ScheduledUtc,
    DateTimeOffset CreatedUtc,
    string? DeliveryChannel,
    string? Phone);

public sealed record SalesReportData(
    int TotalCreated,
    int Delivered,
    int Pending,
    int Failed,
    IReadOnlyList<ReorderReminderRow> Rows);

public sealed record PendingReorderItem(
    Guid ReminderId,
    string ReminderText,
    string? PhoneNumberId,
    string? WaId,
    DateTimeOffset ScheduledUtc);

public sealed record AssignmentResult(
    string ClientExternalId,
    string? AssignedToWaId,
    Guid? AssignedToUserId);
