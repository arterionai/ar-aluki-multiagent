namespace Aluki.Runtime.Abstractions.Persistence;

/// <summary>
/// Persists media artifact metadata linked to a canonical message. Side effect:
/// insert. Never written for duplicate-suppressed deliveries.
/// </summary>
public interface IMediaArtifactRepository
{
    Task<Guid> InsertAsync(MediaArtifactRow row, CancellationToken cancellationToken);

    Task<int> CountByMessageAsync(Guid messageId, CancellationToken cancellationToken);
}
