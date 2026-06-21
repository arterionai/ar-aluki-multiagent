namespace Aluki.Runtime.Abstractions.Persistence;

/// <summary>
/// Persists immutable capture lifecycle audit events. Side effect: insert only.
/// </summary>
public interface IAuditEventRepository
{
    Task<Guid> InsertAsync(CaptureAuditEventRow row, CancellationToken cancellationToken);
}
