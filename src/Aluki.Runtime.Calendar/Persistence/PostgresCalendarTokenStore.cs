using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Capture.Persistence;
using Npgsql;

namespace Aluki.Runtime.Calendar.Persistence;

/// <summary>
/// Persists encrypted provider OAuth tokens in <c>calendar_oauth_tokens</c>. Only
/// ciphertext is read/written here; encryption is the caller's responsibility.
/// </summary>
internal sealed class PostgresCalendarTokenStore : ICalendarTokenStore
{
    private readonly NpgsqlConnectionFactory _factory;

    public PostgresCalendarTokenStore(NpgsqlConnectionFactory factory) => _factory = factory;

    public async Task UpsertAsync(CalendarTokenRecord record, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into calendar_oauth_tokens (
                calendar_oauth_token_id, calendar_connection_id, tenant_id, context_id, user_id,
                provider, access_token_cipher, refresh_token_cipher, access_token_expires_at_utc,
                scope, token_type, correlation_id, updated_at_utc)
            values (
                @id, @connection_id, @tenant_id, @context_id, @user_id,
                @provider, @access_cipher, @refresh_cipher, @expires_at,
                @scope, @token_type, @correlation_id, now())
            on conflict (tenant_id, context_id, user_id, provider) do update set
                calendar_connection_id = excluded.calendar_connection_id,
                access_token_cipher = excluded.access_token_cipher,
                refresh_token_cipher = coalesce(excluded.refresh_token_cipher, calendar_oauth_tokens.refresh_token_cipher),
                access_token_expires_at_utc = excluded.access_token_expires_at_utc,
                scope = excluded.scope,
                token_type = excluded.token_type,
                correlation_id = excluded.correlation_id,
                updated_at_utc = now()
            """, conn);
        cmd.Parameters.AddWithValue("id", record.CalendarOAuthTokenId);
        cmd.Parameters.AddWithValue("connection_id", record.CalendarConnectionId);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId);
        cmd.Parameters.AddWithValue("context_id", record.ContextId);
        cmd.Parameters.AddWithValue("user_id", record.UserId);
        cmd.Parameters.AddWithValue("provider", record.Provider.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("access_cipher", record.AccessTokenCipher);
        cmd.Parameters.AddWithValue("refresh_cipher", (object?)record.RefreshTokenCipher ?? DBNull.Value);
        cmd.Parameters.AddWithValue("expires_at", record.AccessTokenExpiresAtUtc);
        cmd.Parameters.AddWithValue("scope", (object?)record.Scope ?? DBNull.Value);
        cmd.Parameters.AddWithValue("token_type", (object?)record.TokenType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlation_id", record.CorrelationId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<CalendarTokenRecord?> GetAsync(
        Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select calendar_oauth_token_id, calendar_connection_id, tenant_id, context_id, user_id,
                   provider, access_token_cipher, refresh_token_cipher, access_token_expires_at_utc,
                   scope, token_type, correlation_id
            from calendar_oauth_tokens
            where tenant_id = @tenant_id and context_id = @context_id
              and user_id = @user_id and provider = @provider
            limit 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("context_id", contextId);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("provider", provider.ToString().ToLowerInvariant());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new CalendarTokenRecord(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4),
            ParseProvider(reader.GetString(5)),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetString(11));
    }

    public async Task DeleteAsync(
        Guid tenantId, Guid contextId, Guid userId, CalendarProvider provider, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            delete from calendar_oauth_tokens
            where tenant_id = @tenant_id and context_id = @context_id
              and user_id = @user_id and provider = @provider
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("context_id", contextId);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("provider", provider.ToString().ToLowerInvariant());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static CalendarProvider ParseProvider(string v) => v switch
    {
        "outlook" => CalendarProvider.Outlook,
        "google" => CalendarProvider.Google,
        _ => throw new InvalidOperationException($"Unknown provider: {v}")
    };
}
