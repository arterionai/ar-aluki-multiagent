using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Persistence;
using Aluki.Runtime.Abstractions.Security;

namespace Aluki.Runtime.Capture;

/// <summary>
/// Mutable state threaded through the capture skill sequence by the coordinator.
/// Passed by reference via <c>SkillExecutionContext.Input["state"]</c> so each
/// skill reads and contributes to the same in-flight capture.
/// </summary>
public sealed class CapturePipelineState
{
    public CapturePipelineState(PrincipalContext principal, WhatsAppInboundEnvelope envelope, string correlationId)
    {
        Principal = principal;
        Envelope = envelope;
        CorrelationId = correlationId;
    }

    public PrincipalContext Principal { get; }

    public WhatsAppInboundEnvelope Envelope { get; }

    public string CorrelationId { get; }

    public string SenderExternalId => Envelope.Sender.ExternalUserId;

    public string SourceChannel => Principal.SourceChannel;

    /// <summary>Composite idempotency key: tenant|source_channel|provider_message_id.</summary>
    public string IdempotencyKey =>
        $"{Principal.TenantId:D}|{SourceChannel}|{Envelope.ProviderMessageId}";

    public NormalizedCaptureMessage? Normalized { get; set; }

    /// <summary>Active scoped transaction for the current persistence attempt.</summary>
    public ICaptureUnitOfWork? UnitOfWork { get; set; }

    public bool IsDuplicate { get; set; }

    public bool IsUnsupported => Normalized is { IsSupported: false };

    public Guid? IdempotencyId { get; set; }

    public Guid? ProvenanceEventId { get; set; }

    public Guid? CanonicalMessageId { get; set; }

    public int AttemptNumber { get; set; }
}

/// <summary>Well-known <c>SkillExecutionContext.Input</c> keys for the capture pipeline.</summary>
public static class CaptureInputKeys
{
    public const string State = "state";
}
