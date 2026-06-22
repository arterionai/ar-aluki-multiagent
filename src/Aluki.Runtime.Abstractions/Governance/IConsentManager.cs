namespace Aluki.Runtime.Abstractions.Governance;

public interface IConsentManager
{
    Task<ConsentRecord> GrantAsync(GrantConsentRequest request, CancellationToken ct);

    Task<bool> RevokeAsync(RevokeConsentRequest request, CancellationToken ct);

    Task<bool> CheckAsync(Guid tenantId, Guid grantorId, Guid granteeId, string consentType, CancellationToken ct);

    Task<IReadOnlyList<ConsentRecord>> ListGrantedByAsync(Guid tenantId, Guid grantorId, CancellationToken ct);

    Task<IReadOnlyList<ConsentRecord>> ListGrantedToAsync(Guid tenantId, Guid granteeId, CancellationToken ct);
}
