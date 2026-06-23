using Aluki.Runtime.Abstractions.Billing;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aluki.Runtime.Host.Skills.Billing;

public sealed class BillingRepository : IBillingRepository
{
    private readonly NpgsqlConnectionFactory _factory;
    private readonly ILogger<BillingRepository> _logger;

    public BillingRepository(NpgsqlConnectionFactory factory, ILogger<BillingRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    // ── Catalog ───────────────────────────────────────────────────────────────

    public async Task<CatalogVersion> CreateCatalogVersionAsync(CreateCatalogVersionRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO billing_catalog_versions (version_code, effective_from_utc, status, created_at_utc)
            VALUES (@version_code, @effective_from_utc, 'draft', now())
            RETURNING id, version_code, effective_from_utc, effective_to_utc, status, published_at_utc, created_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("version_code", request.VersionCode);
        cmd.Parameters.AddWithValue("effective_from_utc", request.EffectiveFromUtc);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadCatalogVersion(reader);
    }

    public async Task<CatalogVersion?> GetPublishedCatalogVersionAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, version_code, effective_from_utc, effective_to_utc, status, published_at_utc, created_at_utc
            FROM billing_catalog_versions
            WHERE status = 'published'
            LIMIT 1
            """, conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadCatalogVersion(reader);
    }

    public async Task<IReadOnlyList<MeterPrice>> GetMeterPricesAsync(Guid catalogVersionId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, catalog_version_id, meter_code, unit_name, unit_price, min_billable_quantity, rounding_policy, created_at_utc
            FROM meter_prices
            WHERE catalog_version_id = @catalog_version_id
            ORDER BY meter_code
            """, conn);
        cmd.Parameters.AddWithValue("catalog_version_id", catalogVersionId);

