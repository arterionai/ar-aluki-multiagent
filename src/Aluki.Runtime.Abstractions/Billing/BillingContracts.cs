namespace Aluki.Runtime.Abstractions.Billing;

// ── Catalog ───────────────────────────────────────────────────────────────────

public sealed record CatalogVersion(
    Guid Id,
    string VersionCode,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string Status,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record MeterPrice(
    Guid Id,
    Guid CatalogVersionId,
    string MeterCode,
    string UnitName,
    decimal UnitPrice,
    decimal MinBillableQuantity,
    string RoundingPolicy,
    DateTimeOffset CreatedAtUtc);

public sealed record PackageDefinitionVersion(
    Guid Id,
    Guid CatalogVersionId,
    string PackageCode,
    string PackageName,
    string BillingTerm,
    decimal BasePrice,
    string OveragePolicy,
    int GraceDays,
    DateTimeOffset CreatedAtUtc);

public sealed record PackageQuotaRule(
    Guid Id,
    Guid PackageDefinitionVersionId,
    string MeterCode,
    decimal IncludedQuantity,
    decimal? OverageUnitPrice,
    string? HardStopReasonCode,
    DateTimeOffset CreatedAtUtc);

// ── Billing account ───────────────────────────────────────────────────────────

public sealed record BillingAccount(
    Guid Id,
    Guid TenantId,
    string TenantType,
    string BillingMode,
    string BillingStatus,
    string CurrencyCode,
    string Timezone,
    string? InvoiceOwnerDisplay,
    bool ChargebackEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public static class BillingMode
{
    public const string Payg = "payg";
    public const string Package = "package";
}

public static class BillingStatus
{
    public const string Active = "active";
    public const string Grace = "grace";
    public const string Suspended = "suspended";
}

public static class TenantType
{
    public const string Individual = "INDIVIDUAL";
    public const string Organization = "ORGANIZATION";
}

// ── Subscriptions ─────────────────────────────────────────────────────────────

public sealed record PackageSubscription(
    Guid Id,
    Guid TenantId,
    Guid BillingAccountId,
    Guid PackageDefinitionVersionId,
    string State,
    DateTimeOffset? TermStartUtc,
    DateTimeOffset? TermEndUtc,
    string RenewalPolicy,
    Guid? ScheduledChangePackageDefinitionVersionId,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset CreatedAtUtc);

public static class SubscriptionState
{
    public const string PendingActivation = "pending_activation";
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string CancellationGrace = "cancellation_grace";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
}

// ── Ledger ────────────────────────────────────────────────────────────────────

public sealed record LedgerEntry(
    Guid Id,
    Guid TenantId,
    Guid BillingAccountId,
    string EntryType,
    string MeterCode,
    decimal Quantity,
    decimal UnitPriceSnapshot,
    decimal AmountTotal,
    string CurrencyCode,
    string CatalogVersionCodeSnapshot,
    string? PackageCodeSnapshot,
    string IdempotencyKey,
    string? SourceEventId,
    DateTimeOffset? SourceEventTimeUtc,
    Guid? ActorUserId,
    string? AttributionMetadataJson,
    DateTimeOffset RecordedAtUtc);

public static class LedgerEntryType
{
    public const string UsageIncluded = "usage_included";
    public const string UsageCredit = "usage_credit";
    public const string UsageOverage = "usage_overage";
    public const string UsagePayg = "usage_payg";
    public const string ProrationAdjustment = "proration_adjustment";
    public const string ManualAdjustment = "manual_adjustment";
    public const string DenialEvent = "denial_event";
}

// ── Credits ───────────────────────────────────────────────────────────────────

public sealed record CreditBalance(
    Guid Id,
    Guid TenantId,
    Guid BillingAccountId,
    decimal AvailableAmount,
    decimal ReservedAmount,
    string CurrencyCode,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreditMovement(
    Guid Id,
    Guid TenantId,
    Guid CreditBalanceId,
    string MovementType,
    decimal Amount,
    string IdempotencyKey,
    Guid? ReferenceLedgerEntryId,
    DateTimeOffset CreatedAtUtc);

public static class CreditMovementType
{
    public const string Topup = "topup";
    public const string DebitUsage = "debit_usage";
    public const string Refund = "refund";
    public const string Adjustment = "adjustment";
}

// ── Invoices ──────────────────────────────────────────────────────────────────

public sealed record Invoice(
    Guid Id,
    Guid TenantId,
    Guid BillingAccountId,
    string InvoiceNumber,
    DateTimeOffset CycleStartUtc,
    DateTimeOffset CycleEndUtc,
    DateTimeOffset? CycleClosedAtUtc,
    string Status,
    decimal SubtotalAmount,
    decimal AdjustmentsAmount,
    decimal TotalAmount,
    string CurrencyCode,
    DateTimeOffset GeneratedAtUtc);

public sealed record InvoiceLine(
    Guid Id,
    Guid InvoiceId,
    Guid TenantId,
    string LineType,
    string? MeterCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string LedgerEntryRefsJson,
    DateTimeOffset CreatedAtUtc);

public static class InvoiceStatus
{
    public const string Draft = "draft";
    public const string Finalized = "finalized";
    public const string Void = "void";
}

// ── Entitlement evaluation ────────────────────────────────────────────────────

public static class EntitlementDecision
{
    public const string AllowIncluded = "allow_included";
    public const string AllowCredit = "allow_credit";
    public const string AllowOverage = "allow_overage";
    public const string AllowPayg = "allow_payg";
    public const string DenyHardStop = "deny_hard_stop";
    public const string DenyStatus = "deny_status";
    public const string IdempotentNoop = "idempotent_noop";
}

// ── Request / response DTOs ───────────────────────────────────────────────────

public sealed record CreateBillingAccountRequest(
    Guid TenantId,
    string TenantType,
    string BillingMode = BillingMode.Payg,
    string CurrencyCode = "USD",
    string Timezone = "UTC",
    string? InvoiceOwnerDisplay = null);

public sealed record CreateSubscriptionRequest(
    Guid TenantId,
    Guid BillingAccountId,
    Guid PackageDefinitionVersionId,
    string RenewalPolicy = "auto_renew");

public sealed record RecordUsageRequest(
    Guid TenantId,
    string MeterCode,
    decimal Quantity,
    string IdempotencyKey,
    string? SourceEventId = null,
    DateTimeOffset? SourceEventTimeUtc = null,
    Guid? ActorUserId = null,
    string? AttributionMetadataJson = null);

public sealed record UsageRecordResult(
    string Decision,
    Guid? LedgerEntryId,
    decimal AmountCharged,
    string? ReasonCode);

public sealed record EntitlementSnapshot(
    Guid TenantId,
    string BillingMode,
    string BillingStatus,
    string? ActivePackageCode,
    IReadOnlyList<MeterEntitlement> Meters);

public sealed record MeterEntitlement(
    string MeterCode,
    decimal IncludedQuantity,
    decimal UsedQuantity,
    decimal RemainingIncluded,
    decimal CreditBalance,
    decimal OverageUnitPrice,
    bool HardStopEnabled);

public sealed record GenerateInvoiceRequest(
    Guid TenantId,
    DateTimeOffset CycleStartUtc,
    DateTimeOffset CycleEndUtc,
    string? IdempotencyKey = null);

public sealed record CreateCatalogVersionRequest(
    string VersionCode,
    DateTimeOffset EffectiveFromUtc);

public sealed record CreateMeterPriceRequest(
    Guid CatalogVersionId,
    string MeterCode,
    string UnitName,
    decimal UnitPrice,
    decimal MinBillableQuantity = 0,
    string RoundingPolicy = "round_half_up");

public sealed record CreatePackageDefinitionRequest(
    Guid CatalogVersionId,
    string PackageCode,
    string PackageName,
    string BillingTerm,
    decimal BasePrice,
    string OveragePolicy,
    int GraceDays = 0);

public sealed record CreatePackageQuotaRuleRequest(
    Guid PackageDefinitionVersionId,
    string MeterCode,
    decimal IncludedQuantity,
    decimal? OverageUnitPrice = null,
    string? HardStopReasonCode = null);

public sealed record TopUpCreditRequest(
    Guid TenantId,
    decimal Amount,
    string IdempotencyKey,
    string CurrencyCode = "USD");
