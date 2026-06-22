using Aluki.Runtime.Abstractions.Billing;
using Aluki.Runtime.Host.Skills.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class BillingContractTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();

    // ── Constants ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BillingMode.Payg, "payg")]
    [InlineData(BillingMode.Package, "package")]
    public void BillingMode_constants_are_correct(string constant, string expected)
        => Assert.Equal(expected, constant);

    [Theory]
    [InlineData(BillingStatus.Active, "active")]
    [InlineData(BillingStatus.Grace, "grace")]
    [InlineData(BillingStatus.Suspended, "suspended")]
    public void BillingStatus_constants_are_correct(string constant, string expected)
        => Assert.Equal(expected, constant);

    [Theory]
    [InlineData(EntitlementDecision.AllowIncluded, "allow_included")]
    [InlineData(EntitlementDecision.AllowCredit, "allow_credit")]
    [InlineData(EntitlementDecision.AllowOverage, "allow_overage")]
    [InlineData(EntitlementDecision.AllowPayg, "allow_payg")]
    [InlineData(EntitlementDecision.DenyHardStop, "deny_hard_stop")]
    [InlineData(EntitlementDecision.DenyStatus, "deny_status")]
    [InlineData(EntitlementDecision.IdempotentNoop, "idempotent_noop")]
    public void EntitlementDecision_constants_are_correct(string constant, string expected)
        => Assert.Equal(expected, constant);

    // ── PAYG usage ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PAYG_usage_records_allow_payg_decision()
    {
        var repo = new StubBillingRepository();
        await repo.CreateAccountAsync(new CreateBillingAccountRequest(_tenantId, TenantType.Individual, BillingMode.Payg), CancellationToken.None);
        var catalogVersion = await repo.CreateCatalogVersionAsync(new CreateCatalogVersionRequest("v1", DateTimeOffset.UtcNow), CancellationToken.None);
        repo.PublishCatalogVersion(catalogVersion.Id);
        await repo.CreateMeterPriceAsync(new CreateMeterPriceRequest(catalogVersion.Id, "api_call", "API Call", 0.01m), CancellationToken.None);

        var svc = new EntitlementService(repo, NullLogger<EntitlementService>.Instance);
        var result = await svc.RecordUsageAsync(
            new RecordUsageRequest(_tenantId, "api_call", 10, "key-001"),
            CancellationToken.None);

        Assert.Equal(EntitlementDecision.AllowPayg, result.Decision);
        Assert.Equal(0.10m, result.AmountCharged);
    }

    [Fact]
    public async Task PAYG_usage_idempotency_returns_noop()
    {
        var repo = new StubBillingRepository();
        await repo.CreateAccountAsync(new CreateBillingAccountRequest(_tenantId, TenantType.Individual, BillingMode.Payg), CancellationToken.None);
        var catalog = await repo.CreateCatalogVersionAsync(new CreateCatalogVersionRequest("v1", DateTimeOffset.UtcNow), CancellationToken.None);
        repo.PublishCatalogVersion(catalog.Id);
        await repo.CreateMeterPriceAsync(new CreateMeterPriceRequest(catalog.Id, "api_call", "API Call", 0.01m), CancellationToken.None);

        var svc = new EntitlementService(repo, NullLogger<EntitlementService>.Instance);
        var request = new RecordUsageRequest(_tenantId, "api_call", 5, "key-idem");
        await svc.RecordUsageAsync(request, CancellationToken.None);
        var second = await svc.RecordUsageAsync(request, CancellationToken.None);

        Assert.Equal(EntitlementDecision.IdempotentNoop, second.Decision);
    }

    // ── Status gate ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Suspended_account_returns_deny_status()
    {
        var repo = new StubBillingRepository();
        var account = await repo.CreateAccountAsync(new CreateBillingAccountRequest(_tenantId, TenantType.Individual), CancellationToken.None);
        repo.SetBillingStatus(_tenantId, BillingStatus.Suspended);

        var svc = new EntitlementService(repo, NullLogger<EntitlementService>.Instance);
        var result = await svc.RecordUsageAsync(
            new RecordUsageRequest(_tenantId, "api_call", 1, "key-suspended"),
            CancellationToken.None);

        Assert.Equal(EntitlementDecision.DenyStatus, result.Decision);
    }

    [Fact]
    public async Task Missing_account_returns_deny_status()
    {
        var repo = new StubBillingRepository();
        var svc = new EntitlementService(repo, NullLogger<EntitlementService>.Instance);

        var result = await svc.RecordUsageAsync(
            new RecordUsageRequest(Guid.NewGuid(), "api_call", 1, "key-no-account"),
            CancellationToken.None);

        Assert.Equal(EntitlementDecision.DenyStatus, result.Decision);
    }

    // ── Package flow ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Package_included_quota_returns_allow_included()
    {
        var repo = BuildPackageRepo(includedQuantity: 100, overagePolicy: "billable_overage");
        var svc = new EntitlementService(repo, NullLogger<EntitlementService>.Instance);

        var result = await svc.RecordUsageAsync(
            new RecordUsageRequest(_tenantId, "api_call", 10, "key-pkg-01"),
            CancellationToken.None);

        Assert.Equal(EntitlementDecision.AllowIncluded, result.Decision);
        Assert.Equal(0m, result.AmountCharged);
    }

    [Fact]
    public async Task Package_exceeded_quota_billable_overage_returns_allow_overage()
    {
        var repo = BuildPackageRepo(includedQuantity: 0, overagePolicy: "billable_overage", overageUnitPrice: 0.05m);
        var svc = new EntitlementService(repo, NullLogger<EntitlementService>.Instance);

        var result = await svc.RecordUsageAsync(
            new RecordUsageRequest(_tenantId, "api_call", 5, "key-overage-01"),
            CancellationToken.None);

        Assert.Equal(EntitlementDecision.AllowOverage, result.Decision);
        Assert.Equal(0.25m, result.AmountCharged);
    }

    [Fact]
    public async Task Package_exceeded_quota_hard_stop_returns_deny()
    {
        var repo = BuildPackageRepo(includedQuantity: 0, overagePolicy: "hard_stop", hardStopReasonCode: "quota_exceeded");
        var svc = new EntitlementService(repo, NullLogger<EntitlementService>.Instance);

        var result = await svc.RecordUsageAsync(
            new RecordUsageRequest(_tenantId, "api_call", 1, "key-hard-stop"),
            CancellationToken.None);

        Assert.Equal(EntitlementDecision.DenyHardStop, result.Decision);
        Assert.Equal("quota_exceeded", result.ReasonCode);
    }

    // ── Entitlement snapshot ──────────────────────────────────────────────────

    [Fact]
    public async Task Snapshot_reflects_package_usage()
    {
        var repo = BuildPackageRepo(includedQuantity: 100, overagePolicy: "billable_overage");
        var svc = new EntitlementService(repo, NullLogger<EntitlementService>.Instance);

        await svc.RecordUsageAsync(new RecordUsageRequest(_tenantId, "api_call", 30, "k1"), CancellationToken.None);
        var snapshot = await svc.GetEntitlementSnapshotAsync(_tenantId, CancellationToken.None);

        var meter = snapshot.Meters.FirstOrDefault(m => m.MeterCode == "api_call");
        Assert.NotNull(meter);
        Assert.Equal(100m, meter.IncludedQuantity);
        Assert.Equal(30m, meter.UsedQuantity);
        Assert.Equal(70m, meter.RemainingIncluded);
    }

    // ── Invoice generation ────────────────────────────────────────────────────

    [Fact]
    public async Task Invoice_generation_is_idempotent()
    {
        var repo = new StubBillingRepository();
        await repo.CreateAccountAsync(new CreateBillingAccountRequest(_tenantId, TenantType.Individual, BillingMode.Payg), CancellationToken.None);

        var svc = new BillingCycleService(repo, NullLogger<BillingCycleService>.Instance);
        var request = new GenerateInvoiceRequest(_tenantId,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var inv1 = await svc.GenerateInvoiceAsync(request, CancellationToken.None);
        var inv2 = await svc.GenerateInvoiceAsync(request, CancellationToken.None);

        Assert.Equal(inv1.Id, inv2.Id);
    }

    [Fact]
    public async Task Credit_topup_idempotency()
    {
        var repo = new StubBillingRepository();
        await repo.CreateAccountAsync(new CreateBillingAccountRequest(_tenantId, TenantType.Individual, BillingMode.Payg), CancellationToken.None);

        var svc = new BillingCycleService(repo, NullLogger<BillingCycleService>.Instance);
        var request = new TopUpCreditRequest(_tenantId, 50m, "topup-idem-001");

        var b1 = await svc.TopUpCreditAsync(request, CancellationToken.None);
        var b2 = await svc.TopUpCreditAsync(request, CancellationToken.None);

        // Second call is idempotent: balance stays at 50.
        Assert.Equal(50m, b2.AvailableAmount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IBillingRepository BuildPackageRepo(
        decimal includedQuantity, string overagePolicy,
        decimal? overageUnitPrice = null, string? hardStopReasonCode = null)
    {
        var repo = new StubBillingRepository();
        var tenantId = _tenantId;

        // Setup done synchronously via Task.Run to keep test code clean.
        Task.Run(async () =>
        {
            var account = await repo.CreateAccountAsync(
                new CreateBillingAccountRequest(tenantId, TenantType.Individual, BillingMode.Package),
                CancellationToken.None);

            var catalog = await repo.CreateCatalogVersionAsync(
                new CreateCatalogVersionRequest("v1", DateTimeOffset.UtcNow),
                CancellationToken.None);
            repo.PublishCatalogVersion(catalog.Id);

            await repo.CreateMeterPriceAsync(
                new CreateMeterPriceRequest(catalog.Id, "api_call", "API Call", overageUnitPrice ?? 0m),
                CancellationToken.None);

            var pkg = await repo.CreatePackageDefinitionAsync(
                new CreatePackageDefinitionRequest(catalog.Id, "starter", "Starter", "monthly", 9.99m, overagePolicy),
                CancellationToken.None);

            await repo.CreateQuotaRuleAsync(
                new CreatePackageQuotaRuleRequest(pkg.Id, "api_call", includedQuantity, overageUnitPrice, hardStopReasonCode),
                CancellationToken.None);

            await repo.CreateSubscriptionAsync(
                new CreateSubscriptionRequest(tenantId, account.Id, pkg.Id),
                CancellationToken.None);
            var sub = await repo.GetActiveSubscriptionAsync(tenantId, CancellationToken.None);
            if (sub is not null)
                await repo.ActivateSubscriptionAsync(sub.Id,
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddDays(29),
                    CancellationToken.None);
        }).GetAwaiter().GetResult();

        return repo;
    }
}

// ── Stub repository ───────────────────────────────────────────────────────────

file sealed class StubBillingRepository : IBillingRepository
{
    private readonly List<CatalogVersion> _catalogVersions = [];
    private readonly List<MeterPrice> _meterPrices = [];
    private readonly List<PackageDefinitionVersion> _packages = [];
    private readonly List<PackageQuotaRule> _quotaRules = [];
    private readonly List<BillingAccount> _accounts = [];
    private readonly List<PackageSubscription> _subscriptions = [];
    private readonly List<LedgerEntry> _ledger = [];
    private readonly List<CreditBalance> _creditBalances = [];
    private readonly List<CreditMovement> _creditMovements = [];
    private readonly List<Invoice> _invoices = [];
    private readonly List<InvoiceLine> _invoiceLines = [];
    private Guid? _publishedCatalogVersionId;

    public void PublishCatalogVersion(Guid id)
        => _publishedCatalogVersionId = id;

    public void SetBillingStatus(Guid tenantId, string status)
    {
        var idx = _accounts.FindIndex(a => a.TenantId == tenantId);
        if (idx >= 0)
            _accounts[idx] = _accounts[idx] with { BillingStatus = status };
    }

    public Task<CatalogVersion> CreateCatalogVersionAsync(CreateCatalogVersionRequest req, CancellationToken ct)
    {
        var v = new CatalogVersion(Guid.NewGuid(), req.VersionCode, req.EffectiveFromUtc, null, "draft", null, DateTimeOffset.UtcNow);
        _catalogVersions.Add(v);
        return Task.FromResult(v);
    }

    public Task<CatalogVersion?> GetPublishedCatalogVersionAsync(CancellationToken ct)
        => Task.FromResult(_publishedCatalogVersionId.HasValue
            ? _catalogVersions.FirstOrDefault(v => v.Id == _publishedCatalogVersionId.Value)
            : null);

    public Task<IReadOnlyList<MeterPrice>> GetMeterPricesAsync(Guid catalogVersionId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MeterPrice>>(_meterPrices.Where(p => p.CatalogVersionId == catalogVersionId).ToList());

    public Task<MeterPrice> CreateMeterPriceAsync(CreateMeterPriceRequest req, CancellationToken ct)
    {
        var p = new MeterPrice(Guid.NewGuid(), req.CatalogVersionId, req.MeterCode, req.UnitName, req.UnitPrice, req.MinBillableQuantity, req.RoundingPolicy, DateTimeOffset.UtcNow);
        _meterPrices.Add(p);
        return Task.FromResult(p);
    }

    public Task<PackageDefinitionVersion> CreatePackageDefinitionAsync(CreatePackageDefinitionRequest req, CancellationToken ct)
    {
        var p = new PackageDefinitionVersion(Guid.NewGuid(), req.CatalogVersionId, req.PackageCode, req.PackageName, req.BillingTerm, req.BasePrice, req.OveragePolicy, req.GraceDays, DateTimeOffset.UtcNow);
        _packages.Add(p);
        return Task.FromResult(p);
    }

    public Task<PackageDefinitionVersion?> GetPackageDefinitionAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_packages.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<PackageQuotaRule>> GetQuotaRulesAsync(Guid packageDefinitionVersionId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PackageQuotaRule>>(_quotaRules.Where(r => r.PackageDefinitionVersionId == packageDefinitionVersionId).ToList());

    public Task<PackageQuotaRule> CreateQuotaRuleAsync(CreatePackageQuotaRuleRequest req, CancellationToken ct)
    {
        var r = new PackageQuotaRule(Guid.NewGuid(), req.PackageDefinitionVersionId, req.MeterCode, req.IncludedQuantity, req.OverageUnitPrice, req.HardStopReasonCode, DateTimeOffset.UtcNow);
        _quotaRules.Add(r);
        return Task.FromResult(r);
    }

    public Task<BillingAccount> CreateAccountAsync(CreateBillingAccountRequest req, CancellationToken ct)
    {
        var existing = _accounts.FirstOrDefault(a => a.TenantId == req.TenantId);
        if (existing is not null) return Task.FromResult(existing);
        var a = new BillingAccount(Guid.NewGuid(), req.TenantId, req.TenantType, req.BillingMode, BillingStatus.Active, req.CurrencyCode, req.Timezone, req.InvoiceOwnerDisplay, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _accounts.Add(a);
        return Task.FromResult(a);
    }

    public Task<BillingAccount?> GetAccountByTenantAsync(Guid tenantId, CancellationToken ct)
        => Task.FromResult(_accounts.FirstOrDefault(a => a.TenantId == tenantId));

    public Task<BillingAccount?> GetAccountAsync(Guid billingAccountId, CancellationToken ct)
        => Task.FromResult(_accounts.FirstOrDefault(a => a.Id == billingAccountId));

    public Task<PackageSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest req, CancellationToken ct)
    {
        var s = new PackageSubscription(Guid.NewGuid(), req.TenantId, req.BillingAccountId, req.PackageDefinitionVersionId, SubscriptionState.PendingActivation, null, null, req.RenewalPolicy, null, null, DateTimeOffset.UtcNow);
        _subscriptions.Add(s);
        return Task.FromResult(s);
    }

    public Task<PackageSubscription?> GetActiveSubscriptionAsync(Guid tenantId, CancellationToken ct)
        => Task.FromResult(_subscriptions.FirstOrDefault(s => s.TenantId == tenantId && (s.State == SubscriptionState.PendingActivation || s.State == SubscriptionState.Active)));

    public Task<PackageSubscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken ct)
        => Task.FromResult(_subscriptions.FirstOrDefault(s => s.Id == subscriptionId));

    public Task<bool> ActivateSubscriptionAsync(Guid subscriptionId, DateTimeOffset termStartUtc, DateTimeOffset termEndUtc, CancellationToken ct)
    {
        var idx = _subscriptions.FindIndex(s => s.Id == subscriptionId);
        if (idx < 0) return Task.FromResult(false);
        _subscriptions[idx] = _subscriptions[idx] with { State = SubscriptionState.Active, TermStartUtc = termStartUtc, TermEndUtc = termEndUtc };
        return Task.FromResult(true);
    }

    public Task<LedgerEntry?> FindLedgerEntryByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct)
        => Task.FromResult(_ledger.FirstOrDefault(e => e.TenantId == tenantId && e.IdempotencyKey == idempotencyKey));

    public Task<LedgerEntry> AppendLedgerEntryAsync(
        Guid tenantId, Guid billingAccountId, string entryType, string meterCode,
        decimal quantity, decimal unitPriceSnapshot, decimal amountTotal, string currencyCode,
        string catalogVersionCodeSnapshot, string? packageCodeSnapshot, string idempotencyKey,
        string? sourceEventId, DateTimeOffset? sourceEventTimeUtc, Guid? actorUserId,
        string? attributionMetadataJson, CancellationToken ct)
    {
        var e = new LedgerEntry(Guid.NewGuid(), tenantId, billingAccountId, entryType, meterCode,
            quantity, unitPriceSnapshot, amountTotal, currencyCode, catalogVersionCodeSnapshot,
            packageCodeSnapshot, idempotencyKey, sourceEventId, sourceEventTimeUtc, actorUserId,
            attributionMetadataJson, DateTimeOffset.UtcNow);
        _ledger.Add(e);
        return Task.FromResult(e);
    }

    public Task<IReadOnlyList<LedgerEntry>> ListLedgerEntriesAsync(Guid tenantId, string meterCode, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LedgerEntry>>(_ledger.Where(e => e.TenantId == tenantId && e.MeterCode == meterCode && e.RecordedAtUtc >= fromUtc && e.RecordedAtUtc < toUtc).ToList());

    public Task<decimal> SumUsageForMeterAsync(Guid tenantId, string meterCode, DateTimeOffset fromUtc, DateTimeOffset toUtc, IReadOnlyList<string> entryTypes, CancellationToken ct)
    {
        var sum = _ledger.Where(e => e.TenantId == tenantId && e.MeterCode == meterCode && e.RecordedAtUtc >= fromUtc && e.RecordedAtUtc < toUtc && entryTypes.Contains(e.EntryType))
                         .Sum(e => e.Quantity);
        return Task.FromResult(sum);
    }

    public Task<CreditBalance?> GetCreditBalanceAsync(Guid tenantId, CancellationToken ct)
        => Task.FromResult(_creditBalances.FirstOrDefault(b => b.TenantId == tenantId));

    public Task<CreditBalance> GetOrCreateCreditBalanceAsync(Guid tenantId, Guid billingAccountId, string currencyCode, CancellationToken ct)
    {
        var existing = _creditBalances.FirstOrDefault(b => b.TenantId == tenantId);
        if (existing is not null) return Task.FromResult(existing);
        var b = new CreditBalance(Guid.NewGuid(), tenantId, billingAccountId, 0, 0, currencyCode, DateTimeOffset.UtcNow);
        _creditBalances.Add(b);
        return Task.FromResult(b);
    }

    public Task<CreditBalance> AdjustCreditBalanceAsync(Guid tenantId, decimal delta, CancellationToken ct)
    {
        var idx = _creditBalances.FindIndex(b => b.TenantId == tenantId);
        if (idx < 0) throw new InvalidOperationException("No credit balance");
        var updated = _creditBalances[idx] with { AvailableAmount = _creditBalances[idx].AvailableAmount + delta, UpdatedAtUtc = DateTimeOffset.UtcNow };
        _creditBalances[idx] = updated;
        return Task.FromResult(updated);
    }

    public Task<CreditMovement?> FindCreditMovementByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct)
        => Task.FromResult(_creditMovements.FirstOrDefault(m => m.TenantId == tenantId && m.IdempotencyKey == idempotencyKey));

    public Task<CreditMovement> AppendCreditMovementAsync(Guid tenantId, Guid creditBalanceId, string movementType, decimal amount, string idempotencyKey, Guid? referenceLedgerEntryId, CancellationToken ct)
    {
        var m = new CreditMovement(Guid.NewGuid(), tenantId, creditBalanceId, movementType, amount, idempotencyKey, referenceLedgerEntryId, DateTimeOffset.UtcNow);
        _creditMovements.Add(m);
        return Task.FromResult(m);
    }

    public Task<Invoice?> FindInvoiceForCycleAsync(Guid tenantId, DateTimeOffset cycleStartUtc, DateTimeOffset cycleEndUtc, CancellationToken ct)
        => Task.FromResult(_invoices.FirstOrDefault(i => i.TenantId == tenantId && i.CycleStartUtc == cycleStartUtc && i.CycleEndUtc == cycleEndUtc));

    public Task<Invoice> CreateInvoiceAsync(Guid tenantId, Guid billingAccountId, string invoiceNumber, DateTimeOffset cycleStartUtc, DateTimeOffset cycleEndUtc, decimal subtotalAmount, decimal adjustmentsAmount, decimal totalAmount, string currencyCode, CancellationToken ct)
    {
        var inv = new Invoice(Guid.NewGuid(), tenantId, billingAccountId, invoiceNumber, cycleStartUtc, cycleEndUtc, null, InvoiceStatus.Draft, subtotalAmount, adjustmentsAmount, totalAmount, currencyCode, DateTimeOffset.UtcNow);
        _invoices.Add(inv);
        return Task.FromResult(inv);
    }

    public Task FinalizeInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var idx = _invoices.FindIndex(i => i.Id == invoiceId);
        if (idx >= 0) _invoices[idx] = _invoices[idx] with { Status = InvoiceStatus.Finalized, CycleClosedAtUtc = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Invoice>> ListInvoicesAsync(Guid tenantId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Invoice>>(_invoices.Where(i => i.TenantId == tenantId).ToList());

    public Task<InvoiceLine> AppendInvoiceLineAsync(Guid invoiceId, Guid tenantId, string lineType, string? meterCode, string description, decimal quantity, decimal unitPrice, decimal lineTotal, string ledgerEntryRefsJson, CancellationToken ct)
    {
        var line = new InvoiceLine(Guid.NewGuid(), invoiceId, tenantId, lineType, meterCode, description, quantity, unitPrice, lineTotal, ledgerEntryRefsJson, DateTimeOffset.UtcNow);
        _invoiceLines.Add(line);
        return Task.FromResult(line);
    }

    public Task<IReadOnlyList<InvoiceLine>> ListInvoiceLinesAsync(Guid invoiceId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<InvoiceLine>>(_invoiceLines.Where(l => l.InvoiceId == invoiceId).ToList());

    public Task AppendAuditEventAsync(Guid tenantId, string eventType, string? entityType, Guid? entityId, string? reasonCode, string? actorType, Guid? actorId, string? correlationId, string? payloadJson, CancellationToken ct)
        => Task.CompletedTask;
}
