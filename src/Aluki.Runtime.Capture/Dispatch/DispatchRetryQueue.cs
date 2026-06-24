using System.Text.Json;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;

namespace Aluki.Runtime.Capture.Dispatch;

public sealed class DispatchRetryQueue : IDispatchRetryQueue
{
    // Backoff per attempt number (1-based): 1 min → 5 min → abandon on 3rd failure.
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlConnectionFactory _db;
    private readonly ILogger<DispatchRetryQueue> _logger;

    public DispatchRetryQueue(NpgsqlConnectionFactory db, ILogger<DispatchRetryQueue> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnqueueAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        string failedAgentId,
        string errorCode,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dispatch_retry_queue
                (tenant_id, context_id, user_id, correlation_id,
                 unified_message, principal, failed_agent_id, error_code)
            VALUES
                (@tenant_id, @context_id, @user_id, @correlation_id,
                 @unified_message::jsonb, @principal::jsonb, @failed_agent_id, @error_code)
            """;
        cmd.Parameters.AddWithValue("@tenant_id", NpgsqlDbType.Uuid, principal.TenantId);
        cmd.Parameters.AddWithValue("@context_id", NpgsqlDbType.Uuid, principal.ContextId);
        cmd.Parameters.AddWithValue("@user_id", NpgsqlDbType.Uuid, principal.UserId);
        cmd.Parameters.AddWithValue("@correlation_id", NpgsqlDbType.Text, principal.CorrelationId);
        cmd.Parameters.AddWithValue("@unified_message", NpgsqlDbType.Text,
            JsonSerializer.Serialize(message, JsonOpts));
        cmd.Parameters.AddWithValue("@principal", NpgsqlDbType.Text,
            JsonSerializer.Serialize(principal, JsonOpts));
        cmd.Parameters.AddWithValue("@failed_agent_id", NpgsqlDbType.Text, failedAgentId);
        cmd.Parameters.AddWithValue("@error_code", NpgsqlDbType.Text, errorCode);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation(
            "dispatch_retry.enqueued agent_id={AgentId} correlation_id={CorrelationId}",
            failedAgentId, principal.CorrelationId);
    }

    public async Task<IReadOnlyList<DispatchRetryEntry>> ClaimDueAsync(int limit, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM app.claim_due_dispatch_retries(@limit, @now)";
        cmd.Parameters.AddWithValue("@limit", NpgsqlDbType.Integer, limit);
        cmd.Parameters.AddWithValue("@now", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<DispatchRetryEntry>();

        while (await reader.ReadAsync(ct))
        {
            try
            {
                var retryId = reader.GetGuid(0);
                var messageJson = reader.GetString(5);
                var principalJson = reader.GetString(6);
                var failedAgentId = reader.GetString(7);
                var attemptCount = reader.GetInt32(8);

                var message = JsonSerializer.Deserialize<UnifiedMessage>(messageJson, JsonOpts)!;
                var principal = JsonSerializer.Deserialize<PrincipalContext>(principalJson, JsonOpts)!;

                results.Add(new DispatchRetryEntry(retryId, message, principal, failedAgentId, attemptCount));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "dispatch_retry.deserialize_failed — skipping row");
            }
        }

        return results;
    }

    public async Task MarkSucceededAsync(Guid retryId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE dispatch_retry_queue
            SET status = 'succeeded', updated_at_utc = now()
            WHERE retry_id = @retry_id
            """;
        cmd.Parameters.AddWithValue("@retry_id", NpgsqlDbType.Uuid, retryId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(Guid retryId, string error, bool abandon, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        // Re-fetch attempt_count to compute the right backoff index.
        cmd.CommandText = """
            UPDATE dispatch_retry_queue
            SET status         = CASE WHEN @abandon THEN 'abandoned' ELSE 'pending' END,
                last_error     = @error,
                next_retry_utc = CASE WHEN @abandon THEN next_retry_utc
                                      ELSE now() + (@backoff_seconds * interval '1 second')
                                 END,
                updated_at_utc = now()
            WHERE retry_id = @retry_id
            """;

        // Use attempt_count (already incremented by claim) to pick the next backoff.
        // Index 0 = after 1st attempt (wait 1 min), index 1 = after 2nd (wait 5 min).
        var backoffSeconds = (int)Backoff[Math.Min(1, Backoff.Length - 1)].TotalSeconds;

        cmd.Parameters.AddWithValue("@retry_id", NpgsqlDbType.Uuid, retryId);
        cmd.Parameters.AddWithValue("@abandon", NpgsqlDbType.Boolean, abandon);
        cmd.Parameters.AddWithValue("@error", NpgsqlDbType.Text, error);
        cmd.Parameters.AddWithValue("@backoff_seconds", NpgsqlDbType.Integer, backoffSeconds);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
