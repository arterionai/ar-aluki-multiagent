using Aluki.Runtime.Abstractions.Billing;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Host.Skills.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(DbCollection.Name)]
public sealed class BillingIntegrationTests
{
    private readonly DbCaptureFixture _fixture;

    public BillingIntegrationTests(DbCaptureFixture fixture) => _fixture = fixture;

    private (IBillingRepository repo, IEntitlementService entitlement, IBillingCycleService cycle) BuildServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = _fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<NpgsqlConnectionFactory>();
        services.AddScoped<BillingRepository>();
        services.AddScoped<IBillingRepository>(sp => sp.GetRequiredService<BillingRepository>());
        services.AddScoped<EntitlementService>();
        services.AddScoped<IEntitlementService>(sp => sp.GetRequiredService<EntitlementService>());
        services.AddScoped<BillingCycleService>();
        services.AddScoped<IBillingCycleService>(sp => sp.GetRequiredService<BillingCycleService>());
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IBillingRepository>(),
                sp.GetRequiredService<IEntitlementService>(),
                sp.GetRequiredService<IBillingCycleService>());
    }

    [Fact]
    public async Task Billing_account_create_and_get_roundtrip()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (repo, _, _) = BuildServices();

        var account = await repo.CreateAccountAsync(
            new CreateBillingAccountRequest(seeded.TenantId, TenantType.Individual, BillingMode.Payg),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal(BillingMode.Payg, account.BillingMode);
        Assert.Equal(BillingStatus.Active, account.BillingStatus);

        var fetched = await repo.GetAccountByTenantAsync(seeded.TenantId, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(account.Id, fetched.Id);
    }

    [Fact]
    public async Task PAYG_usage_records_ledger_entry()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (repo, entitlement, _) = BuildServices();

        await repo.CreateAccountAsync(
            new CreateBillingAccountRequest(seeded.TenantId, TenantType.Individual, BillingMode.Payg),
            CancellationToken.None);

        var catalog = await repo.CreateCatalogVersionAsync(
            new CreateCatalogVersionRequest("v1-integ", DateTimeOffset.UtcNow),
            CancellationToken.None);

        // Publish via direct update — in tests we bypass the publish workflow.
        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "UPDATE billing_catalog_versions SET status = 'published', published_at_utc = now() WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", catalog.Id);
        await cmd.ExecuteNonQueryAsync();

        await repo.CreateMeterPriceAsync(
            new CreateMeterPriceRequest(catalog.Id, "api_call_integ", "API Call", 0.02m),
            CancellationToken.None);

        var result = await entitlement.RecordUsageAsync(
            new RecordUsageRequest(seeded.TenantId, "api_call_integ", 5, $"key-{Guid.NewGuid():N}"),
            CancellationToken.None);

        Assert.Equal(EntitlementDecision.AllowPayg, result.Decision);
        Assert.Equal(0.10m, result.AmountCharged);
        Assert.NotNull(result.LedgerEntryId);
    }

    [Fact]
    public async Task PAYG_usage_idempotency_roundtrip()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (repo, entitlement, _) = BuildServices();

        await repo.CreateAccountAsync(
            new CreateBillingAccountRequest(seeded.TenantId, TenantType.Individual, BillingMode.Payg),
            CancellationToken.None);

        var catalog = await repo.CreateCatalogVersionAsync(
            new CreateCatalogVersionRequest("v1-idem", DateTimeOffset.UtcNow),
            CancellationToken.None);

        await using var conn = await _fixture.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "UPDATE billing_catalog_versions SET status = 'published', published_at_utc = now() WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", catalog.Id);
        await cmd.ExecuteNonQueryAsync();

        await repo.CreateMeterPriceAsync(new CreateMeterPriceRequest(catalog.Id, "sms_send", "SMS", 0.005m), CancellationToken.None);

        var key = $"idem-{Guid.NewGuid():N}";
        var r1 = await entitlement.RecordUsageAsync(new RecordUsageRequest(seeded.TenantId, "sms_send", 1, key), CancellationToken.None);
        var r2 = await entitlement.RecordUsageAsync(new RecordUsageRequest(seeded.TenantId, "sms_send", 1, key), CancellationToken.None);

        Assert.Equal(EntitlementDecision.AllowPayg, r1.Decision);
        Assert.Equal(EntitlementDecision.IdempotentNoop, r2.Decision);
        Assert.Equal(r1.LedgerEntryId, r2.LedgerEntryId);
    }

    [Fact]
    public async Task Credit_topup_and_balance_roundtrip()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (repo, _, cycle) = BuildServices();

        await repo.CreateAccountAsync(
            new CreateBillingAccountRequest(seeded.TenantId, TenantType.Individual, BillingMode.Payg),
            CancellationToken.None);

        var balance = await cycle.TopUpCreditAsync(
            new TopUpCreditRequest(seeded.TenantId, 100m, $"topup-{Guid.NewGuid():N}"),
            CancellationToken.None);

        Assert.Equal(100m, balance.AvailableAmount);

        var fetched = await repo.GetCreditBalanceAsync(seeded.TenantId, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(100m, fetched.AvailableAmount);
    }

    [Fact]
    public async Task Invoice_generation_for_empty_cycle_creates_zero_invoice()
    {
        if (!_fixture.Available) return;

        var seeded = await _fixture.SeedPrincipalAsync();
        var (repo, _, cycle) = BuildServices();

        await repo.CreateAccountAsync(
            new CreateBillingAccountRequest(seeded.TenantId, TenantType.Individual, BillingMode.Payg),
            CancellationToken.None);

        var cycleStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var cycleEnd = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var invoice = await cycle.GenerateInvoiceAsync(
            new GenerateInvoiceRequest(seeded.TenantId, cycleStart, cycleEnd),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, invoice.Id);
        Assert.Equal(InvoiceStatus.Finalized, invoice.Status);
        Assert.Equal(0m, invoice.TotalAmount);
    }
}
