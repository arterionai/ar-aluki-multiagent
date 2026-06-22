namespace Aluki.Runtime.Abstractions.Skills.Calendar;

public interface ICalendarProvider
{
    CalendarProvider Provider { get; }
    Task<ProviderCreateResult> CreateEventAsync(ProviderCreateRequest request, CancellationToken ct = default);
}

public sealed record ProviderCreateRequest(
    string Title,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Timezone,
    string ProviderAccountRef,
    string CorrelationId);

public sealed record ProviderCreateResult(
    bool Success,
    string? ProviderEventRef,
    bool ReconnectRequired,
    string? ErrorMessage);
