namespace Aluki.Runtime.Abstractions.Skills.SuggestionsAdmin;

public static class AdminStatus
{
    public const string Captured = "captured";
    public const string UnderReview = "under_review";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Archived = "archived";
}

public static class AdminRole
{
    public const string Reviewer = "AdminReviewer";
    public const string Approver = "AdminApprover";
    public const string Auditor = "AdminAuditor";
    public const string System = "System";
}

public static class AdminActionType
{
    public const string StatusChange = "status_change";
    public const string CategoryChange = "category_change";
    public const string PriorityChange = "priority_change";
    public const string AuthorizationDenied = "authorization_denied";
    public const string Compensation = "compensation";
}

public static class RewardRuleType
{
    public const string Base = "base";
    public const string Quality = "quality";
    public const string Streak = "streak";
}

public static class GrantStatus
{
    public const string Granted = "granted";
    public const string Rejected = "rejected";
    public const string Duplicate = "duplicate";
    public const string Conflict = "conflict";
    public const string Compensation = "compensation";
}

public static class DecisionType
{
    public const string Granted = "granted";
    public const string Skipped = "skipped";
    public const string Rejected = "rejected";
    public const string Duplicate = "duplicate";
    public const string Conflict = "conflict";
}

public static class DeliveryState
{
    public const string Pending = "pending";
    public const string Retrying = "retrying";
    public const string Delivered = "delivered";
    public const string DeadLetter = "dead_letter";
}
