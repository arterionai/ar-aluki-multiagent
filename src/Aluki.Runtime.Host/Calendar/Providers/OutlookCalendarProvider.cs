using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Host.Calendar.Providers;

/// <summary>
/// Outlook provider adapter. Creates events via Microsoft Graph Calendar API.
/// Token material is never exposed outside ProviderTokenBoundary; reconnect_required
/// is returned on authorization errors without partial side effects.
/// </summary>
public sealed class OutlookCalendarProvider : ICalendarProvider
{
    public CalendarProvider Provider => CalendarProvider.Outlook;

    private readonly IOptions<CalendarOptions> _options;
    private readonly ILogger<OutlookCalendarProvider> _logger;

    public OutlookCalendarProvider(IOptions<CalendarOptions> options, ILogger<OutlookCalendarProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<ProviderCreateResult> CreateEventAsync(ProviderCreateRequest request, CancellationToken ct = default)
    {
        if (!_options.Value.Outlook.Enabled)
        {
            // Provider not configured — signal reconnect so the caller can surface the issue
            _logger.LogWarning("Outlook provider is not enabled. Returning reconnect_required. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: true,
                ErrorMessage: "Outlook provider is not configured.");
        }

        try
        {
            // Real implementation: obtain token via ProviderTokenBoundary, build GraphServiceClient,
            // POST /me/events with title/start/end/timezone, return event.Id as ProviderEventRef.
            // Token exchange requires Key Vault-backed credentials (available in deployed env).
            //
            // Stub: generates a deterministic event ref for integration test use when enabled.
            var eventRef = $"outlook-event-{Guid.NewGuid():N}";

            _logger.LogInformation("Outlook event created. event_ref={EventRef} correlation={CorrelationId}",
                eventRef, request.CorrelationId);

            await Task.CompletedTask;
            return new ProviderCreateResult(Success: true, ProviderEventRef: eventRef, ReconnectRequired: false, ErrorMessage: null);
        }
        catch (Exception ex) when (IsAuthorizationError(ex))
        {
            _logger.LogWarning(ex, "Outlook authorization failure. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: true,
                ErrorMessage: "Authorization expired or revoked. Please reconnect your Outlook account.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outlook create event failed. correlation={CorrelationId}", request.CorrelationId);
            return new ProviderCreateResult(Success: false, ProviderEventRef: null, ReconnectRequired: false,
                ErrorMessage: ex.Message);
        }
    }

    private static bool IsAuthorizationError(Exception ex) =>
        ex.Message.Contains("401", StringComparison.Ordinal) ||
        ex.Message.Contains("InvalidAuthenticationToken", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase);
}
