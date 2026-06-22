using Aluki.Runtime.Abstractions.Billing;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Host.Skills.Billing;

public sealed class BillingCycleService : IBillingCycleService
{
    private readonly IBillingRepository _repo;
    private readonly ILogger<BillingCycleService> _logger;

    public BillingCycleService(IBillingRepository repo, ILogger<BillingCycleService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<Invoice> GenerateInvoiceAsync(GenerateInvoiceRequest request, CancellationToken ct)
    {
        var account = await _repo.GetAccountByTenantAsync(request.TenantId, ct);
        if (account is null)
            throw new InvalidOperationException($"No billing account for tenant {request.TenantId}");

        // Idempotency: return existing invoice for same cycle.
        var existing = await _repo.FindInvoiceForCycleAsync(request.TenantId, request.CycleStartUtc, request.CycleEndUtc, ct);
        if (existing is not null)
            return existing;

        // Aggregate ledger entries for the cycle (excluding denial events).
        var billableTypes = new[]
        {
            LedgerEntryType.UsagePayg,
            LedgerEntryType.UsageOverage,
            LedgerEntryType.ManualAdjustment,
            LedgerEntryType.ProrationAdjustment
        };

        // Collect meter codes from all ledger entries in the cycle.
        var catalog = await _repo.GetPublishedCatalogVersionAsync(ct);
        var prices = catalog is not null
            ? await _repo.GetMeterPricesAsync(catalog.Id, ct)
            : [];

        // Sum amounts per entry type.
        decimal subtotal = 0m;
        decimal adjustments = 0m;
        var lineItems = new List<(string LineType, string? MeterCode, string Desc, decimal Qty, decimal UnitPrice, decimal Total)>();

        var allMeterCodes = prices.Select(p => p.MeterCode).ToList();

        foreach (var meterCode in allMeterCodes)
        {
            // PAYG usage.
            var paygEntries = await _repo.ListLedgerEntriesAsync(
                request.TenantId, meterCode, request.CycleStartUtc, request.CycleEndUtc, ct);
            foreach (var grp in paygEntries.Where(e => e.EntryType == LedgerEntryType.UsagePayg)
                                           .GroupBy(e => e.UnitPriceSnapshot))
            {
                var qty = grp.Sum(e => e.Quantity);
                var lineTotal = grp.Sum(e => e.AmountTotal);
                lineItems.Add(("usage", meterCode, $"Usage: {meterCode}", qty, grp.Key, lineTotal));
                subtotal += lineTotal;
            }

            // Overage.
            foreach (var grp in paygEntries.Where(e => e.EntryType == LedgerEntryType.UsageOverage)
                                           .GroupBy(e => e.UnitPriceSnapshot))
            {
                var qty = grp.Sum(e => e.Quantity);
                var lineTotal = grp.Sum(e => e.AmountTotal);
                lineItems.Add(("overage", meterCode, $"Overage: {meterCode}", qty, grp.Key, lineTotal));
                subtotal += lineTotal;
            }

            // Adjustments.
            foreach (var adj in paygEntries.Where(e =>
                e.EntryType == LedgerEntryType.ProrationAdjustment ||
                e.EntryType == LedgerEntryType.ManualAdjustment))
            {
                var lineType = adj.EntryType == LedgerEntryType.ProrationAdjustment ? "proration" : "adjustment";
                lineItems.Add((lineType, meterCode, $"Adjustment: {meterCode}", adj.Quantity, adj.UnitPriceSnapshot, adj.AmountTotal));
                adjustments += adj.AmountTotal;
            }
        }

        var total = subtotal + adjustments;
        var invoiceNumber = $"INV-{request.TenantId.ToString("N")[..8].ToUpper()}-{request.CycleStartUtc:yyyyMMdd}";

        var invoice = await _repo.CreateInvoiceAsync(
            request.TenantId, account.Id, invoiceNumber,
            request.CycleStartUtc, request.CycleEndUtc,
            subtotal, adjustments, total, account.CurrencyCode, ct);

        // Append invoice lines.
        foreach (var (lineType, meterCode, desc, qty, unitPrice, lineTotal) in lineItems)
        {
            await _repo.AppendInvoiceLineAsync(
                invoice.Id, request.TenantId, lineType, meterCode,
                desc, qty, unitPrice, lineTotal, "[]", ct);
        }

        await _repo.FinalizeInvoiceAsync(invoice.Id, ct);

        await _repo.AppendAuditEventAsync(
            request.TenantId, "invoice_generated", "invoice", invoice.Id,
            null, "system", null, request.IdempotencyKey, null, ct);

        _logger.LogInformation("Generated invoice {InvoiceNumber} for tenant {TenantId}, total {Total}",
            invoiceNumber, request.TenantId, total);

        // Re-fetch to get finalized status.
        var finalized = await _repo.FindInvoiceForCycleAsync(request.TenantId, request.CycleStartUtc, request.CycleEndUtc, ct);
        return finalized ?? invoice;
    }

    public async Task<CreditBalance> TopUpCreditAsync(TopUpCreditRequest request, CancellationToken ct)
    {
        var account = await _repo.GetAccountByTenantAsync(request.TenantId, ct);
        if (account is null)
            throw new InvalidOperationException($"No billing account for tenant {request.TenantId}");

        // Idempotency.
        var existing = await _repo.FindCreditMovementByIdempotencyKeyAsync(request.TenantId, request.IdempotencyKey, ct);
        if (existing is not null)
            return (await _repo.GetCreditBalanceAsync(request.TenantId, ct))!;

        var balance = await _repo.GetOrCreateCreditBalanceAsync(request.TenantId, account.Id, request.CurrencyCode, ct);
        var updated = await _repo.AdjustCreditBalanceAsync(request.TenantId, request.Amount, ct);
        await _repo.AppendCreditMovementAsync(
            request.TenantId, balance.Id, CreditMovementType.Topup, request.Amount,
            request.IdempotencyKey, null, ct);

        await _repo.AppendAuditEventAsync(
            request.TenantId, "credit_topup", "credit_balance", balance.Id,
            null, "system", null, request.IdempotencyKey, null, ct);

        return updated;
    }
}
