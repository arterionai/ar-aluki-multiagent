using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Capture.Dispatch;

/// <summary>No-op dispatcher for environments where domain agent dispatch is not configured.</summary>
public sealed class NullMessageDispatcher : IMessageDispatcher
{
    public Task<DispatchResult> DispatchAsync(UnifiedMessage message, PrincipalContext principal, CancellationToken ct)
        => Task.FromResult(new DispatchResult(DispatchOutcome.NoAgents, null, true, "null_dispatcher", Guid.Empty));
}
