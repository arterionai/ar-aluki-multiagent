using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Microsoft.Extensions.Logging;

namespace Aluki.Runtime.Memory.Dispatch;

/// <summary>
/// Catch-all fallback domain agent: ingests text messages into personal memory.
/// Registered at <see cref="int.MaxValue"/> priority so specific domain agents
/// are always evaluated first. Claims any message that carries text.
/// </summary>
public sealed class MemoryDomainAgent : IDomainAgent
{
    public const string Id = "memory.recall_and_capture";

    private readonly IMemoryIngestionSink _sink;
    private readonly ILogger<MemoryDomainAgent> _logger;

    public MemoryDomainAgent(IMemoryIngestionSink sink, ILogger<MemoryDomainAgent> logger)
    {
        _sink = sink;
        _logger = logger;
    }

    public string AgentId => Id;
    public int Priority => int.MaxValue;
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.UtcNow;

    public bool ClaimsIntent(UnifiedMessage message, PrincipalContext principal)
        => !string.IsNullOrWhiteSpace(message.Text);

    public async Task<AgentHandleResult> HandleAsync(
        UnifiedMessage message,
        PrincipalContext principal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
            return new AgentHandleResult(true, OutcomeCode: "no_text_skipped");

        try
        {
            await _sink.IngestAsync(new MemoryIngestionItem(
                TenantId: principal.TenantId,
                ContextId: principal.ContextId,
                UserId: principal.UserId,
                SourceChannel: message.ChannelType,
                SourceIdentity: message.MessageId,
                ContentText: message.Text,
                ProvenanceRef: $"{message.ChannelType}:{message.MessageId}",
                CorrelationId: message.CorrelationId ?? message.MessageId),
                ct);

            return new AgentHandleResult(true, OutcomeCode: "ingested");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "MemoryDomainAgent ingestion failed. message_id={MessageId}",
                message.MessageId);
            return new AgentHandleResult(false, ErrorCode: "ingestion_failed", ErrorMessage: ex.Message);
        }
    }
}
