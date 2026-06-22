using Aluki.Runtime.Abstractions.Billing;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.Billing;

public sealed class EntitlementService : IEntitlementService
{
    private readonly IBillingRepository _repo;
    private readonly ILogger<EntitlementService> _logger;

    public EntitlementService(IBillingRepository repo, ILogger<EntitlementService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<UsageRecordResult> RecordUsageAsync(RecordUsageRequest request, CancellationToken ct)
    {
        // Idempotency check.
        var existing = await _repo.FindLedgerEntryByIdempotencyKeyAsync(request.TenantId, request.IdempotencyKey, ct);
        if (existing is not null)
        {
            return new UsageRecordResult(
                EntitlementDecision.IdempotentNoop,
                existing.Id,
                existing.AmountTotal,
                null);
        }

        var account = await _repo.GetAccountByTenantAsync(request.TenantId, ct);
        if (account is null)
        {
            _logger.LogWarning("No billing account for tenant {TenantId}", request.TenantId);
            return new UsageRecordResult(EntitlementDecision.DenyStatus, null, 0, "no_billing_account");
        }

        // Status gate: only active accounts may consume.
        if (account.BillingStatus != BillingStatus.Active && account.BillingStatus != BillingStatus.Grace)
        {
            await AppendDenialAsync(account, request, "0", "v0", null, ct);
            return new UsageRecordResult(EntitlementDecision.DenyStatus, null, 0, account.BillingStatus);
        }

        if (account.BillingMode == BillingMode.Package)
            return await RecordPackageUsageAsync(account, request, ct);

        return await RecordPaygUsageAsync(account, request, ct);
    }

    // ── PAYG flow ─────────────────────────────────────────────────────────────

    private async Task<UsageRecordResult> RecordPaygUsageAsync(BillingAccount account, RecordUsageRequest request, CancellationToken ct)
    {
        var catalog = await _repo.GetPublishedCatalogVersionAsync(ct);
        if (catalog is null)
        {
            _logger.LogWarning("No published catalog for PAYG billing, tenant {TenantId}", request.TenantId);
            return new UsageRecordResult(EntitlementDecision.DenyStatus, null, 0, "no_catalog");
        }

        var prices = await _repo.GetMeterPricesAsync(catalog.Id, ct);
        var price = prices.FirstOrDefault(p => p.MeterCode == request.MeterCode);
        var unitPrice = price?.UnitPrice ?? 0m;
        var amount = unitPrice * request.Quantity;

        var entry = await _repo.AppendLedgerEntryAsync(
            request.TenantId, account.Id, LedgerEntryType.UsagePayg, request.MeterCode,
            request.Quantity, unitPrice, amount, account.CurrencyCode,
            catalog.VersionCode, null, request.IdempotencyKey,
            request.SourceEventId, request.SourceEventTimeUtc, request.ActorUserId,
            request.AttributionMetadataJson, ct);

        return new UsageRecordResult(EntitlementDecision.AllowPayg, entry.Id, amount, null);
    }

    // ── Package flow ──────────────────────────────────────────────────────────

    private async Task<UsageRecordResult> RecordPackageUsageAsync(BillingAccount account, RecordUsageRequest request, CancellationToken ct)
    {
        var catalog = await _repo.GetPublishedCatalogVersionAsync(ct);
        if (catalog is null)
            return new UsageRecordResult(EntitlementDecision.DenyStatus, null, 0, "no_catalog");

        var subscription = await _repo.GetActiveSubscriptionAsync(request.TenantId, ct);
        if (subscription is null || subscription.State != SubscriptionState.Active)
            return await RecordPaygUsageAsync(account, request, ct);

        var pkg = await _repo.GetPackageDefinitionAsync(subscription.PackageDefinitionVersionId, ct);
        if (pkg is null)
            return new UsageRecordResult(EntitlementDecision.DenyStatus, null, 0, "no_package");

        var quotaRules = await _repo.GetQuotaRulesAsync(subscription.PackageDefinitionVersionId, ct);
        var rule = quotaRules.FirstOrDefault(r => r.MeterCode == request.MeterCode);

        // No quota rule for this meter → treat as PAYG.
        if (rule is null)
            return await RecordPaygUsageAsync(account, request, ct);

        // Compute cycle window: from term_start to now (capped by term_end).
        var cycleStart = subscription.TermStartUtc ?? DateTimeOffset.UtcNow.Date;
        var cycleEnd = subscription.TermEndUtc ?? cycleStart.AddMonths(1);
        var now = DateTimeOffset.UtcNow;
        if (now > cycleEnd) cycleEnd = now.AddSeconds(1);

        var includedTypes = new[] { LedgerEntryType.UsageIncluded };
        var usedIncluded = await _repo.SumUsageForMeterAsync(
            request.TenantId, request.MeterCode, cycleStart, cycleEnd, includedTypes, ct);

        var remaining = rule.IncludedQuantity - usedIncluded;

        // 1. Allow from included quota.
        if (remaining >= request.Quantity)
        {
            var entry = await _repo.AppendLedgerEntryAsync(
                request.TenantId, account.Id, LedgerEntryType.UsageIncluded, request.MeterCode,
                request.Quantity, 0m, 0m, account.CurrencyCode,
                catalog.VersionCode, pkg.PackageCode, request.IdempotencyKey,
                request.SourceEventId, request.SourceEventTimeUtc, request.ActorUserId,
                request.AttributionMetadataJson, ct);

            return new UsageRecordResult(EntitlementDecision.AllowIncluded, entry.Id, 0m, null);
        }

        // 2. Try credit balance.
        var creditBalance = await _repo.GetCreditBalanceAsync(request.TenantId, ct);
        if (creditBalance is not null && creditBalance.AvailableAmount > 0)
        {
            var prices = await _repo.GetMeterPricesAsync(catalog.Id, ct);
            var price = prices.FirstOrDefault(p => p.MeterCode == request.MeterCode);
            var unitPrice = price?.UnitPrice ?? (rule.OverageUnitPrice ?? 0m);
            var amount = unitPrice * request.Quantity;

            if (creditBalance.AvailableAmount >= amount)
            {
                var creditKey = $"credit-{request.IdempotencyKey}";
                var balance = await _repo.AdjustCreditBalanceAsync(request.TenantId, -amount, ct);
                await _repo.AppendCreditMovementAsync(
                    request.TenantId, creditBalance.Id, CreditMovementType.DebitUsage, amount,
                    creditKey, null, ct);

                var entry = await _repo.AppendLedgerEntryAsync(
                    request.TenantId, account.Id, LedgerEntryType.UsageCredit, request.MeterCode,
                    request.Quantity, unitPrice, amount, account.CurrencyCode,
                    catalog.VersionCode, pkg.PackageCode, request.IdempotencyKey,
                    request.SourceEventId, request.SourceEventTimeUtc, request.ActorUserId,
                    request.AttributionMetadataJson, ct);

                return new UsageRecordResult(EntitlementDecision.AllowCredit, entry.Id, amount, null);
            }
        }

        // 3. Overage.
        if (pkg.OveragePolicy == "billable_overage" && rule.OverageUnitPrice.HasValue)
        {
            var overagePrice = rule.OverageUnitPrice.Value;
            var overageAmount = overagePrice * request.Quantity;

            var entry = await _repo.AppendLedgerEntryAsync(
                request.TenantId, account.Id, LedgerEntryType.UsageOverage, request.MeterCode,
                request.Quantity, overagePrice, overageAmount, account.CurrencyCode,
                catalog.VersionCode, pkg.PackageCode, request.IdempotencyKey,
                request.SourceEventId, request.SourceEventTimeUtc, request.ActorUserId,
                request.AttributionMetadataJson, ct);

            return new UsageRecordResult(EntitlementDecision.AllowOverage, entry.Id, overageAmount, null);
        }

        // 4. Hard stop.
        var reasonCode = rule.HardStopReasonCode ?? "quota_exceeded";
        await AppendDenialAsync(account, request, "0", catalog.VersionCode, pkg.PackageCode, ct);
        return new UsageRecordResult(EntitlementDecision.DenyHardStop, null, 0m, reasonCode);
    }

    private async Task AppendDenialAsync(
        BillingAccount account, RecordUsageRequest request,
        string unitPrice, string catalogVersionCode, string? packageCode, CancellationToken ct)
    {
        await _repo.AppendLedgerEntryAsync(
            request.TenantId, account.Id, LedgerEntryType.DenialEvent, request.MeterCode,
            request.Quantity, 0m, 0m, account.CurrencyCode,
            catalogVersionCode, packageCode, request.IdempotencyKey,
            request.SourceEventId, request.SourceEventTimeUtc, request.ActorUserId,
            request.AttributionMetadataJson, ct);
    }

    // ── Entitlement snapshot ──────────────────────────────────────────────────

    public async Task<EntitlementSnapshot> GetEntitlementSnapshotAsync(Guid tenantId, CancellationToken ct)
    {
        var account = await _repo.GetAccountByTenantAsync(tenantId, ct);
        if (account is null)
            return new EntitlementSnapshot(tenantId, BillingMode.Payg, BillingStatus.Active, null, []);

        var catalog = await _repo.GetPublishedCatalogVersionAsync(ct);
        if (catalog is null)
            return new EntitlementSnapshot(tenantId, account.BillingMode, account.BillingStatus, null, []);

        if (account.BillingMode != BillingMode.Package)
        {
            var prices = await _repo.GetMeterPricesAsync(catalog.Id, ct);
            var meters = prices.Select(p => new MeterEntitlement(
                p.MeterCode, 0, 0, 0, 0, p.UnitPrice, false)).ToList();
            return new EntitlementSnapshot(tenantId, account.BillingMode, account.BillingStatus, null, meters);
        }

        var subscription = await _repo.GetActiveSubscriptionAsync(tenantId, ct);
        var creditBalance = await _repo.GetCreditBalanceAsync(tenantId, ct);
        var credits = creditBalance?.AvailableAmount ?? 0m;

        if (subscription?.State != SubscriptionState.Active)
            return new EntitlementSnapshot(tenantId, account.BillingMode, account.BillingStatus, null, []);

        var pkg = await _repo.GetPackageDefinitionAsync(subscription.PackageDefinitionVersionId, ct);
        if (pkg is null)
            return new EntitlementSnapshot(tenantId, account.BillingMode, account.BillingStatus, null, []);

        var quotaRules = await _repo.GetQuotaRulesAsync(subscription.PackageDefinitionVersionId, ct);
        var cycleStart = subscription.TermStartUtc ?? DateTimeOffset.UtcNow.Date;
        var cycleEnd = subscription.TermEndUtc ?? cycleStart.AddMonths(1);
        var now = DateTimeOffset.UtcNow;
        if (now > cycleEnd) cycleEnd = now.AddSeconds(1);

        var includedTypes = new[] { LedgerEntryType.UsageIncluded };
        var meterEntitlements = new List<MeterEntitlement>();
        foreach (var rule in quotaRules)
        {
            var used = await _repo.SumUsageForMeterAsync(
                tenantId, rule.MeterCode, cycleStart, cycleEnd, includedTypes, ct);
            meterEntitlements.Add(new MeterEntitlement(
                rule.MeterCode,
                rule.IncludedQuantity,
                used,
                Math.Max(0, rule.IncludedQuantity - used),
                credits,
                rule.OverageUnitPrice ?? 0m,
                rule.HardStopReasonCode is not null));
        }

        return new EntitlementSnapshot(tenantId, account.BillingMode, account.BillingStatus, pkg.PackageCode, meterEntitlements);
    }
}
