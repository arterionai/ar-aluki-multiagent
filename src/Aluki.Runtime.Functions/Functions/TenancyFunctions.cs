using System.Net;
using System.Text.Json;
using Aluki.Runtime.Host.Skills.Tenancy;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// HTTP endpoints for generic tenancy management:
/// - Add/remove members from an org tenant by WA id
/// - Register/unregister WA Business channel → tenant mapping
/// </summary>
public sealed class TenancyFunctions
{
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };

    private readonly TenancyRepository _tenancy;
    private readonly ILogger<TenancyFunctions> _logger;

    public TenancyFunctions(TenancyRepository tenancy, ILogger<TenancyFunctions> logger)
    {
        _tenancy = tenancy;
        _logger = logger;
    }

    // ── Add member ────────────────────────────────────────────────────────────
    // POST api/tenancy/members
    // Body: { "tenantId": "...", "waId": "525512345678", "phone": "+525512345678", "role": "MEMBER" }

    [Function("TenancyAddMember")]
    public async Task<HttpResponseData> AddMemberAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "tenancy/members")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        AddMemberRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AddMemberRequest>(
                request.Body, _json, cancellationToken);
        }
        catch
        {
            return await BadRequestAsync(request, "Invalid JSON body.");
        }

        if (body is null || body.TenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(body.WaId)
            || string.IsNullOrWhiteSpace(body.Phone))
            return await BadRequestAsync(request, "tenantId, waId, and phone are required.");

        var role = string.IsNullOrWhiteSpace(body.Role) ? "MEMBER" : body.Role.ToUpperInvariant();
        if (role is not ("OWNER" or "ADMIN" or "MEMBER" or "GUEST"))
            return await BadRequestAsync(request, $"Invalid role '{role}'. Must be OWNER, ADMIN, MEMBER, or GUEST.");

        try
        {
            var result = await _tenancy.AddMemberToTenantAsync(
                body.TenantId, body.WaId, body.Phone, role, cancellationToken);

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                userId = result.UserId,
                tenantId = result.TenantId,
                waId = result.WaId,
                role = result.Role,
                isNew = result.IsNew
            }, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TenancyFunctions.AddMember failed. waId={WaId} tenantId={TenantId}",
                body.WaId, body.TenantId);
            var err = request.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Failed to add member." }, cancellationToken);
            return err;
        }
    }

    // ── Register WA channel ───────────────────────────────────────────────────
    // POST api/tenancy/channels
    // Body: { "phoneNumberId": "123456789", "tenantId": "...", "displayName": "Sheló NABEL" }

    [Function("TenancyRegisterChannel")]
    public async Task<HttpResponseData> RegisterChannelAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "tenancy/channels")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        RegisterChannelRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<RegisterChannelRequest>(
                request.Body, _json, cancellationToken);
        }
        catch
        {
            return await BadRequestAsync(request, "Invalid JSON body.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.PhoneNumberId) || body.TenantId == Guid.Empty)
            return await BadRequestAsync(request, "phoneNumberId and tenantId are required.");

        try
        {
            await _tenancy.RegisterWhatsAppChannelAsync(
                body.PhoneNumberId, body.TenantId, body.DisplayName, cancellationToken);

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                phoneNumberId = body.PhoneNumberId,
                tenantId = body.TenantId,
                registered = true
            }, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TenancyFunctions.RegisterChannel failed. phoneNumberId={Id}", body.PhoneNumberId);
            var err = request.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Failed to register channel." }, cancellationToken);
            return err;
        }
    }

    private static async Task<HttpResponseData> BadRequestAsync(HttpRequestData request, string message)
    {
        var response = request.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}

public sealed record AddMemberRequest(
    Guid TenantId,
    string WaId,
    string Phone,
    string? Role);

public sealed record RegisterChannelRequest(
    string PhoneNumberId,
    Guid TenantId,
    string? DisplayName);
