namespace Aluki.Runtime.Abstractions.Governance;

public interface IGovernanceRepository
{
    Task<IReadOnlyList<PolicyRule>> GetActiveRulesAsync(Guid tenantId, string operationType, CancellationToken ct);

    Task<PolicyRule> CreateRuleAsync(CreatePolicyRuleRequest request, CancellationToken ct);

    Task<IReadOnlyList<PolicyRule>> ListRulesAsync(Guid tenantId, CancellationToken ct);

    Task<Guid> AppendDecisionAsync(
        Guid tenantId, Guid? principalUserId, string operationType,
        string decision, string reasonCode, IReadOnlyList<string> appliedRules,
        decimal? estimatedCost, string? correlationId, object? metadata,
        CancellationToken ct);

    Task<ConsentRecord?> GetActiveConsentAsync(Guid tenantId, Guid grantorId, Guid granteeId, string consentType, CancellationToken ct);

    Task<ConsentRecord> InsertConsentAsync(GrantConsentRequest request, CancellationToken ct);

    Task<bool> RevokeConsentAsync(Guid tenantId, Guid grantorId, Guid granteeId, string consentType, string? reason, CancellationToken ct);

    Task<IReadOnlyList<ConsentRecord>> ListConsentsByGrantorAsync(Guid tenantId, Guid grantorId, CancellationToken ct);

    Task<IReadOnlyList<ConsentRecord>> ListConsentsByGranteeAsync(Guid tenantId, Guid granteeId, CancellationToken ct);
}
