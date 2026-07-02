using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Aluki.Runtime.Conversation;

/// <summary>
/// Retrieves recent conversation turns for a user by UNIONing inbound messages from
/// app.unified_message_artifact with outbound messages from app.outbound_messages.
/// </summary>
public sealed class ConversationHistoryStore : IConversationHistoryStore
{
    private readonly NpgsqlConnectionFactory _factory;

    public ConversationHistoryStore(NpgsqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(
        Guid tenantId,
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);

        // Tenant/user isolation on this query is enforced by the explicit WHERE
        // filters below. (A previous set_config here used a mis-named GUC —
        // 'app.current_tenant_id' vs the RLS policies' 'app.current_tenant' — and
        // ran transaction-local outside a transaction, so it never had any effect;
        // it only cost a round-trip on the hot reply path.)
        const string sql = """
            select body, direction, created_at
            from (
                select
                    message_text   as body,
                    'inbound'      as direction,
                    created_at_utc as created_at
                from unified_message_artifact
                where tenant_id = @tenant_id
                  and created_by_user_id = @user_id
                  and message_text is not null

                union all

                select
                    body,
                    'outbound' as direction,
                    created_at_utc as created_at
                from app.outbound_messages
                where tenant_id = @tenant_id
                  and user_id = @user_id
            ) t
            order by created_at desc
            limit @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit });

        var turns = new List<ConversationTurn>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            turns.Add(new ConversationTurn(
                Body: reader.GetString(0),
                Direction: reader.GetString(1),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(2)));
        }

        // Return oldest-first so prompts are in chronological order.
        turns.Reverse();
        return turns;
    }
}
