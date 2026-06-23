using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Conversation;

/// <summary>
/// Idempotent persistence of outbound messages to app.outbound_messages.
/// Uniqueness enforced by (tenant_id, correlation_message_id).
/// </summary>
public sealed class OutboundMessageStore : IOutboundMessageStore
{
    private readonly NpgsqlConnectionFactory _factory;

    public OutboundMessageStore(NpgsqlConnectionFactory factory) => _factory = factory;

    public async Task<bool> TryPersistAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);

        const string sql = """
            insert into app.outbound_messages (
                id, tenant_id, user_id, correlation_message_id,
                channel, recipient_wa_id, body, status, error_reason,
                created_at_utc, delivered_at_utc)
            values (
                @id, @tenant_id, @user_id, @correlation_message_id,
                @channel, @recipient_wa_id, @body, @status, @error_reason,
                @created_at_utc, @delivered_at_utc)
            on conflict (tenant_id, correlation_message_id) do nothing
            returning (xmax = 0) as is_new;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", message.Id);
        cmd.Parameters.AddWithValue("tenant_id", message.TenantId);
        cmd.Parameters.AddWithValue("user_id", message.UserId);
        cmd.Parameters.AddWithValue("correlation_message_id", message.CorrelationMessageId);
        cmd.Parameters.AddWithValue("channel", message.Channel);
        cmd.Parameters.AddWithValue("recipient_wa_id", message.RecipientWaId);
        cmd.Parameters.AddWithValue("body", message.Body);
        cmd.Parameters.AddWithValue("status", message.Status);
        cmd.Parameters.AddWithValue("error_reason", (object?)message.ErrorReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at_utc", message.CreatedAt);
        cmd.Parameters.AddWithValue("delivered_at_utc", (object?)message.DeliveredAt ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        // If ON CONFLICT suppressed the insert, ExecuteScalar returns null.
        return result is bool b && b;
    }
}
