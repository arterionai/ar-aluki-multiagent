using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Governance;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

public sealed class GovernanceFunctions
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IPolicyDecisionEngine _policyEngine;
    private readonly IConsentManager _consentManager;
    private readonly IGovernanceRepository _repo;

    public GovernanceFunctions(
        IPolicyDecisionEngine policyEngine,
        IConsentManager consentManager,
        IGovernanceRepository repo)
    {
        _policyEngine = policyEngine;
        _consentManager = consentManager;
        _repo = repo;
    }

    // ── Policy ────────────────────────────────────────────────────────────────

    [Function("GovernancePolicyEvaluate")]
    public async Task<HttpResponseData> EvaluatePolicyAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/policy/evaluate")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        PolicyEvaluationRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<PolicyEvaluationRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException) { payload = null; }

        if (payload is null)
            return await WriteBadRequestAsync(request, "invalid_payload", cancellationToken);

        var decision = await _policyEngine.EvaluateAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, decision, cancellationToken);
    }

    [Function("GovernancePolicyRuleCreate")]
    public async Task<HttpResponseData> CreateRuleAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/policy/rules")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        CreatePolicyRuleRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<CreatePolicyRuleRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException) { payload = null; }

        if (payload is null)
            return await WriteBadRequestAsync(request, "invalid_payload", cancellationToken);

        var rule = await _repo.CreateRuleAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.Created, rule, cancellationToken);
    }

    [Function("GovernancePolicyRuleList")]
    public async Task<HttpResponseData> ListRulesAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "governance/policy/rules")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId))
            return await WriteBadRequestAsync(request, "tenant_id_required", cancellationToken);

        var rules = await _repo.ListRulesAsync(tenantId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, rules, cancellationToken);
    }

    // ── Consent ───────────────────────────────────────────────────────────────

    [Function("GovernanceConsentGrant")]
    public async Task<HttpResponseData> GrantConsentAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/consent/grant")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        GrantConsentRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<GrantConsentRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException) { payload = null; }

        if (payload is null)
            return await WriteBadRequestAsync(request, "invalid_payload", cancellationToken);

        var record = await _consentManager.GrantAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, record, cancellationToken);
    }

    [Function("GovernanceConsentRevoke")]
    public async Task<HttpResponseData> RevokeConsentAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/consent/revoke")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        RevokeConsentRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<RevokeConsentRequest>(
                request.Body, _jsonOptions, cancellationToken);
        }
        catch (JsonException) { payload = null; }

        if (payload is null)
            return await WriteBadRequestAsync(request, "invalid_payload", cancellationToken);

        var revoked = await _consentManager.RevokeAsync(payload, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, new { revoked }, cancellationToken);
    }

    [Function("GovernanceConsentCheck")]
    public async Task<HttpResponseData> CheckConsentAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "governance/consent/check")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId) ||
            !Guid.TryParse(request.Query["grantorId"], out var grantorId) ||
            !Guid.TryParse(request.Query["granteeId"], out var granteeId))
            return await WriteBadRequestAsync(request, "tenantId_grantorId_granteeId_required", cancellationToken);

        var consentType = request.Query["consentType"];
        if (string.IsNullOrWhiteSpace(consentType))
            return await WriteBadRequestAsync(request, "consent_type_required", cancellationToken);

        var granted = await _consentManager.CheckAsync(tenantId, grantorId, granteeId, consentType, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, new { granted }, cancellationToken);
    }

    [Function("GovernanceConsentListByGrantor")]
    public async Task<HttpResponseData> ListByGrantorAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "governance/consent/by-grantor")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId) ||
            !Guid.TryParse(request.Query["grantorId"], out var grantorId))
            return await WriteBadRequestAsync(request, "tenantId_grantorId_required", cancellationToken);

        var records = await _consentManager.ListGrantedByAsync(tenantId, grantorId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, records, cancellationToken);
    }

    [Function("GovernanceConsentListByGrantee")]
    public async Task<HttpResponseData> ListByGranteeAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "governance/consent/by-grantee")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.Query["tenantId"], out var tenantId) ||
            !Guid.TryParse(request.Query["granteeId"], out var granteeId))
            return await WriteBadRequestAsync(request, "tenantId_granteeId_required", cancellationToken);

        var records = await _consentManager.ListGrantedToAsync(tenantId, granteeId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, records, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<HttpResponseData> WriteBadRequestAsync(
        HttpRequestData request, string error, CancellationToken ct)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(new { error }, ct);
        response.StatusCode = HttpStatusCode.BadRequest;
        return response;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request, HttpStatusCode statusCode, T body, CancellationToken ct)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(body, ct);
        response.StatusCode = statusCode;
        return response;
    }
}
