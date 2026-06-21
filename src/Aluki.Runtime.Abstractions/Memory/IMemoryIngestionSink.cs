namespace Aluki.Runtime.Abstractions.Memory;

/// <summary>
/// A captured message promoted into personal memory so it becomes recall-able.
/// Carries only the scope and content the memory store needs; the sink owns
/// embedding and persistence.
/// </summary>
public sealed record MemoryIngestionItem(
    Guid TenantId,
    Guid ContextId,
    Guid UserId,
    string SourceChannel,
    string SourceIdentity,
    string ContentText,
    string ProvenanceRef,
    string CorrelationId);

/// <summary>
/// Bridge from capture to personal memory (SB-001 → SB-002). Capture invokes this
/// best-effort after a message is durably persisted; implementations must be
/// idempotent (repeat deliveries of the same source identity are suppressed) and
/// must never throw back into the capture path.
/// </summary>
public interface IMemoryIngestionSink
{
    Task IngestAsync(MemoryIngestionItem item, CancellationToken cancellationToken);
}
