namespace Aluki.Runtime.Abstractions.Governance;

// ── Policy decision ───────────────────────────────────────────────────────────

public sealed record PolicyEvaluationRequest(
    Guid TenantId,
    Guid? UserId,
    string OperationType,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    string? CorrelationId = null);

public sealed record PolicyDecision(
    string Decision,
    string ReasonCode,
    IReadOnlyList<string> AppliedRules,
    decimal? EstimatedCost = null,
    string? WarningMessage = null)
{
    public bool Allowed => Decision is PolicyDecisionKind.Allow or PolicyDecisionKind.Warn;
}

public static class PolicyDecisionKind
{
    public const string Allow = "allow";
    public const string Deny = "deny";
    public const string Warn = "warn";
}

public static class PolicyReasonCode
{
    public const string DefaultAllow = "no_applicable_rules";
    public const string QuotaExceeded = "quota_exceeded";
    public const string BudgetExceeded = "budget_exceeded";
    public const string FeatureDisabled = "feature_disabled";
    public const string ComplianceViolation = "compliance_violation";
    public const string FraudRiskExceeded = "fraud_risk_exceeded";
    public const string WithinQuota = "within_quota";
    public const string WithinBudget = "within_budget";
    public const string FeatureEnabled = "feature_enabled";
}

// ── Policy rules ─────────────────────────────────────────────────────────────

public sealed record PolicyRule(
    Guid Id,
    Guid TenantId,
    string RuleType,
    string OperationType,
    IReadOnlyDictionary<string, object?> RuleDefinition,
    int Priority,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public static class PolicyRuleType
{
    public const string Quota = "quota";
    public const string Budget = "budget";
    public const string FeatureFlag = "feature_flag";
    public const string Compliance = "compliance";
    public const string FraudRisk = "fraud_risk";
}

public sealed record CreatePolicyRuleRequest(
    Guid TenantId,
    string RuleType,
    string OperationType,
    IReadOnlyDictionary<string, object?> RuleDefinition,
    int Priority = 100);

// ── Consent ──────────────────────────────────────────────────────────────────

public sealed record ConsentRecord(
    Guid Id,
    Guid TenantId,
    Guid GrantorId,
    Guid GranteeId,
    string ConsentType,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevocationReason)
{
    public bool IsActive => RevokedAtUtc is null;
}

public sealed record GrantConsentRequest(
    Guid TenantId,
    Guid GrantorId,
    Guid GranteeId,
    string ConsentType);

public sealed record RevokeConsentRequest(
    Guid TenantId,
    Guid GrantorId,
    Guid GranteeId,
    string ConsentType,
    string? Reason = null);

public static class ConsentType
{
    public const string DelegatedReminderSend = "delegated_reminder_send";
    public const string ShareMemory = "share_memory";
    public const string ViewCalendar = "view_calendar";
    public const string SendFeedbackOnBehalf = "send_feedback_on_behalf";
}
