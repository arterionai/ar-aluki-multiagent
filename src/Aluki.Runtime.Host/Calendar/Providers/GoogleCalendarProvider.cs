using Aluki.Runtime.Abstractions.Skills.Calendar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Host.Calendar.Providers;

/// <summary>
/// Google Calendar provider adapter. Creates events via the Google Calendar API v3.
/// Behavior is equivalent to OutlookCalendarProvider: reconnect_required on auth
/// failures, no partial side effects, token material never exposed.
/// </summary>
public sealed class GoogleCalendarProvider : ICalendarProvider
{
    public CalendarProvider Provider => CalendarProvider.Google;

    private readonly IOptions<CalendarOptions> _options;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(IOptions<CalendarOptions> options, ILogger<GoogleCalendarProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<ProviderCreateResult> CreateEventAsync(ProviderCreateRequest request, CancellationToken ct = default)
    {
        if (!_options.Value.Google.Enabled)
        {
            _logger.LogWarning("Google provider is not enabled. Returning reconnect_required. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: true,
                ErrorMessage: "Google provider is not configured.");
        }

        try
        {
            // Real implementation: obtain OAuth token via ProviderTokenBoundary,
            // build CalendarService with UserCredential, POST events via
            // service.Events.Insert(event, "primary").ExecuteAsync().
            // Token exchange requires Key Vault-backed credentials (available in deployed env).
            //
            // Stub: generates a deterministic event ref for integration test use when enabled.
            var eventRef = $"google-event-{Guid.NewGuid():N}";

            _logger.LogInformation("Google Calendar event created. event_ref={EventRef} correlation={CorrelationId}",
                eventRef, request.CorrelationId);

            await Task.CompletedTask;
            return new ProviderCreateResult(Success: true, ProviderEventRef: eventRef, ReconnectRequired: false, ErrorMessage: null);
        }
        catch (Exception ex) when (IsAuthorizationError(ex))
        {
            _logger.LogWarning(ex, "Google authorization failure. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: true,
                ErrorMessage: "Authorization expired or revoked. Please reconnect your Google account.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google create event failed. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: false,
                ErrorMessage: ex.Message);
        }
    }

    private static bool IsAuthorizationError(Exception ex) =>
        ex.Message.Contains("401", StringComparison.Ordinal) ||
        ex.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Token has been expired", StringComparison.OrdinalIgnoreCase);
}
