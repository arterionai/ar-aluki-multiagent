using Aluki.Runtime.Abstractions.Governance;

namespace Aluki.Runtime.Host.Skills.Governance;

public sealed class ConsentManager : IConsentManager
{
    private readonly IGovernanceRepository _repo;

    public ConsentManager(IGovernanceRepository repo) => _repo = repo;

    public async Task<ConsentRecord> GrantAsync(GrantConsentRequest request, CancellationToken ct)
    {
        var existing = await _repo.GetActiveConsentAsync(
            request.TenantId, request.GrantorId, request.GranteeId, request.ConsentType, ct);
        if (existing is not null) return existing;
        return await _repo.InsertConsentAsync(request, ct);
    }

    public Task<bool> RevokeAsync(RevokeConsentRequest request, CancellationToken ct)
        => _repo.RevokeConsentAsync(request.TenantId, request.GrantorId, request.GranteeId, request.ConsentType, request.Reason, ct);

    public async Task<bool> CheckAsync(Guid tenantId, Guid grantorId, Guid granteeId, string consentType, CancellationToken ct)
    {
        var record = await _repo.GetActiveConsentAsync(tenantId, grantorId, granteeId, consentType, ct);
        return record is not null;
    }

    public Task<IReadOnlyList<ConsentRecord>> ListGrantedByAsync(Guid tenantId, Guid grantorId, CancellationToken ct)
        => _repo.ListConsentsByGrantorAsync(tenantId, grantorId, ct);

    public Task<IReadOnlyList<ConsentRecord>> ListGrantedToAsync(Guid tenantId, Guid granteeId, CancellationToken ct)
        => _repo.ListConsentsByGranteeAsync(tenantId, granteeId, ct);
}
