using Aluki.Runtime.Abstractions.Security;
using Microsoft.Extensions.Configuration;

namespace Aluki.Runtime.Host.Security;

/// <summary>
/// Enforces consent-stop (STOP/ALTO) state before capture side effects (FR-011).
/// The durable opt-out store is delivered by the governance/security feature
/// (spec 012); until then this gate honors a configuration-seeded block list at
/// <c>Capture:ConsentStop:BlockedSenders</c> so the policy is wired and testable.
/// </summary>
public sealed class ConsentStopPolicyService : IConsentStopPolicy
{
    private readonly HashSet<string> _blockedSenders;

    public ConsentStopPolicyService(IConfiguration configuration)
    {
        _blockedSenders = configuration
            .GetSection("Capture:ConsentStop:BlockedSenders")
            .Get<string[]>()?
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> IsStopActiveAsync(
        PrincipalContext principal,
        string senderExternalId,
        CancellationToken cancellationToken)
    {
        var active = _blockedSenders.Contains(senderExternalId);
        return Task.FromResult(active);
    }
}
