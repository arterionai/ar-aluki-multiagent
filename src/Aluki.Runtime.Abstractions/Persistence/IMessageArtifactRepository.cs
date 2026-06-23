namespace Aluki.Runtime.Abstractions.Persistence;

/// <summary>
/// Persists and reads canonical unified message artifacts. Side effect: insert.
/// All operations execute under an active tenant/context session scope.
/// </summary>
public interface IMessageArtifactRepository
{
    Task<Guid> InsertAsync(UnifiedMessageArtifactRow row, CancellationToken cancellationToken);

    Task<UnifiedMessageArtifactRow?> GetByIdAsync(Guid messageId, CancellationToken cancellationToken);

    Task<int> CountByProviderAsync(
        Guid tenantId,
        string sourceChannel,
        string providerMessageId,
        CancellationToken cancellationToken);
}
