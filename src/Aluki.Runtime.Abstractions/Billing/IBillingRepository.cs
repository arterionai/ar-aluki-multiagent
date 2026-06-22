namespace Aluki.Runtime.Abstractions.Billing;

public interface IBillingRepository
{
    // ── Catalog ───────────────────────────────────────────────────────────────
    Task<CatalogVersion> CreateCatalogVersionAsync(CreateCatalogVersionRequest request, CancellationToken ct);
    Task<CatalogVersion?> GetPublishedCatalogVersionAsync(CancellationToken ct);
    Task<IReadOnlyList<MeterPrice>> GetMeterPricesAsync(Guid catalogVersionId, CancellationToken ct);
    Task<MeterPrice> CreateMeterPriceAsync(CreateMeterPriceRequest request, CancellationToken ct);
    Task<PackageDefinitionVersion> CreatePackageDefinitionAsync(CreatePackageDefinitionRequest request, CancellationToken ct);
    Task<PackageDefinitionVersion?> GetPackageDefinitionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<PackageQuotaRule>> GetQuotaRulesAsync(Guid packageDefinitionVersionId, CancellationToken ct);
    Task<PackageQuotaRule> CreateQuotaRuleAsync(CreatePackageQuotaRuleRequest request, CancellationToken ct);

    // ── Billing account ───────────────────────────────────────────────────────
    Task<BillingAccount> CreateAccountAsync(CreateBillingAccountRequest request, CancellationToken ct);
    Task<BillingAccount?> GetAccountByTenantAsync(Guid tenantId, CancellationToken ct);
    Task<BillingAccount?> GetAccountAsync(Guid billingAccountId, CancellationToken ct);

    // ── Subscriptions ─────────────────────────────────────────────────────────
    Task<PackageSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken ct);
    Task<PackageSubscription?> GetActiveSubscriptionAsync(Guid tenantId, CancellationToken ct);
    Task<PackageSubscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken ct);
    Task<bool> ActivateSubscriptionAsync(Guid subscriptionId, DateTimeOffset termStartUtc, DateTimeOffset termEndUtc, CancellationToken ct);

    // ── Ledger (WORM) ─────────────────────────────────────────────────────────
    Task<LedgerEntry?> FindLedgerEntryByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct);
    Task<LedgerEntry> AppendLedgerEntryAsync(
        Guid tenantId, Guid billingAccountId, string entryType, string meterCode,
        decimal quantity, decimal unitPriceSnapshot, decimal amountTotal, string currencyCode,
        string catalogVersionCodeSnapshot, string? packageCodeSnapshot, string idempotencyKey,
        string? sourceEventId, DateTimeOffset? sourceEventTimeUtc, Guid? actorUserId,
        string? attributionMetadataJson, CancellationToken ct);
    Task<IReadOnlyList<LedgerEntry>> ListLedgerEntriesAsync(
        Guid tenantId, string meterCode, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
    Task<decimal> SumUsageForMeterAsync(
        Guid tenantId, string meterCode, DateTimeOffset fromUtc, DateTimeOffset toUtc,
        IReadOnlyList<string> entryTypes, CancellationToken ct);

    // ── Credits ───────────────────────────────────────────────────────────────
    Task<CreditBalance?> GetCreditBalanceAsync(Guid tenantId, CancellationToken ct);
    Task<CreditBalance> GetOrCreateCreditBalanceAsync(Guid tenantId, Guid billingAccountId, string currencyCode, CancellationToken ct);
    Task<CreditBalance> AdjustCreditBalanceAsync(Guid tenantId, decimal delta, CancellationToken ct);
    Task<CreditMovement?> FindCreditMovementByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct);
    Task<CreditMovement> AppendCreditMovementAsync(
        Guid tenantId, Guid creditBalanceId, string movementType, decimal amount,
        string idempotencyKey, Guid? referenceLedgerEntryId, CancellationToken ct);

    // ── Invoices ──────────────────────────────────────────────────────────────
    Task<Invoice?> FindInvoiceForCycleAsync(Guid tenantId, DateTimeOffset cycleStartUtc, DateTimeOffset cycleEndUtc, CancellationToken ct);
    Task<Invoice> CreateInvoiceAsync(
        Guid tenantId, Guid billingAccountId, string invoiceNumber,
        DateTimeOffset cycleStartUtc, DateTimeOffset cycleEndUtc,
        decimal subtotalAmount, decimal adjustmentsAmount, decimal totalAmount,
        string currencyCode, CancellationToken ct);
    Task FinalizeInvoiceAsync(Guid invoiceId, CancellationToken ct);
    Task<IReadOnlyList<Invoice>> ListInvoicesAsync(Guid tenantId, CancellationToken ct);
    Task<InvoiceLine> AppendInvoiceLineAsync(
        Guid invoiceId, Guid tenantId, string lineType, string? meterCode,
        string description, decimal quantity, decimal unitPrice, decimal lineTotal,
        string ledgerEntryRefsJson, CancellationToken ct);
    Task<IReadOnlyList<InvoiceLine>> ListInvoiceLinesAsync(Guid invoiceId, CancellationToken ct);

    // ── Audit ─────────────────────────────────────────────────────────────────
    Task AppendAuditEventAsync(
        Guid tenantId, string eventType, string? entityType, Guid? entityId,
        string? reasonCode, string? actorType, Guid? actorId,
        string? correlationId, string? payloadJson, CancellationToken ct);
}
