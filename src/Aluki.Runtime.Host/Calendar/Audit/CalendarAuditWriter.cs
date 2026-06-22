using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Host.Calendar.Security;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Calendar.Audit;

public sealed class CalendarAuditWriter
{
    private readonly ICalendarAuditRepository _repository;
    private readonly ILogger<CalendarAuditWriter> _logger;

    public CalendarAuditWriter(ICalendarAuditRepository repository, ILogger<CalendarAuditWriter> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task WriteAsync(
        string eventName,
        Guid tenantId,
        Guid contextId,
        Guid? userId,
        CalendarProvider? provider,
        string skillName,
        string result,
        string? outcomeRef,
        string correlationId,
        object? payload = null,
        CancellationToken ct = default)
    {
        var payloadJson = TokenRedactionPolicy.SerializeRedacted(payload);
        var record = new CalendarAuditRecord(
            CalendarAuditEventId: Guid.NewGuid(),
            EventName: eventName,
            TenantId: tenantId,
            ContextId: contextId,
            UserId: userId,
            Provider: provider,
            SkillName: skillName,
            Result: result,
            OutcomeReference: outcomeRef,
            CorrelationId: correlationId,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            PayloadJson: payloadJson);

        try
        {
            await _repository.AppendAsync(record, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write calendar audit event. event={EventName} correlation={CorrelationId}", eventName, correlationId);
        }
    }
}
