using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Aluki.Runtime.E2ETests;

internal static class E2EHelpers
{
    internal static string BuildWebhookPayload(
        string waId, string phoneNumberId, string wamid, string text, long? timestamp = null)
    {
        timestamp ??= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var escapedText = JsonSerializer.Serialize(text)[1..^1];
        return
            "{\"object\":\"whatsapp_business_account\",\"entry\":[{\"id\":\"E2E\",\"changes\":[{\"field\":\"messages\",\"value\":{" +
            "\"messaging_product\":\"whatsapp\"," +
            $"\"metadata\":{{\"display_phone_number\":\"15550000000\",\"phone_number_id\":\"{phoneNumberId}\"}}," +
            $"\"contacts\":[{{\"profile\":{{\"name\":\"E2E Test User\"}},\"wa_id\":\"{waId}\"}}]," +
            $"\"messages\":[{{\"from\":\"{waId}\",\"id\":\"{wamid}\",\"timestamp\":\"{timestamp}\",\"type\":\"text\",\"text\":{{\"body\":\"{escapedText}\"}}}}]" +
            "}}]}]}";
    }

    internal static string ComputeSignature(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    internal static async Task<HttpStatusCode> SendWebhookAsync(
        HttpClient http,
        string? secret,
        string phoneNumberId,
        string waId,
        string wamid,
        string text,
        bool tamperSignature = false)
    {
        var payload = BuildWebhookPayload(waId, phoneNumberId, wamid, text);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp");
        req.Content = new ByteArrayContent(bytes);
        req.Content.Headers.ContentType = new("application/json");

        if (!string.IsNullOrEmpty(secret))
        {
            var sig = tamperSignature
                ? ComputeSignature("wrong-secret-intentionally", bytes)
                : ComputeSignature(secret, bytes);
            req.Headers.Add("x-hub-signature-256", sig);
        }

        var resp = await http.SendAsync(req);
        return resp.StatusCode;
    }

    // Poll app.outbound_messages for a reply matching the given pattern.
    // Returns the full body text, or null if timeout exceeded.
    internal static async Task<string?> WaitForOutboundAsync(
        NpgsqlDataSource db,
        string recipientWaId,
        string bodyContains,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = db.CreateCommand("""
                SELECT body FROM app.outbound_messages
                WHERE recipient_wa_id = $1
                  AND created_at > NOW() - INTERVAL '3 minutes'
                  AND body ILIKE $2
                ORDER BY created_at DESC LIMIT 1
                """);
            cmd.Parameters.AddWithValue(recipientWaId);
            cmd.Parameters.AddWithValue($"%{bodyContains}%");
            var result = await cmd.ExecuteScalarAsync();
            if (result is string body) return body;
            await Task.Delay(500);
        }
        return null;
    }

    // Poll dispatch_audit_events for the agent selected for a specific wamid.
    internal static async Task<string?> WaitForAuditAsync(
        NpgsqlDataSource db,
        string wamid,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = db.CreateCommand("""
                SELECT dae.selected_agent_id
                FROM app.dispatch_audit_events dae
                JOIN app.unified_message_artifact uma
                  ON uma.unified_message_id = dae.unified_message_id
                WHERE uma.provider_message_id = $1
                LIMIT 1
                """);
            cmd.Parameters.AddWithValue(wamid);
            var result = await cmd.ExecuteScalarAsync();
            if (result is string agentId) return agentId;
            await Task.Delay(500);
        }
        return null;
    }

    // Check if any outbound message exists for the recipient in the last N minutes with any body.
    internal static async Task<string?> WaitForAnyOutboundAsync(
        NpgsqlDataSource db,
        string recipientWaId,
        DateTimeOffset after,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(45));
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = db.CreateCommand("""
                SELECT body FROM app.outbound_messages
                WHERE recipient_wa_id = $1
                  AND created_at > $2
                ORDER BY created_at DESC LIMIT 1
                """);
            cmd.Parameters.AddWithValue(recipientWaId);
            cmd.Parameters.AddWithValue(after.UtcDateTime);
            var result = await cmd.ExecuteScalarAsync();
            if (result is string body) return body;
            await Task.Delay(500);
        }
        return null;
    }

    internal static string NewWamid() => $"wamid.e2e.{Guid.NewGuid():N}";
}
