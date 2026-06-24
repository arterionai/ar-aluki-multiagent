using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Capture.Dispatch;

/// <summary>No-op retry queue for tests and environments that don't need it.</summary>
public sealed class NullDispatchRetryQueue : IDispatchRetryQueue
{
    public Task EnqueueAsync(UnifiedMessage message, PrincipalContext principal,
        string failedAgentId, string errorCode, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<DispatchRetryEntry>> ClaimDueAsync(int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DispatchRetryEntry>>([]);

    public Task MarkSucceededAsync(Guid retryId, CancellationToken ct) => Task.CompletedTask;

    public Task MarkFailedAsync(Guid retryId, string error, bool abandon, CancellationToken ct)
        => Task.CompletedTask;
}
