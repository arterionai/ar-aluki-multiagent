using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Host.Calendar.Providers;

/// <summary>
/// Outlook calendar provider adapter. Creates events via Microsoft Graph
/// (<c>POST /me/events</c>) using the connection's stored OAuth token, refreshed on
/// demand. Authorization failures surface as <c>reconnect_required</c> with no partial
/// side effects; token material is never exposed outside <c>ProviderTokenBoundary</c>.
/// </summary>
public sealed class OutlookCalendarProvider : ICalendarProvider
{
    public CalendarProvider Provider => CalendarProvider.Outlook;

    private readonly HttpClient _http;
    private readonly ICalendarTokenService _tokenService;
    private readonly IOptions<CalendarOptions> _options;
    private readonly ILogger<OutlookCalendarProvider> _logger;

    public OutlookCalendarProvider(
        HttpClient http,
        ICalendarTokenService tokenService,
        IOptions<CalendarOptions> options,
        ILogger<OutlookCalendarProvider> logger)
    {
        _http = http;
        _tokenService = tokenService;
        _options = options;
        _logger = logger;
    }

    public async Task<ProviderCreateResult> CreateEventAsync(ProviderCreateRequest request, CancellationToken ct = default)
    {
        if (!_options.Value.Outlook.Enabled)
        {
            _logger.LogWarning("Outlook provider is not enabled. Returning reconnect_required. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(false, null, ReconnectRequired: true, "Outlook provider is not configured.");
        }

        var token = await _tokenService.GetValidAccessTokenAsync(
            request.TenantId, request.ContextId, request.UserId, Provider, ct);
        if (token is null)
            return new ProviderCreateResult(false, null, ReconnectRequired: true,
                "Authorization expired or revoked. Please reconnect your Outlook account.");

        // Send times in UTC so Graph stores an unambiguous instant regardless of DST.
        var payload = new
        {
            subject = request.Title,
            start = new { dateTime = request.StartUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
            end = new { dateTime = request.EndUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
        };

        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/events")
            {
                Content = JsonContent.Create(payload),
            };
            msg.Headers.Authorization = new("Bearer", token.Unwrap());

            using var resp = await _http.SendAsync(msg, ct);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Graph returned {Status}; reconnect required. correlation={CorrelationId}", (int)resp.StatusCode, request.CorrelationId);
                return new ProviderCreateResult(false, null, ReconnectRequired: true,
                    "Authorization expired or revoked. Please reconnect your Outlook account.");
            }

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Graph create event failed: {Status}. correlation={CorrelationId}", (int)resp.StatusCode, request.CorrelationId);
                return new ProviderCreateResult(false, null, ReconnectRequired: false, $"Graph error {(int)resp.StatusCode}.");
            }

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var eventRef = body.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (string.IsNullOrEmpty(eventRef))
                return new ProviderCreateResult(false, null, ReconnectRequired: false, "Graph response missing event id.");

            _logger.LogInformation("Outlook event created. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(true, eventRef, ReconnectRequired: false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outlook create event failed. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(false, null, ReconnectRequired: false, ex.Message);
        }
    }
}
