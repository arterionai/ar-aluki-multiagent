using System.Diagnostics;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Calendar.Observability;

public sealed class CalendarTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Aluki.Runtime.Calendar");

    private readonly ILogger<CalendarTelemetry> _logger;

    public CalendarTelemetry(ILogger<CalendarTelemetry> logger) => _logger = logger;

    public void RecordConnectAttempt(Guid tenantId, Guid userId, CalendarProvider provider)
        => _logger.LogInformation("Calendar connect attempt. tenant={TenantId} user={UserId} provider={Provider}",
            tenantId, userId, provider);

    public void RecordConnectOutcome(Guid tenantId, Guid userId, CalendarProvider provider, string result, long elapsedMs)
        => _logger.LogInformation("Calendar connect outcome. tenant={TenantId} user={UserId} provider={Provider} result={Result} elapsed_ms={ElapsedMs}",
            tenantId, userId, provider, result, elapsedMs);

    public void RecordCreateAttempt(Guid tenantId, Guid userId, CalendarProvider provider)
        => _logger.LogInformation("Calendar create attempt. tenant={TenantId} user={UserId} provider={Provider}",
            tenantId, userId, provider);

    public void RecordCreateOutcome(Guid tenantId, Guid userId, CalendarProvider? provider, string outcomeType, long elapsedMs)
        => _logger.LogInformation("Calendar create outcome. tenant={TenantId} user={UserId} provider={Provider} outcome={OutcomeType} elapsed_ms={ElapsedMs}",
            tenantId, userId, provider, outcomeType, elapsedMs);

    public void RecordScopeDenial(Guid tenantId, Guid userId, string denialCode)
        => _logger.LogWarning("Calendar scope denied. tenant={TenantId} user={UserId} denial_code={DenialCode}",
            tenantId, userId, denialCode);

    public void RecordAuthFailure(Guid tenantId, Guid userId, CalendarProvider provider, string failureReason)
        => _logger.LogWarning("Calendar auth failure. tenant={TenantId} user={UserId} provider={Provider} reason={FailureReason}",
            tenantId, userId, provider, failureReason);
}
