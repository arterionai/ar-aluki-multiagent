namespace Aluki.Runtime.Abstractions.Persistence;

/// <summary>
/// Canonical deduplication store keyed on
/// <c>(tenant_id, source_channel, provider_message_id)</c>.
/// </summary>
public interface IIdempotencyRepository
{
    /// <summary>
    /// Atomically claims the canonical idempotency key. Returns
    /// <see cref="IdempotencyClaimResult.IsNew"/> = true on first delivery and
    /// false (with the existing canonical id) for duplicate redeliveries.
    /// </summary>
    Task<IdempotencyClaimResult> TryClaimAsync(
        Guid tenantId,
        string sourceChannel,
        string providerMessageId,
        CancellationToken cancellationToken);

    /// <summary>Links a claimed idempotency record to its canonical message.</summary>
    Task LinkCanonicalAsync(
        Guid idempotencyId,
        Guid canonicalMessageId,
        CancellationToken cancellationToken);
}