        var list = new List<MeterPrice>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadMeterPrice(reader));
        return list;
    }

    public async Task<MeterPrice> CreateMeterPriceAsync(CreateMeterPriceRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO meter_prices (catalog_version_id, meter_code, unit_name, unit_price, min_billable_quantity, rounding_policy, created_at_utc)
            VALUES (@catalog_version_id, @meter_code, @unit_name, @unit_price, @min_billable_quantity, @rounding_policy, now())
            RETURNING id, catalog_version_id, meter_code, unit_name, unit_price, min_billable_quantity, rounding_policy, created_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("catalog_version_id", request.CatalogVersionId);
        cmd.Parameters.AddWithValue("meter_code", request.MeterCode);
        cmd.Parameters.AddWithValue("unit_name", request.UnitName);
        cmd.Parameters.AddWithValue("unit_price", request.UnitPrice);
        cmd.Parameters.AddWithValue("min_billable_quantity", request.MinBillableQuantity);
        cmd.Parameters.AddWithValue("rounding_policy", request.RoundingPolicy);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadMeterPrice(reader);
    }

    public async Task<PackageDefinitionVersion> CreatePackageDefinitionAsync(CreatePackageDefinitionRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO package_definition_versions
                (catalog_version_id, package_code, package_name, billing_term, base_price, overage_policy, grace_days, created_at_utc)
            VALUES (@catalog_version_id, @package_code, @package_name, @billing_term, @base_price, @overage_policy, @grace_days, now())
            RETURNING id, catalog_version_id, package_code, package_name, billing_term, base_price, overage_policy, grace_days, created_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("catalog_version_id", request.CatalogVersionId);
        cmd.Parameters.AddWithValue("package_code", request.PackageCode);
        cmd.Parameters.AddWithValue("package_name", request.PackageName);
        cmd.Parameters.AddWithValue("billing_term", request.BillingTerm);
        cmd.Parameters.AddWithValue("base_price", request.BasePrice);
        cmd.Parameters.AddWithValue("overage_policy", request.OveragePolicy);
        cmd.Parameters.AddWithValue("grace_days", request.GraceDays);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadPackageDefinition(reader);
    }

    public async Task<PackageDefinitionVersion?> GetPackageDefinitionAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, catalog_version_id, package_code, package_name, billing_term, base_price, overage_policy, grace_days, created_at_utc
            FROM package_definition_versions
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadPackageDefinition(reader);
    }

    public async Task<IReadOnlyList<PackageQuotaRule>> GetQuotaRulesAsync(Guid packageDefinitionVersionId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, package_definition_version_id, meter_code, included_quantity, overage_unit_price, hard_stop_reason_code, created_at_utc
            FROM package_quota_rules
            WHERE package_definition_version_id = @pkg_id
            ORDER BY meter_code
            """, conn);
        cmd.Parameters.AddWithValue("pkg_id", packageDefinitionVersionId);

        var list = new List<PackageQuotaRule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadQuotaRule(reader));
        return list;
    }

    public async Task<PackageQuotaRule> CreateQuotaRuleAsync(CreatePackageQuotaRuleRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO package_quota_rules
                (package_definition_version_id, meter_code, included_quantity, overage_unit_price, hard_stop_reason_code, created_at_utc)
            VALUES (@pkg_id, @meter_code, @included_quantity, @overage_unit_price, @hard_stop_reason_code, now())
            RETURNING id, package_definition_version_id, meter_code, included_quantity, overage_unit_price, hard_stop_reason_code, created_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("pkg_id", request.PackageDefinitionVersionId);
        cmd.Parameters.AddWithValue("meter_code", request.MeterCode);
        cmd.Parameters.AddWithValue("included_quantity", request.IncludedQuantity);
        cmd.Parameters.AddWithValue("overage_unit_price", (object?)request.OverageUnitPrice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hard_stop_reason_code", (object?)request.HardStopReasonCode ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadQuotaRule(reader);
    }

    // ── Billing account ───────────────────────────────────────────────────────

    public async Task<BillingAccount> CreateAccountAsync(CreateBillingAccountRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO billing_accounts
                (tenant_id, tenant_type, billing_mode, billing_status, currency_code, timezone,
                 invoice_owner_display, chargeback_enabled, created_at_utc, updated_at_utc)
            VALUES (@tenant_id, @tenant_type, @billing_mode, 'active', @currency_code, @timezone,
                    @invoice_owner_display, false, now(), now())
            ON CONFLICT (tenant_id) DO UPDATE SET updated_at_utc = EXCLUDED.updated_at_utc
            RETURNING id, tenant_id, tenant_type, billing_mode, billing_status, currency_code, timezone,
                      invoice_owner_display, chargeback_enabled, created_at_utc, updated_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", request.TenantId);
        cmd.Parameters.AddWithValue("tenant_type", request.TenantType);
        cmd.Parameters.AddWithValue("billing_mode", request.BillingMode);
        cmd.Parameters.AddWithValue("currency_code", request.CurrencyCode);
        cmd.Parameters.AddWithValue("timezone", request.Timezone);
        cmd.Parameters.AddWithValue("invoice_owner_display", (object?)request.InvoiceOwnerDisplay ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadBillingAccount(reader);
    }

    public async Task<BillingAccount?> GetAccountByTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, tenant_type, billing_mode, billing_status, currency_code, timezone,
                   invoice_owner_display, chargeback_enabled, created_at_utc, updated_at_utc
            FROM billing_accounts
            WHERE tenant_id = @tenant_id
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadBillingAccount(reader);
    }

    public async Task<BillingAccount?> GetAccountAsync(Guid billingAccountId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, tenant_type, billing_mode, billing_status, currency_code, timezone,
                   invoice_owner_display, chargeback_enabled, created_at_utc, updated_at_utc
            FROM billing_accounts
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", billingAccountId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadBillingAccount(reader);
    }

    // ── Subscriptions ─────────────────────────────────────────────────────────

    public async Task<PackageSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO package_subscriptions
                (tenant_id, billing_account_id, package_definition_version_id, state, renewal_policy, created_at_utc)
            VALUES (@tenant_id, @billing_account_id, @pkg_id, 'pending_activation', @renewal_policy, now())
            RETURNING id, tenant_id, billing_account_id, package_definition_version_id, state,
                      term_start_utc, term_end_utc, renewal_policy,
                      scheduled_change_package_definition_version_id, canceled_at_utc, created_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", request.TenantId);
        cmd.Parameters.AddWithValue("billing_account_id", request.BillingAccountId);
        cmd.Parameters.AddWithValue("pkg_id", request.PackageDefinitionVersionId);
        cmd.Parameters.AddWithValue("renewal_policy", request.RenewalPolicy);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadSubscription(reader);
    }

    public async Task<PackageSubscription?> GetActiveSubscriptionAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, billing_account_id, package_definition_version_id, state,
                   term_start_utc, term_end_utc, renewal_policy,
                   scheduled_change_package_definition_version_id, canceled_at_utc, created_at_utc
            FROM package_subscriptions
            WHERE tenant_id = @tenant_id AND state IN ('pending_activation', 'active')
            ORDER BY created_at_utc DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadSubscription(reader);
    }

    public async Task<PackageSubscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, billing_account_id, package_definition_version_id, state,
                   term_start_utc, term_end_utc, renewal_policy,
                   scheduled_change_package_definition_version_id, canceled_at_utc, created_at_utc
            FROM package_subscriptions
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", subscriptionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadSubscription(reader);
    }

    public async Task<bool> ActivateSubscriptionAsync(Guid subscriptionId, DateTimeOffset termStartUtc, DateTimeOffset termEndUtc, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE package_subscriptions
            SET state = 'active', term_start_utc = @term_start, term_end_utc = @term_end
            WHERE id = @id AND state = 'pending_activation'
            """, conn);
        cmd.Parameters.AddWithValue("id", subscriptionId);
        cmd.Parameters.AddWithValue("term_start", termStartUtc);
        cmd.Parameters.AddWithValue("term_end", termEndUtc);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ── Ledger ────────────────────────────────────────────────────────────────

    public async Task<LedgerEntry?> FindLedgerEntryByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, billing_account_id, entry_type, meter_code, quantity,
                   unit_price_snapshot, amount_total, currency_code,
                   catalog_version_code_snapshot, package_code_snapshot, idempotency_key,
                   source_event_id, source_event_time_utc, actor_user_id,
                   attribution_metadata_json, recorded_at_utc
            FROM billing_ledger_entries
            WHERE tenant_id = @tenant_id AND idempotency_key = @key
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("key", idempotencyKey);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadLedgerEntry(reader);
    }

    public async Task<LedgerEntry> AppendLedgerEntryAsync(
        Guid tenantId, Guid billingAccountId, string entryType, string meterCode,
        decimal quantity, decimal unitPriceSnapshot, decimal amountTotal, string currencyCode,
        string catalogVersionCodeSnapshot, string? packageCodeSnapshot, string idempotencyKey,
        string? sourceEventId, DateTimeOffset? sourceEventTimeUtc, Guid? actorUserId,
        string? attributionMetadataJson, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO billing_ledger_entries
                (tenant_id, billing_account_id, entry_type, meter_code, quantity,
                 unit_price_snapshot, amount_total, currency_code,
                 catalog_version_code_snapshot, package_code_snapshot, idempotency_key,
                 source_event_id, source_event_time_utc, actor_user_id,
                 attribution_metadata_json, recorded_at_utc)
            VALUES
                (@tenant_id, @billing_account_id, @entry_type, @meter_code, @quantity,
                 @unit_price, @amount_total, @currency_code,
                 @catalog_version_code, @package_code, @idempotency_key,
                 @source_event_id, @source_event_time, @actor_user_id,
                 @attribution::jsonb, now())
            RETURNING id, tenant_id, billing_account_id, entry_type, meter_code, quantity,
                      unit_price_snapshot, amount_total, currency_code,
                      catalog_version_code_snapshot, package_code_snapshot, idempotency_key,
                      source_event_id, source_event_time_utc, actor_user_id,
                      attribution_metadata_json, recorded_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("billing_account_id", billingAccountId);
        cmd.Parameters.AddWithValue("entry_type", entryType);
        cmd.Parameters.AddWithValue("meter_code", meterCode);
        cmd.Parameters.AddWithValue("quantity", quantity);
        cmd.Parameters.AddWithValue("unit_price", unitPriceSnapshot);
        cmd.Parameters.AddWithValue("amount_total", amountTotal);
        cmd.Parameters.AddWithValue("currency_code", currencyCode);
        cmd.Parameters.AddWithValue("catalog_version_code", catalogVersionCodeSnapshot);
        cmd.Parameters.AddWithValue("package_code", (object?)packageCodeSnapshot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        cmd.Parameters.AddWithValue("source_event_id", (object?)sourceEventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("source_event_time", (object?)sourceEventTimeUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actor_user_id", (object?)actorUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("attribution", (object?)attributionMetadataJson ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadLedgerEntry(reader);
    }

    public async Task<IReadOnlyList<LedgerEntry>> ListLedgerEntriesAsync(
        Guid tenantId, string meterCode, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, billing_account_id, entry_type, meter_code, quantity,
                   unit_price_snapshot, amount_total, currency_code,
                   catalog_version_code_snapshot, package_code_snapshot, idempotency_key,
                   source_event_id, source_event_time_utc, actor_user_id,
                   attribution_metadata_json, recorded_at_utc
            FROM billing_ledger_entries
            WHERE tenant_id = @tenant_id AND meter_code = @meter_code
              AND recorded_at_utc >= @from_utc AND recorded_at_utc < @to_utc
            ORDER BY recorded_at_utc ASC
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("meter_code", meterCode);
        cmd.Parameters.AddWithValue("from_utc", fromUtc);
        cmd.Parameters.AddWithValue("to_utc", toUtc);

        var list = new List<LedgerEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadLedgerEntry(reader));
        return list;
    }

    public async Task<decimal> SumUsageForMeterAsync(
        Guid tenantId, string meterCode, DateTimeOffset fromUtc, DateTimeOffset toUtc,
        IReadOnlyList<string> entryTypes, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(quantity), 0)
            FROM billing_ledger_entries
            WHERE tenant_id = @tenant_id AND meter_code = @meter_code
              AND recorded_at_utc >= @from_utc AND recorded_at_utc < @to_utc
              AND entry_type = ANY(@entry_types)
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("meter_code", meterCode);
        cmd.Parameters.AddWithValue("from_utc", fromUtc);
        cmd.Parameters.AddWithValue("to_utc", toUtc);
        cmd.Parameters.AddWithValue("entry_types", entryTypes.ToArray());

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToDecimal(result);
    }

    // ── Credits ───────────────────────────────────────────────────────────────

    public async Task<CreditBalance?> GetCreditBalanceAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, billing_account_id, available_amount, reserved_amount, currency_code, updated_at_utc
            FROM credit_balances
            WHERE tenant_id = @tenant_id
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadCreditBalance(reader);
    }

    public async Task<CreditBalance> GetOrCreateCreditBalanceAsync(Guid tenantId, Guid billingAccountId, string currencyCode, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO credit_balances (tenant_id, billing_account_id, available_amount, reserved_amount, currency_code, updated_at_utc)
            VALUES (@tenant_id, @billing_account_id, 0, 0, @currency_code, now())
            ON CONFLICT (tenant_id) DO UPDATE SET updated_at_utc = credit_balances.updated_at_utc
            RETURNING id, tenant_id, billing_account_id, available_amount, reserved_amount, currency_code, updated_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("billing_account_id", billingAccountId);
        cmd.Parameters.AddWithValue("currency_code", currencyCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadCreditBalance(reader);
    }

    public async Task<CreditBalance> AdjustCreditBalanceAsync(Guid tenantId, decimal delta, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE credit_balances
            SET available_amount = available_amount + @delta, updated_at_utc = now()
            WHERE tenant_id = @tenant_id
            RETURNING id, tenant_id, billing_account_id, available_amount, reserved_amount, currency_code, updated_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("delta", delta);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadCreditBalance(reader);
    }

    public async Task<CreditMovement?> FindCreditMovementByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, credit_balance_id, movement_type, amount, idempotency_key, reference_ledger_entry_id, created_at_utc
            FROM credit_movements
            WHERE tenant_id = @tenant_id AND idempotency_key = @key
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("key", idempotencyKey);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadCreditMovement(reader);
    }

    public async Task<CreditMovement> AppendCreditMovementAsync(
        Guid tenantId, Guid creditBalanceId, string movementType, decimal amount,
        string idempotencyKey, Guid? referenceLedgerEntryId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO credit_movements (tenant_id, credit_balance_id, movement_type, amount, idempotency_key, reference_ledger_entry_id, created_at_utc)
            VALUES (@tenant_id, @credit_balance_id, @movement_type, @amount, @idempotency_key, @ref_ledger_entry_id, now())
            RETURNING id, tenant_id, credit_balance_id, movement_type, amount, idempotency_key, reference_ledger_entry_id, created_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("credit_balance_id", creditBalanceId);
        cmd.Parameters.AddWithValue("movement_type", movementType);
        cmd.Parameters.AddWithValue("amount", amount);
        cmd.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        cmd.Parameters.AddWithValue("ref_ledger_entry_id", (object?)referenceLedgerEntryId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadCreditMovement(reader);
    }

    // ── Invoices ──────────────────────────────────────────────────────────────

    public async Task<Invoice?> FindInvoiceForCycleAsync(Guid tenantId, DateTimeOffset cycleStartUtc, DateTimeOffset cycleEndUtc, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, billing_account_id, invoice_number, cycle_start_utc, cycle_end_utc,
                   cycle_closed_at_utc, status, subtotal_amount, adjustments_amount, total_amount,
                   currency_code, generated_at_utc
            FROM invoices
            WHERE tenant_id = @tenant_id AND cycle_start_utc = @cycle_start AND cycle_end_utc = @cycle_end
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("cycle_start", cycleStartUtc);
        cmd.Parameters.AddWithValue("cycle_end", cycleEndUtc);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadInvoice(reader);
    }

    public async Task<Invoice> CreateInvoiceAsync(
        Guid tenantId, Guid billingAccountId, string invoiceNumber,
        DateTimeOffset cycleStartUtc, DateTimeOffset cycleEndUtc,
        decimal subtotalAmount, decimal adjustmentsAmount, decimal totalAmount,
        string currencyCode, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO invoices
                (tenant_id, billing_account_id, invoice_number, cycle_start_utc, cycle_end_utc,
                 status, subtotal_amount, adjustments_amount, total_amount, currency_code, generated_at_utc)
            VALUES
                (@tenant_id, @billing_account_id, @invoice_number, @cycle_start, @cycle_end,
                 'draft', @subtotal, @adjustments, @total, @currency_code, now())
            RETURNING id, tenant_id, billing_account_id, invoice_number, cycle_start_utc, cycle_end_utc,
                      cycle_closed_at_utc, status, subtotal_amount, adjustments_amount, total_amount,
                      currency_code, generated_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("billing_account_id", billingAccountId);
        cmd.Parameters.AddWithValue("invoice_number", invoiceNumber);
        cmd.Parameters.AddWithValue("cycle_start", cycleStartUtc);
        cmd.Parameters.AddWithValue("cycle_end", cycleEndUtc);
        cmd.Parameters.AddWithValue("subtotal", subtotalAmount);
        cmd.Parameters.AddWithValue("adjustments", adjustmentsAmount);
        cmd.Parameters.AddWithValue("total", totalAmount);
        cmd.Parameters.AddWithValue("currency_code", currencyCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadInvoice(reader);
    }

    public async Task FinalizeInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE invoices
            SET status = 'finalized', cycle_closed_at_utc = now()
            WHERE id = @id AND status = 'draft'
            """, conn);
        cmd.Parameters.AddWithValue("id", invoiceId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Invoice>> ListInvoicesAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, billing_account_id, invoice_number, cycle_start_utc, cycle_end_utc,
                   cycle_closed_at_utc, status, subtotal_amount, adjustments_amount, total_amount,
                   currency_code, generated_at_utc
            FROM invoices
            WHERE tenant_id = @tenant_id
            ORDER BY cycle_start_utc DESC
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        var list = new List<Invoice>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadInvoice(reader));
        return list;
    }

    public async Task<InvoiceLine> AppendInvoiceLineAsync(
        Guid invoiceId, Guid tenantId, string lineType, string? meterCode,
        string description, decimal quantity, decimal unitPrice, decimal lineTotal,
        string ledgerEntryRefsJson, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO invoice_lines
                (invoice_id, tenant_id, line_type, meter_code, description, quantity, unit_price, line_total, ledger_entry_refs_json, created_at_utc)
            VALUES
                (@invoice_id, @tenant_id, @line_type, @meter_code, @description, @quantity, @unit_price, @line_total, @refs::jsonb, now())
            RETURNING id, invoice_id, tenant_id, line_type, meter_code, description, quantity, unit_price, line_total, ledger_entry_refs_json, created_at_utc
            """, conn);
        cmd.Parameters.AddWithValue("invoice_id", invoiceId);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("line_type", lineType);
        cmd.Parameters.AddWithValue("meter_code", (object?)meterCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("description", description);
        cmd.Parameters.AddWithValue("quantity", quantity);
        cmd.Parameters.AddWithValue("unit_price", unitPrice);
        cmd.Parameters.AddWithValue("line_total", lineTotal);
        cmd.Parameters.AddWithValue("refs", ledgerEntryRefsJson);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadInvoiceLine(reader);
    }

    public async Task<IReadOnlyList<InvoiceLine>> ListInvoiceLinesAsync(Guid invoiceId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, invoice_id, tenant_id, line_type, meter_code, description, quantity, unit_price, line_total, ledger_entry_refs_json, created_at_utc
            FROM invoice_lines
            WHERE invoice_id = @invoice_id
            ORDER BY created_at_utc ASC
            """, conn);
        cmd.Parameters.AddWithValue("invoice_id", invoiceId);

        var list = new List<InvoiceLine>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadInvoiceLine(reader));
        return list;
    }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public async Task AppendAuditEventAsync(
        Guid tenantId, string eventType, string? entityType, Guid? entityId,
        string? reasonCode, string? actorType, Guid? actorId,
        string? correlationId, string? payloadJson, CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO billing_audit_events
                (tenant_id, event_type, entity_type, entity_id, reason_code, actor_type, actor_id, correlation_id, payload_json, created_at_utc)
            VALUES
                (@tenant_id, @event_type, @entity_type, @entity_id, @reason_code, @actor_type, @actor_id, @correlation_id, @payload::jsonb, now())
            """, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("event_type", eventType);
        cmd.Parameters.AddWithValue("entity_type", (object?)entityType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("entity_id", (object?)entityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reason_code", (object?)reasonCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actor_type", (object?)actorType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actor_id", (object?)actorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlation_id", (object?)correlationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("payload", (object?)payloadJson ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Read helpers ──────────────────────────────────────────────────────────

    private static CatalogVersion ReadCatalogVersion(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetString(1),
               r.GetFieldValue<DateTimeOffset>(2),
               r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3),
               r.GetString(4),
               r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5),
               r.GetFieldValue<DateTimeOffset>(6));

    private static MeterPrice ReadMeterPrice(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3),
               r.GetFieldValue<decimal>(4), r.GetFieldValue<decimal>(5),
               r.GetString(6), r.GetFieldValue<DateTimeOffset>(7));

    private static PackageDefinitionVersion ReadPackageDefinition(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3),
               r.GetString(4), r.GetFieldValue<decimal>(5), r.GetString(6),
               r.GetInt32(7), r.GetFieldValue<DateTimeOffset>(8));

    private static PackageQuotaRule ReadQuotaRule(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2),
               r.GetFieldValue<decimal>(3),
               r.IsDBNull(4) ? null : r.GetFieldValue<decimal>(4),
               r.IsDBNull(5) ? null : r.GetString(5),
               r.GetFieldValue<DateTimeOffset>(6));

    private static BillingAccount ReadBillingAccount(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3),
               r.GetString(4), r.GetString(5), r.GetString(6),
               r.IsDBNull(7) ? null : r.GetString(7),
               r.GetBoolean(8),
               r.GetFieldValue<DateTimeOffset>(9),
               r.GetFieldValue<DateTimeOffset>(10));

    private static PackageSubscription ReadSubscription(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetGuid(3),
               r.GetString(4),
               r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5),
               r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6),
               r.GetString(7),
               r.IsDBNull(8) ? null : r.GetGuid(8),
               r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9),
               r.GetFieldValue<DateTimeOffset>(10));

    private static LedgerEntry ReadLedgerEntry(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3),
               r.GetString(4), r.GetFieldValue<decimal>(5),
               r.GetFieldValue<decimal>(6), r.GetFieldValue<decimal>(7),
               r.GetString(8), r.GetString(9),
               r.IsDBNull(10) ? null : r.GetString(10),
               r.GetString(11),
               r.IsDBNull(12) ? null : r.GetString(12),
               r.IsDBNull(13) ? null : r.GetFieldValue<DateTimeOffset>(13),
               r.IsDBNull(14) ? null : r.GetGuid(14),
               r.IsDBNull(15) ? null : r.GetString(15),
               r.GetFieldValue<DateTimeOffset>(16));

    private static CreditBalance ReadCreditBalance(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2),
               r.GetFieldValue<decimal>(3), r.GetFieldValue<decimal>(4),
               r.GetString(5), r.GetFieldValue<DateTimeOffset>(6));

    private static CreditMovement ReadCreditMovement(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3),
               r.GetFieldValue<decimal>(4), r.GetString(5),
               r.IsDBNull(6) ? null : r.GetGuid(6),
               r.GetFieldValue<DateTimeOffset>(7));

    private static Invoice ReadInvoice(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3),
               r.GetFieldValue<DateTimeOffset>(4), r.GetFieldValue<DateTimeOffset>(5),
               r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6),
               r.GetString(7),
               r.GetFieldValue<decimal>(8), r.GetFieldValue<decimal>(9), r.GetFieldValue<decimal>(10),
               r.GetString(11), r.GetFieldValue<DateTimeOffset>(12));

    private static InvoiceLine ReadInvoiceLine(NpgsqlDataReader r)
        => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3),
               r.IsDBNull(4) ? null : r.GetString(4),
               r.GetString(5), r.GetFieldValue<decimal>(6), r.GetFieldValue<decimal>(7),
               r.GetFieldValue<decimal>(8), r.GetString(9),
               r.GetFieldValue<DateTimeOffset>(10));
}
