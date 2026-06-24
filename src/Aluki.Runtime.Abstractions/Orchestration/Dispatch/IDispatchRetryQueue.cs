using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Abstractions.Orchestration.Dispatch;

public interface IDispatchRetryQueue
{
    Task EnqueueAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        string failedAgentId,
        string errorCode,
        CancellationToken ct);

    Task<IReadOnlyList<DispatchRetryEntry>> ClaimDueAsync(int limit, CancellationToken ct);

    Task MarkSucceededAsync(Guid retryId, CancellationToken ct);

    Task MarkFailedAsync(Guid retryId, string error, bool abandon, CancellationToken ct);
}

public sealed record DispatchRetryEntry(
    Guid RetryId,
    UnifiedMessage Message,
    PrincipalContext Principal,
    string FailedAgentId,
    int AttemptCount);
