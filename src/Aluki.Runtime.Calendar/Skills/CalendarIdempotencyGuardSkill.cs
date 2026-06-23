using System.Security.Cryptography;
using System.Text;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Calendar.Skills;

public sealed class CalendarIdempotencyGuardSkill
{
    private readonly IDeduplicationRepository _dedup;
    private readonly CalendarOptions _options;

    public CalendarIdempotencyGuardSkill(IDeduplicationRepository dedup, IOptions<CalendarOptions> options)
    {
        _dedup = dedup;
        _options = options.Value;
    }

    public string ComputeIdempotencyKey(
        Guid tenantId, Guid userId, CalendarProvider provider,
        string? title, string? startLocal, string? canonicalTimezone) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{tenantId}|{userId}|{provider}|{title?.ToLowerInvariant()}|{startLocal?.ToLowerInvariant()}|{canonicalTimezone?.ToLowerInvariant()}"
        ))).ToLowerInvariant();

    public async Task<IdempotencyCheckResult> CheckAsync(
        Guid tenantId, Guid contextId, Guid userId,
        CalendarProvider provider, string idempotencyKey,
        CancellationToken ct = default)
    {
        var existing = await _dedup.GetActiveAsync(tenantId, contextId, userId, provider, idempotencyKey, ct);
        if (existing is not null)
            return IdempotencyCheckResult.Duplicate(existing);

        return IdempotencyCheckResult.New;
    }

    public async Task<DeduplicationRecord> BeginAsync(
        Guid tenantId, Guid contextId, Guid userId,
        CalendarProvider provider, string idempotencyKey,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new DeduplicationRecord(
            DeduplicationRecordId: Guid.NewGuid(),
            TenantId: tenantId,
            ContextId: contextId,
            UserId: userId,
            Provider: provider,
            IdempotencyKey: idempotencyKey,
            WindowStartedAtUtc: now,
            WindowExpiresAtUtc: now.AddMinutes(_options.DeduplicationWindowMinutes),
            FirstOutcomeRef: Guid.NewGuid().ToString("N"),
            FirstProviderEventRef: null,
            Status: DeduplicationStatus.InProgress);

        await _dedup.CreateAsync(record, ct);
        return record;
    }

    public Task CompleteAsync(Guid dedupId, string providerEventRef, CancellationToken ct = default) =>
        _dedup.UpdateStatusAsync(dedupId, DeduplicationStatus.Created, providerEventRef, ct);

    public Task FailAsync(Guid dedupId, CancellationToken ct = default) =>
        _dedup.UpdateStatusAsync(dedupId, DeduplicationStatus.Failed, null, ct);
}

public sealed class IdempotencyCheckResult
{
    public static readonly IdempotencyCheckResult New = new() { IsDuplicate = false };

    public bool IsDuplicate { get; private init; }
    public DeduplicationRecord? Existing { get; private init; }

    public static IdempotencyCheckResult Duplicate(DeduplicationRecord record) => new()
    {
        IsDuplicate = true,
        Existing = record
    };
}
