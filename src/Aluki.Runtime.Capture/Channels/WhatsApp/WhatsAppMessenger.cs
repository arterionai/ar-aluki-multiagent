using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Capture.Channels.WhatsApp;

/// <summary>
/// Sends WhatsApp delivery feedback to the sender: a read receipt (the blue
/// double-check) and a typing indicator (the "…" bubble, shown up to ~25s or
/// until a message is sent). Both are delivered in a single Graph API call.
/// </summary>
public interface IWhatsAppMessenger
{
    Task MarkReadAndShowTypingAsync(string phoneNumberId, string messageId, CancellationToken cancellationToken);
    Task SendTextMessageAsync(string phoneNumberId, string recipientWaId, string text, CancellationToken cancellationToken);
}

/// <summary>
/// Meta Graph API implementation. POSTs to <c>/{phone-number-id}/messages</c>
/// with <c>status=read</c> + <c>typing_indicator</c> so the sender sees blue ticks
/// and a typing bubble. Uses the same bearer token as media download
/// (<c>Meta:AccessToken</c>, <c>Meta:GraphBaseUrl</c>).
/// </summary>
public sealed class MetaWhatsAppMessenger : IWhatsAppMessenger
{
    private readonly HttpClient _http;
    private readonly ILogger<MetaWhatsAppMessenger> _logger;
    private readonly string _accessToken;
    private readonly string _graphBaseUrl;

    public MetaWhatsAppMessenger(HttpClient http, IConfiguration configuration, ILogger<MetaWhatsAppMessenger> logger)
    {
        _http = http;
        _logger = logger;
        _accessToken = configuration["Meta:AccessToken"] ?? string.Empty;
        _graphBaseUrl = (configuration["Meta:GraphBaseUrl"] ?? "https://graph.facebook.com/v21.0").TrimEnd('/');
    }

    public async Task MarkReadAndShowTypingAsync(string phoneNumberId, string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_accessToken) || string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        // Single call: marks the inbound message read (blue ticks) and shows the
        // typing indicator to the sender.
        var payload = JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            status = "read",
            message_id = messageId,
            typing_indicator = new { type = "text" }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_graphBaseUrl}/{phoneNumberId}/messages")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "WhatsApp read/typing indicator failed. status={Status} message_id={MessageId} detail={Detail}",
                (int)response.StatusCode, messageId, detail);
        }
    }

    public async Task SendTextMessageAsync(string phoneNumberId, string recipientWaId, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_accessToken) || string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(recipientWaId))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            to = recipientWaId,
            type = "text",
            text = new { body = text }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_graphBaseUrl}/{phoneNumberId}/messages")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "WhatsApp send text failed. status={Status} recipient={Recipient} detail={Detail}",
                (int)response.StatusCode, recipientWaId, detail);
            throw new InvalidOperationException(
                $"WhatsApp send text failed with status {(int)response.StatusCode}: {detail}");
        }
    }
}

/// <summary>No-op messenger used when outbound feedback is not configured (Host, tests).</summary>
public sealed class NullWhatsAppMessenger : IWhatsAppMessenger
{
    public Task MarkReadAndShowTypingAsync(string phoneNumberId, string messageId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SendTextMessageAsync(string phoneNumberId, string recipientWaId, string text, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
