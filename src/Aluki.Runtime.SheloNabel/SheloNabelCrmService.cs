using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Host.Skills.Tenancy;
using Npgsql;

namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Sheló NABEL–specific CRM queries:
/// - Sales / reorder report (reminders created in the org tenant)
/// - Pending reorder campaign items (due reminders with whatsapp channel)
///
/// Generic tenancy operations (channel routing, sub-tenants, member assignment)
/// are in <see cref="TenancyRepository"/> and available to any org tenant.
/// </summary>
public sealed class SheloNabelCrmService
{
    private readonly NpgsqlConnectionFactory _db;
    public readonly TenancyRepository Tenancy;

    public SheloNabelCrmService(NpgsqlConnectionFactory db, TenancyRepository tenancy)
    {
        _db = db;
        Tenancy = tenancy;
    }

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

// Re-export from TenancyRepository so callers inside this namespace don't need
// an extra using for the generic type.
public sealed record AssignmentResult(
    string ClientExternalId,
    string? AssignedToWaId,
    Guid? AssignedToUserId);

public sealed record AddMemberOutcome(
    bool Success,
    string WaId,
    string Role,
    bool IsNew,
    string? ErrorMessage = null);
