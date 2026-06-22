using Aluki.Runtime.Abstractions.Governance;
using Aluki.Runtime.Host.Skills.Governance;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class GovernanceContractTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _userId = Guid.NewGuid();

    // ── PolicyDecisionEngine ─────────────────────────────────────────────────

    [Fact]
    public async Task No_rules_results_in_default_allow()
    {
        var engine = BuildEngine([]);
        var request = new PolicyEvaluationRequest(_tenantId, _userId, "test.op");

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(PolicyDecisionKind.Allow, decision.Decision);
        Assert.Equal(PolicyReasonCode.DefaultAllow, decision.ReasonCode);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Feature_flag_disabled_results_in_deny()
    {
        var rule = MakeRule(PolicyRuleType.FeatureFlag, new Dictionary<string, object?> { ["enabled"] = false });
        var engine = BuildEngine([rule]);
        var request = new PolicyEvaluationRequest(_tenantId, _userId, "test.op");

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(PolicyDecisionKind.Deny, decision.Decision);
        Assert.Equal(PolicyReasonCode.FeatureDisabled, decision.ReasonCode);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Feature_flag_enabled_results_in_allow()
    {
        var rule = MakeRule(PolicyRuleType.FeatureFlag, new Dictionary<string, object?> { ["enabled"] = true });
        var engine = BuildEngine([rule]);
        var request = new PolicyEvaluationRequest(_tenantId, _userId, "test.op");

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(PolicyDecisionKind.Allow, decision.Decision);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Compliance_violation_results_in_deny()
    {
        var rule = MakeRule(PolicyRuleType.Compliance, new Dictionary<string, object?> { ["allowed"] = false });
        var engine = BuildEngine([rule]);
        var request = new PolicyEvaluationRequest(_tenantId, _userId, "test.op");

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(PolicyDecisionKind.Deny, decision.Decision);
        Assert.Equal(PolicyReasonCode.ComplianceViolation, decision.ReasonCode);
    }

    [Fact]
    public async Task Fraud_risk_exceeded_results_in_deny()
    {
        var rule = MakeRule(PolicyRuleType.FraudRisk, new Dictionary<string, object?> { ["max_risk_score"] = "0.5" });
        var engine = BuildEngine([rule]);
        var meta = new Dictionary<string, object?> { ["risk_score"] = "0.9" };
        var request = new PolicyEvaluationRequest(_tenantId, _userId, "test.op", meta);

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(PolicyDecisionKind.Deny, decision.Decision);
        Assert.Equal(PolicyReasonCode.FraudRiskExceeded, decision.ReasonCode);
    }

    [Fact]
    public async Task Fraud_risk_within_limit_results_in_allow()
    {
        var rule = MakeRule(PolicyRuleType.FraudRisk, new Dictionary<string, object?> { ["max_risk_score"] = "0.8" });
        var engine = BuildEngine([rule]);
        var meta = new Dictionary<string, object?> { ["risk_score"] = "0.3" };
        var request = new PolicyEvaluationRequest(_tenantId, _userId, "test.op", meta);

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(PolicyDecisionKind.Allow, decision.Decision);
    }

    [Fact]
    public async Task Deny_rule_wins_over_warn_rule()
    {
        var denyRule = MakeRule(PolicyRuleType.Compliance, new Dictionary<string, object?> { ["allowed"] = false }, priority: 10);
        var quotaRule = MakeRule(PolicyRuleType.Quota, new Dictionary<string, object?>(), priority: 20);
        var engine = BuildEngine([denyRule, quotaRule]);
        var request = new PolicyEvaluationRequest(_tenantId, _userId, "test.op");

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(PolicyDecisionKind.Deny, decision.Decision);
    }

    // ── ConsentManager ───────────────────────────────────────────────────────

    [Fact]
    public async Task Consent_check_returns_false_when_not_granted()
    {
        var repo = new StubGovernanceRepository();
        var manager = new ConsentManager(repo);

        var result = await manager.CheckAsync(_tenantId, Guid.NewGuid(), Guid.NewGuid(), ConsentType.ShareMemory, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Grant_then_check_returns_true()
    {
        var repo = new StubGovernanceRepository();
        var manager = new ConsentManager(repo);
        var grantorId = Guid.NewGuid();
        var granteeId = Guid.NewGuid();

        await manager.GrantAsync(new GrantConsentRequest(_tenantId, grantorId, granteeId, ConsentType.ShareMemory), CancellationToken.None);
        var result = await manager.CheckAsync(_tenantId, grantorId, granteeId, ConsentType.ShareMemory, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Revoke_removes_active_consent()
    {
        var repo = new StubGovernanceRepository();
        var manager = new ConsentManager(repo);
        var grantorId = Guid.NewGuid();
        var granteeId = Guid.NewGuid();

        await manager.GrantAsync(new GrantConsentRequest(_tenantId, grantorId, granteeId, ConsentType.ViewCalendar), CancellationToken.None);
        var revoked = await manager.RevokeAsync(new RevokeConsentRequest(_tenantId, grantorId, granteeId, ConsentType.ViewCalendar), CancellationToken.None);
        var stillGranted = await manager.CheckAsync(_tenantId, grantorId, granteeId, ConsentType.ViewCalendar, CancellationToken.None);

        Assert.True(revoked);
        Assert.False(stillGranted);
    }

    [Fact]
    public async Task Grant_is_idempotent_when_already_active()
    {
        var repo = new StubGovernanceRepository();
        var manager = new ConsentManager(repo);
        var grantorId = Guid.NewGuid();
        var granteeId = Guid.NewGuid();

        var first = await manager.GrantAsync(new GrantConsentRequest(_tenantId, grantorId, granteeId, ConsentType.ShareMemory), CancellationToken.None);
        var second = await manager.GrantAsync(new GrantConsentRequest(_tenantId, grantorId, granteeId, ConsentType.ShareMemory), CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IPolicyDecisionEngine BuildEngine(IReadOnlyList<PolicyRule> rules)
    {
        var repo = new StubGovernanceRepository(rules);
        return new PolicyDecisionEngine(repo, NullLogger<PolicyDecisionEngine>.Instance);
    }

    private static PolicyRule MakeRule(
        string ruleType,
        IReadOnlyDictionary<string, object?> def,
        int priority = 100)
        => new(
            Id: Guid.NewGuid(),
            TenantId: _tenantId,
            RuleType: ruleType,
            OperationType: "test.op",
            RuleDefinition: def,
            Priority: priority,
            IsActive: true,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}

// ── Stubs ─────────────────────────────────────────────────────────────────────

file sealed class StubGovernanceRepository : IGovernanceRepository
{
    private readonly List<PolicyRule> _rules;
    private readonly List<ConsentRecord> _consents = [];

    public StubGovernanceRepository(IReadOnlyList<PolicyRule>? rules = null)
        => _rules = rules is null ? [] : [.. rules];

    public Task<IReadOnlyList<PolicyRule>> GetActiveRulesAsync(Guid tenantId, string operationType, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PolicyRule>>(
            _rules.Where(r => r.TenantId == tenantId && r.OperationType == operationType && r.IsActive)
                  .OrderBy(r => r.Priority)
                  .ToList());

    public Task<PolicyRule> CreateRuleAsync(CreatePolicyRuleRequest request, CancellationToken ct)
    {
        var rule = new PolicyRule(Guid.NewGuid(), request.TenantId, request.RuleType, request.OperationType,
            request.RuleDefinition, request.Priority, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _rules.Add(rule);
        return Task.FromResult(rule);
    }

    public Task<IReadOnlyList<PolicyRule>> ListRulesAsync(Guid tenantId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PolicyRule>>(_rules.Where(r => r.TenantId == tenantId).ToList());

    public Task<Guid> AppendDecisionAsync(
        Guid tenantId, Guid? principalUserId, string operationType,
        string decision, string reasonCode, IReadOnlyList<string> appliedRules,
        decimal? estimatedCost, string? correlationId, object? metadata, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());

    public Task<ConsentRecord?> GetActiveConsentAsync(Guid tenantId, Guid grantorId, Guid granteeId, string consentType, CancellationToken ct)
    {
        var record = _consents.FirstOrDefault(c =>
            c.TenantId == tenantId && c.GrantorId == grantorId && c.GranteeId == granteeId &&
            c.ConsentType == consentType && c.IsActive);
        return Task.FromResult(record);
    }

    public Task<ConsentRecord> InsertConsentAsync(GrantConsentRequest request, CancellationToken ct)
    {
        var record = new ConsentRecord(Guid.NewGuid(), request.TenantId, request.GrantorId, request.GranteeId,
            request.ConsentType, DateTimeOffset.UtcNow, null, null);
        _consents.Add(record);
        return Task.FromResult(record);
    }

    public Task<bool> RevokeConsentAsync(Guid tenantId, Guid grantorId, Guid granteeId, string consentType, string? reason, CancellationToken ct)
    {
        var idx = _consents.FindIndex(c =>
            c.TenantId == tenantId && c.GrantorId == grantorId && c.GranteeId == granteeId &&
            c.ConsentType == consentType && c.IsActive);
        if (idx < 0) return Task.FromResult(false);
        var old = _consents[idx];
        _consents[idx] = old with { RevokedAtUtc = DateTimeOffset.UtcNow, RevocationReason = reason };
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ConsentRecord>> ListConsentsByGrantorAsync(Guid tenantId, Guid grantorId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConsentRecord>>(_consents.Where(c => c.TenantId == tenantId && c.GrantorId == grantorId).ToList());

    public Task<IReadOnlyList<ConsentRecord>> ListConsentsByGranteeAsync(Guid tenantId, Guid granteeId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConsentRecord>>(_consents.Where(c => c.TenantId == tenantId && c.GranteeId == granteeId).ToList());
}
