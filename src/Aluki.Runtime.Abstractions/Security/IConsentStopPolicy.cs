namespace Aluki.Runtime.Abstractions.Security;

/// <summary>
/// Enforces consent-stop (STOP/ALTO) state. When a stop is active for the
/// principal/context, capture side effects must not proceed.
/// </summary>
public interface IConsentStopPolicy
{
    Task<bool> IsStopActiveAsync(
        PrincipalContext principal,
        string senderExternalId,
        CancellationToken cancellationToken);
}
