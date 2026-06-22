namespace Aluki.Runtime.Abstractions.Conversation;

public interface IConversationHistoryStore
{
    Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(
        Guid tenantId,
        Guid userId,
        int limit,
        CancellationToken cancellationToken);
}
