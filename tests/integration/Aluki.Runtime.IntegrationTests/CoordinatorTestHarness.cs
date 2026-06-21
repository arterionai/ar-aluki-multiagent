using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Host.Capture;
using Aluki.Runtime.Host.Capture.Retry;
using Aluki.Runtime.Host.Capture.Skills;
using Aluki.Runtime.Host.Configuration;
using Aluki.Runtime.Host.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.IntegrationTests;

internal static class CoordinatorTestHarness
{
    public static PrincipalContext Principal() => new(
        UserId: Guid.NewGuid(),
        TenantId: Guid.NewGuid(),
        ContextId: Guid.NewGuid(),
        Roles: ["MEMBER"],
        SourceChannel: "whatsapp",
        CorrelationId: "corr-1");

    public static WhatsAppInboundEnvelope TextEnvelope(string providerMessageId = "wamid-1") => new(
        ProviderMessageId: providerMessageId,
        SourceChannel: "whatsapp",
        Sender: new SenderInfo("5215555555555", "Tester"),
        ContextMetadata: null,
        Payload: new Payload("text", "hola", Media: null, Forwarded: null, RawEnvelopeRef: "blob://raw/1"),
        OccurredAtUtc: DateTimeOffset.UtcNow,
        CorrelationId: "corr-1");

    public static WhatsAppCaptureCoordinator Build(
        FakeCaptureUnitOfWorkFactory factory,
        PrincipalContext principal,
        int maxAttempts = 5,
        bool consentStop = false)
    {
        var resolver = new FakePrincipalContextResolver(PrincipalResolution.Allow(principal));
        var retryPolicy = new CaptureRetryPolicy(Options.Create(new CaptureOptions
        {
            Retry = new RetryOptions
            {
                MaxAttempts = maxAttempts,
                BaseDelayMilliseconds = 1,
                MaxDelayMilliseconds = 2
            }
        }));
        var telemetry = new CaptureTelemetry();
        var consent = new FakeConsentStopPolicy(consentStop);

        var writeScopeDenied = new WriteScopeDeniedAuditSkill(factory, NullLogger<WriteScopeDeniedAuditSkill>.Instance);
        var writeRetry = new WriteRetryAuditSkill(factory, NullLogger<WriteRetryAuditSkill>.Instance);

        return new WhatsAppCaptureCoordinator(
            resolver,
            factory,
            retryPolicy,
            telemetry,
            new ScopeGuardSkill(consent),
            new NormalizeWhatsAppInboundSkill(),
            new IdempotencyGuardSkill(),
            new PersistCaptureSkill(),
            new PersistUnsupportedCaptureSkill(),
            new WriteCaptureAuditSkill(),
            writeScopeDenied,
            writeRetry,
            NullLogger<WhatsAppCaptureCoordinator>.Instance);
    }
}
