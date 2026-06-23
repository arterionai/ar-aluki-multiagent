using System.Net;
using System.Text.Json;
using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Capture;
using Aluki.Runtime.Capture.Failure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

/// <summary>
/// Isolated-worker HTTP ingress for inbound WhatsApp events. Validates the
/// envelope and dispatches the shared capture pipeline
/// (<see cref="WhatsAppCaptureCoordinator"/>), returning contract-compliant
/// ack/error responses. This is the deployable counterpart of the Host endpoint.
/// </summary>
public sealed class WhatsAppInboundFunction
{
    private static readonly HashSet<string> KnownPayloadTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        CapturePayloadType.Text,
        CapturePayloadType.Image,
        CapturePayloadType.Audio,
        CapturePayloadType.Forwarded,
        CapturePayloadType.Unsupported
    };

    private readonly WhatsAppCaptureCoordinator _coordinator;

    public WhatsAppInboundFunction(WhatsAppCaptureCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    [Function("WhatsAppInbound")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "channels/whatsapp/inbound")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        WhatsAppInboundEnvelope? envelope;
        try
        {
            envelope = await JsonSerializer.DeserializeAsync<WhatsAppInboundEnvelope>(
                request.Body,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope is null)
        {
            return await BadRequestAsync(
                request, Guid.NewGuid().ToString("N"), "Request body is missing or invalid JSON.", cancellationToken);
        }

        var correlationId = string.IsNullOrWhiteSpace(envelope.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : envelope.CorrelationId!;

        var validationError = Validate(envelope);
        if (validationError is not null)
        {
            return await BadRequestAsync(request, correlationId, validationError, cancellationToken);
        }

        var outcome = await _coordinator.CaptureAsync(envelope, cancellationToken);
        var mapped = CaptureFailureMapper.Map(outcome);

        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(mapped.Body, cancellationToken);
        response.StatusCode = (HttpStatusCode)mapped.StatusCode;
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

        if (!KnownPayloadTypes.Contains(envelope.Payload.Type))
        {
            return "payload.type is invalid.";
        }

        return null;
    }

    private static async Task<HttpResponseData> BadRequestAsync(
        HttpRequestData request,
        string correlationId,
        string message,
        CancellationToken cancellationToken)
    {
        var error = new CaptureError(
            Status: CaptureStatus.Rejected,
            CorrelationId: correlationId,
            Code: CaptureErrorCode.InvalidPayload,
            Message: message,
            AuditEvent: null);

        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(error, cancellationToken);
        response.StatusCode = HttpStatusCode.BadRequest;
        return response;
    }
}
