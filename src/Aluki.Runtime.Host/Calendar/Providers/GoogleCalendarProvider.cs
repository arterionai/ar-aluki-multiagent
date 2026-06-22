using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Host.Calendar.Providers;

/// <summary>
/// Google calendar provider adapter. Creates events via the Google Calendar API v3
/// (<c>POST /calendars/primary/events</c>) using the connection's stored OAuth token,
/// refreshed on demand. Behavior mirrors <see cref="OutlookCalendarProvider"/>:
/// reconnect_required on authorization failures, no partial side effects, token
/// material never exposed.
/// </summary>
public sealed class GoogleCalendarProvider : ICalendarProvider
{
    public CalendarProvider Provider => CalendarProvider.Google;

    private readonly HttpClient _http;
    private readonly ICalendarTokenService _tokenService;
    private readonly IOptions<CalendarOptions> _options;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(
        HttpClient http,
        ICalendarTokenService tokenService,
        IOptions<CalendarOptions> options,
        ILogger<GoogleCalendarProvider> logger)
    {
        _http = http;
        _tokenService = tokenService;
        _options = options;
        _logger = logger;
    }

    public async Task<ProviderCreateResult> CreateEventAsync(ProviderCreateRequest request, CancellationToken ct = default)
    {
        if (!_options.Value.Google.Enabled)
        {
            _logger.LogWarning("Google provider is not enabled. Returning reconnect_required. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(false, null, ReconnectRequired: true, "Google provider is not configured.");
        }

        var token = await _tokenService.GetValidAccessTokenAsync(
            request.TenantId, request.ContextId, request.UserId, Provider, ct);
        if (token is null)
            return new ProviderCreateResult(false, null, ReconnectRequired: true,
                "Authorization expired or revoked. Please reconnect your Google account.");

        // RFC3339 UTC instants are unambiguous and accepted by the Calendar API.
        var payload = new
        {
            summary = request.Title,
            start = new { dateTime = request.StartUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'") },
            end = new { dateTime = request.EndUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'") },
        };

        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post,
                "https://www.googleapis.com/calendar/v3/calendars/primary/events")
            {
                Content = JsonContent.Create(payload),
            };
            msg.Headers.Authorization = new("Bearer", token.Unwrap());

            using var resp = await _http.SendAsync(msg, ct);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Google API returned {Status}; reconnect required. correlation={CorrelationId}", (int)resp.StatusCode, request.CorrelationId);
                return new ProviderCreateResult(false, null, ReconnectRequired: true,
                    "Authorization expired or revoked. Please reconnect your Google account.");
            }

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Google create event failed: {Status}. correlation={CorrelationId}", (int)resp.StatusCode, request.CorrelationId);
                return new ProviderCreateResult(false, null, ReconnectRequired: false, $"Google Calendar error {(int)resp.StatusCode}.");
            }

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var eventRef = body.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (string.IsNullOrEmpty(eventRef))
                return new ProviderCreateResult(false, null, ReconnectRequired: false, "Google response missing event id.");

            _logger.LogInformation("Google event created. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(true, eventRef, ReconnectRequired: false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google create event failed. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(false, null, ReconnectRequired: false, ex.Message);
        }
    }
}
