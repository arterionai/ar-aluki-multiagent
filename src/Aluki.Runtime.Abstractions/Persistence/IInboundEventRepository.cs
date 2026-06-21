namespace Aluki.Runtime.Abstractions.Persistence;

/// <summary>
/// Persists raw provider envelope intake records. Side effect: insert only.
/// All operations execute under an active tenant/context session scope.
/// </summary>
public interface IInboundEventRepository
{
    Task<Guid> InsertAsync(InboundMessageEventRow row, CancellationToken cancellationToken);
}
