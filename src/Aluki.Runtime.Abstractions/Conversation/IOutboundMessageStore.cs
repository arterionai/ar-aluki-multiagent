namespace Aluki.Runtime.Abstractions.Conversation;

public interface IOutboundMessageStore
{
    /// <summary>
    /// Idempotent insert: returns true if newly persisted, false if already exists
    /// (same tenant_id + correlation_message_id).
    /// </summary>
    Task<bool> TryPersistAsync(OutboundMessage message, CancellationToken cancellationToken);
}
