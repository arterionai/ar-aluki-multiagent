using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Functions.Channels.WhatsApp;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

public sealed class WhatsAppInboundFunction
{
    private static readonly HashSet<string> SupportedPayloadTypes =
    [
        "text",
        "image",
        "audio",
        "forwarded",
        "unsupported"
    ];

    private readonly InMemoryCaptureStore _store;

    public WhatsAppInboundFunction(InMemoryCaptureStore store)
    {
        _store = store;
    }

    [Function("WhatsAppInbound")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "channels/whatsapp/inbound")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var envelope = await JsonSerializer.DeserializeAsync<WhatsAppInboundEnvelope>(
            request.Body,
            cancellationToken: cancellationToken);

        if (envelope is null)
        {
            return await CreateErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                correlationId: Guid.NewGuid().ToString("N"),
                code: CaptureErrorCode.InvalidPayload,
                message: "Request body is missing or invalid JSON.",
                auditEvent: null,
                cancellationToken);
        }

        var correlationId = string.IsNullOrWhiteSpace(envelope.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : envelope.CorrelationId!;

        var validationError = Validate(envelope);
        if (validationError is not null)
        {
            return await CreateErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                correlationId,
                CaptureErrorCode.InvalidPayload,
                validationError,
                auditEvent: null,
                cancellationToken);
        }

        if (!TryResolveTenant(envelope.ContextMetadata?.TenantHint, out var tenantId))
        {
            return await CreateErrorAsync(
                request,
                HttpStatusCode.Forbidden,
                correlationId,
                CaptureErrorCode.ScopeDenied,
                "Missing or invalid tenant_hint in context_metadata.",
                CaptureAuditEvent.ScopeDenied,
                cancellationToken);
        }

        var idempotencyKey = $"{tenantId:D}|{envelope.SourceChannel}|{envelope.ProviderMessageId}";
        var storeResult = _store.Upsert(idempotencyKey);

        var status = envelope.Payload.Type.Equals("unsupported", StringComparison.OrdinalIgnoreCase)
            ? CaptureStatus.AcceptedUnsupported
            : storeResult.IsDuplicate
                ? CaptureStatus.DuplicateSuppressed
                : CaptureStatus.Accepted;

        var auditEvent = status switch
        {
            CaptureStatus.DuplicateSuppressed => CaptureAuditEvent.DuplicateSuppressed,
            CaptureStatus.AcceptedUnsupported => CaptureAuditEvent.UnsupportedPayload,
            _ => CaptureAuditEvent.Accepted
        };

        var ack = new CaptureAck(
            Status: status,
            CorrelationId: correlationId,
            IdempotencyKey: idempotencyKey,
            CanonicalMessageId: storeResult.CanonicalMessageId,
            AuditEvent: auditEvent);

        var response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(ack, cancellationToken);
        return response;
    }

    private static string? Validate(WhatsAppInboundEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.ProviderMessageId))
        {
            return "provider_message_id is required.";
        }

        if (!"whatsapp".Equals(envelope.SourceChannel, StringComparison.OrdinalIgnoreCase))
        {
            return "source_channel must be 'whatsapp'.";
        }

        if (envelope.Sender is null || string.IsNullOrWhiteSpace(envelope.Sender.ExternalUserId))
        {
            return "sender.external_user_id is required.";
        }

        if (envelope.Payload is null || string.IsNullOrWhiteSpace(envelope.Payload.Type))
        {
            return "payload.type is required.";
        }

        if (!SupportedPayloadTypes.Contains(envelope.Payload.Type))
        {
            return "payload.type is invalid.";
        }

        return null;
    }

    private static bool TryResolveTenant(string? tenantHint, out Guid tenantId)
    {
        return Guid.TryParse(tenantHint, out tenantId);
    }

    private static async Task<HttpResponseData> CreateErrorAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string correlationId,
        string code,
        string message,
        string? auditEvent,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        var error = new CaptureError(
            Status: CaptureStatus.Rejected,
            CorrelationId: correlationId,
            Code: code,
            Message: message,
            AuditEvent: auditEvent);

        await response.WriteAsJsonAsync(error, cancellationToken);
        return response;
    }
}