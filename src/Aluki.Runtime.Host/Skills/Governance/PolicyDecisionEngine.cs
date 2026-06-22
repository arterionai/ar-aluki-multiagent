using Aluki.Runtime.Abstractions.Governance;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.Governance;

public sealed class PolicyDecisionEngine : IPolicyDecisionEngine
{
    private readonly IGovernanceRepository _repo;
    private readonly ILogger<PolicyDecisionEngine> _logger;

    public PolicyDecisionEngine(IGovernanceRepository repo, ILogger<PolicyDecisionEngine> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken ct)
    {
        var rules = await _repo.GetActiveRulesAsync(request.TenantId, request.OperationType, ct);

        PolicyDecision decision;
        if (rules.Count == 0)
        {
            decision = new PolicyDecision(
                Decision: PolicyDecisionKind.Allow,
                ReasonCode: PolicyReasonCode.DefaultAllow,
                AppliedRules: []);
        }
        else
        {
            decision = EvaluateRules(rules, request);
        }

        await _repo.AppendDecisionAsync(
            tenantId: request.TenantId,
            principalUserId: request.UserId,
            operationType: request.OperationType,
            decision: decision.Decision,
            reasonCode: decision.ReasonCode,
            appliedRules: decision.AppliedRules,
            estimatedCost: decision.EstimatedCost,
            correlationId: request.CorrelationId,
            metadata: request.Metadata,
            ct: ct);

        return decision;
    }

    private static PolicyDecision EvaluateRules(IReadOnlyList<PolicyRule> rules, PolicyEvaluationRequest request)
    {
        var appliedRuleIds = new List<string>();
        string? firstDeny = null;
        string? firstDenyReason = null;
        bool hasWarn = false;
        string? warnReason = null;
        string? warnMessage = null;

        foreach (var rule in rules)
        {
            var (outcome, reasonCode, message) = EvaluateRule(rule, request);
            appliedRuleIds.Add(rule.Id.ToString("N"));

            if (outcome == PolicyDecisionKind.Deny && firstDeny is null)
            {
                firstDeny = PolicyDecisionKind.Deny;
                firstDenyReason = reasonCode;
            }
            else if (outcome == PolicyDecisionKind.Warn && !hasWarn)
            {
                hasWarn = true;
                warnReason = reasonCode;
                warnMessage = message;
            }
        }

        if (firstDeny is not null)
            return new PolicyDecision(PolicyDecisionKind.Deny, firstDenyReason!, appliedRuleIds);

        if (hasWarn)
            return new PolicyDecision(PolicyDecisionKind.Warn, warnReason!, appliedRuleIds, WarningMessage: warnMessage);

        // All rules evaluated — determine allow reason from the first matching rule type.
        var allowReason = rules[0].RuleType switch
        {
            PolicyRuleType.Quota => PolicyReasonCode.WithinQuota,
            PolicyRuleType.Budget => PolicyReasonCode.WithinBudget,
            PolicyRuleType.FeatureFlag => PolicyReasonCode.FeatureEnabled,
            _ => PolicyReasonCode.DefaultAllow
        };
        return new PolicyDecision(PolicyDecisionKind.Allow, allowReason, appliedRuleIds);
    }

    private static (string outcome, string reasonCode, string? message) EvaluateRule(
        PolicyRule rule, PolicyEvaluationRequest request)
    {
        return rule.RuleType switch
        {
            PolicyRuleType.FeatureFlag => EvaluateFeatureFlag(rule),
            PolicyRuleType.Compliance => EvaluateCompliance(rule),
            PolicyRuleType.FraudRisk => EvaluateFraudRisk(rule, request),
            // Quota and Budget require runtime counters not available at evaluation time — warn.
            PolicyRuleType.Quota => (PolicyDecisionKind.Warn, PolicyReasonCode.WithinQuota, "Quota enforcement requires runtime counter; evaluation deferred."),
            PolicyRuleType.Budget => (PolicyDecisionKind.Warn, PolicyReasonCode.WithinBudget, "Budget enforcement requires runtime counter; evaluation deferred."),
            _ => (PolicyDecisionKind.Allow, PolicyReasonCode.DefaultAllow, null)
        };
    }

    private static (string, string, string?) EvaluateFeatureFlag(PolicyRule rule)
    {
        if (!rule.RuleDefinition.TryGetValue("enabled", out var enabledVal))
            return (PolicyDecisionKind.Allow, PolicyReasonCode.FeatureEnabled, null);

        var enabled = enabledVal is bool b ? b
            : bool.TryParse(enabledVal?.ToString(), out var parsed) && parsed;

        return enabled
            ? (PolicyDecisionKind.Allow, PolicyReasonCode.FeatureEnabled, null)
            : (PolicyDecisionKind.Deny, PolicyReasonCode.FeatureDisabled, null);
    }

    private static (string, string, string?) EvaluateCompliance(PolicyRule rule)
    {
        if (!rule.RuleDefinition.TryGetValue("allowed", out var allowedVal))
            return (PolicyDecisionKind.Allow, PolicyReasonCode.DefaultAllow, null);

        var allowed = allowedVal is bool b ? b
            : bool.TryParse(allowedVal?.ToString(), out var parsed) && parsed;

        return allowed
            ? (PolicyDecisionKind.Allow, PolicyReasonCode.DefaultAllow, null)
            : (PolicyDecisionKind.Deny, PolicyReasonCode.ComplianceViolation, null);
    }

    private static (string, string, string?) EvaluateFraudRisk(PolicyRule rule, PolicyEvaluationRequest request)
    {
        if (!rule.RuleDefinition.TryGetValue("max_risk_score", out var maxRiskVal))
            return (PolicyDecisionKind.Allow, PolicyReasonCode.DefaultAllow, null);

        if (!decimal.TryParse(maxRiskVal?.ToString(), out var maxRisk))
            return (PolicyDecisionKind.Allow, PolicyReasonCode.DefaultAllow, null);

        decimal actualRisk = 0;
        if (request.Metadata is not null && request.Metadata.TryGetValue("risk_score", out var riskVal))
            decimal.TryParse(riskVal?.ToString(), out actualRisk);

        return actualRisk > maxRisk
            ? (PolicyDecisionKind.Deny, PolicyReasonCode.FraudRiskExceeded, null)
            : (PolicyDecisionKind.Allow, PolicyReasonCode.DefaultAllow, null);
    }
}
