using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Billing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

public sealed class BillingFunctions
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IBillingRepository _repo;
    private readonly IEntitlementService _entitlement;
    private readonly IBillingCycleService _cycle;
    private readonly ILogger<BillingFunctions> _logger;

    public BillingFunctions(
        IBillingRepository repo,
        IEntitlementService entitlement,
        IBillingCycleService cycle,
        ILogger<BillingFunctions> logger)
    {
        _repo = repo;
        _entitlement = entitlement;
        _cycle = cycle;
        _logger = logger;
    }

    // ── Usage ─────────────────────────────────────────────────────────────────

    [Function("BillingRecordUsage")]
    public async Task<HttpResponseData> RecordUsageAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/usage/record")] HttpRequestData req,
        CancellationToken ct)
    {
        RecordUsageRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<RecordUsageRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.MeterCode))
            return await BadRequestAsync(req, "tenantId and meterCode are required.");

        var result = await _entitlement.RecordUsageAsync(request, ct);
        return await OkAsync(req, result);
    }

    // ── Entitlements ──────────────────────────────────────────────────────────

    [Function("BillingGetEntitlements")]
    public async Task<HttpResponseData> GetEntitlementsAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "billing/entitlements/{tenantId}")] HttpRequestData req,
        Guid tenantId,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            return await BadRequestAsync(req, "tenantId is required.");

        var snapshot = await _entitlement.GetEntitlementSnapshotAsync(tenantId, ct);
        return await OkAsync(req, snapshot);
    }

    // ── Invoices ──────────────────────────────────────────────────────────────

    [Function("BillingGenerateInvoice")]
    public async Task<HttpResponseData> GenerateInvoiceAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/invoices/generate")] HttpRequestData req,
        CancellationToken ct)
    {
        GenerateInvoiceRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<GenerateInvoiceRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || request.TenantId == Guid.Empty)
            return await BadRequestAsync(req, "tenantId is required.");

        try
        {
            var invoice = await _cycle.GenerateInvoiceAsync(request, ct);
            return await OkAsync(req, invoice);
        }
        catch (InvalidOperationException ex)
        {
            return await BadRequestAsync(req, ex.Message);
        }
    }

    [Function("BillingListInvoices")]
    public async Task<HttpResponseData> ListInvoicesAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "billing/invoices")] HttpRequestData req,
        CancellationToken ct)
    {
        var tenantIdStr = req.Query["tenantId"];
        if (!Guid.TryParse(tenantIdStr, out var tenantId))
            return await BadRequestAsync(req, "tenantId query parameter is required.");

        var invoices = await _repo.ListInvoicesAsync(tenantId, ct);
        return await OkAsync(req, invoices);
    }

    // ── Credits ───────────────────────────────────────────────────────────────

    [Function("BillingTopUpCredit")]
    public async Task<HttpResponseData> TopUpCreditAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/credits/topup")] HttpRequestData req,
        CancellationToken ct)
    {
        TopUpCreditRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<TopUpCreditRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || request.TenantId == Guid.Empty || request.Amount <= 0)
            return await BadRequestAsync(req, "tenantId and a positive amount are required.");

        try
        {
            var balance = await _cycle.TopUpCreditAsync(request, ct);
            return await OkAsync(req, balance);
        }
        catch (InvalidOperationException ex)
        {
            return await BadRequestAsync(req, ex.Message);
        }
    }

    [Function("BillingGetCreditBalance")]
    public async Task<HttpResponseData> GetCreditBalanceAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "billing/credits/{tenantId}")] HttpRequestData req,
        Guid tenantId,
        CancellationToken ct)
    {
        var balance = await _repo.GetCreditBalanceAsync(tenantId, ct);
        if (balance is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            return notFound;
        }

        return await OkAsync(req, balance);
    }

    // ── Setup endpoints ───────────────────────────────────────────────────────

    [Function("BillingCreateAccount")]
    public async Task<HttpResponseData> CreateAccountAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/accounts")] HttpRequestData req,
        CancellationToken ct)
    {
        CreateBillingAccountRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreateBillingAccountRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || request.TenantId == Guid.Empty)
            return await BadRequestAsync(req, "tenantId is required.");

        var account = await _repo.CreateAccountAsync(request, ct);
        return await OkAsync(req, account);
    }

    [Function("BillingGetAccount")]
    public async Task<HttpResponseData> GetAccountAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "billing/accounts/{tenantId}")] HttpRequestData req,
        Guid tenantId,
        CancellationToken ct)
    {
        var account = await _repo.GetAccountByTenantAsync(tenantId, ct);
        if (account is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        return await OkAsync(req, account);
    }

    [Function("BillingCreateSubscription")]
    public async Task<HttpResponseData> CreateSubscriptionAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/subscriptions")] HttpRequestData req,
        CancellationToken ct)
    {
        CreateSubscriptionRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreateSubscriptionRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || request.TenantId == Guid.Empty || request.BillingAccountId == Guid.Empty)
            return await BadRequestAsync(req, "tenantId and billingAccountId are required.");

        var sub = await _repo.CreateSubscriptionAsync(request, ct);
        return await OkAsync(req, sub);
    }

    [Function("BillingCreateCatalogVersion")]
    public async Task<HttpResponseData> CreateCatalogVersionAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/catalog/versions")] HttpRequestData req,
        CancellationToken ct)
    {
        CreateCatalogVersionRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreateCatalogVersionRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.VersionCode))
            return await BadRequestAsync(req, "versionCode is required.");

        var version = await _repo.CreateCatalogVersionAsync(request, ct);
        return await OkAsync(req, version);
    }

    [Function("BillingCreateMeterPrice")]
    public async Task<HttpResponseData> CreateMeterPriceAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/catalog/meter-prices")] HttpRequestData req,
        CancellationToken ct)
    {
        CreateMeterPriceRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreateMeterPriceRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.MeterCode))
            return await BadRequestAsync(req, "meterCode is required.");

        var price = await _repo.CreateMeterPriceAsync(request, ct);
        return await OkAsync(req, price);
    }

    [Function("BillingCreatePackageDefinition")]
    public async Task<HttpResponseData> CreatePackageDefinitionAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/catalog/packages")] HttpRequestData req,
        CancellationToken ct)
    {
        CreatePackageDefinitionRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreatePackageDefinitionRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.PackageCode))
            return await BadRequestAsync(req, "packageCode is required.");

        var pkg = await _repo.CreatePackageDefinitionAsync(request, ct);
        return await OkAsync(req, pkg);
    }

    [Function("BillingCreateQuotaRule")]
    public async Task<HttpResponseData> CreateQuotaRuleAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "billing/catalog/quota-rules")] HttpRequestData req,
        CancellationToken ct)
    {
        CreatePackageQuotaRuleRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreatePackageQuotaRuleRequest>(req.Body, _json, ct);
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid request body.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.MeterCode))
            return await BadRequestAsync(req, "meterCode is required.");

        var rule = await _repo.CreateQuotaRuleAsync(request, ct);
        return await OkAsync(req, rule);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<HttpResponseData> OkAsync(HttpRequestData req, object value)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync(JsonSerializer.Serialize(value, _json));
        response.Headers.Add("Content-Type", "application/json");
        return response;
    }

    private static async Task<HttpResponseData> BadRequestAsync(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(JsonSerializer.Serialize(new { error = message }, _json));
        response.Headers.Add("Content-Type", "application/json");
        return response;
    }
}
