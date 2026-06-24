using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Capture.Media;
using Aluki.Runtime.Conversation;
using Aluki.Runtime.Extraction;
using Aluki.Runtime.Extraction.Providers;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Chat;
using Aluki.Runtime.Memory.Recall;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class ConversationalResponseAgentContractTests
{
    private static ConversationalResponseAgent BuildAgent(
        IWhatsAppMessenger? messenger = null,
        IOutboundMessageStore? outboundStore = null,
        ConversationOptions? options = null,
        IMetaMediaClient? mediaClient = null,
        ITranscriptionProvider? transcriptionProvider = null,
        IChatModelRouter? chatRouter = null,
        IMemoryRecallService? recallService = null)
    {
        return new ConversationalResponseAgent(
            ingestionSink: null!,
            feedbackSink: null!,
            recallService: recallService ?? new StubMemoryRecallService(),
            chatRouter: chatRouter ?? null!,
            messenger: messenger ?? new StubWhatsAppMessenger(),
            historyStore: new StubConversationHistoryStore(),
            outboundStore: outboundStore ?? new StubOutboundMessageStore(),
            promptBuilder: new ConversationPromptBuilder(),
            mediaClient: mediaClient ?? new StubMetaMediaClient(shouldThrow: true),
            transcriptionProvider: transcriptionProvider ?? new StubTranscriptionProvider(null),
            options: Options.Create(options ?? new ConversationOptions()),
            logger: NullLogger<ConversationalResponseAgent>.Instance);
    }

    private static PrincipalContext MakePrincipal() =>
        new(UserId: Guid.NewGuid(), TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(),
            Roles: [], SourceChannel: "whatsapp", CorrelationId: Guid.NewGuid().ToString("N"));

    private static UnifiedMessage MakeWhatsAppMessage(
        string? text = "hola",
        string? senderExternalId = "521555000001",
        string? phoneNumberId = "PNID",
        IReadOnlyList<UnifiedMediaRef>? mediaRefs = null) =>
        new(MessageId: Guid.NewGuid().ToString("N"),
            ChannelType: ChannelType.WhatsApp,
            Text: text,
            MediaRefs: mediaRefs ?? [],
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString("N"),
            SenderExternalId: senderExternalId,
            PhoneNumberId: phoneNumberId);

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void AgentId_is_conversation_whatsapp_response()
    {
        var agent = BuildAgent();
        Assert.Equal("conversation.whatsapp_response", agent.AgentId);
    }

    [Fact]
    public void Priority_is_100()
    {
        var agent = BuildAgent();
        Assert.Equal(100, agent.Priority);
    }

    // ── ClaimsIntent ─────────────────────────────────────────────────────────

    [Fact]
    public void ClaimsIntent_WhatsApp_with_sender_and_phoneId_returns_true()
    {
        var agent = BuildAgent();
        var message = MakeWhatsAppMessage();
        Assert.True(agent.ClaimsIntent(message, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_WhatsApp_missing_senderExternalId_returns_false()
    {
        var agent = BuildAgent();
        var message = MakeWhatsAppMessage(senderExternalId: null);
        Assert.False(agent.ClaimsIntent(message, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_WhatsApp_empty_senderExternalId_returns_false()
    {
        var agent = BuildAgent();
        var message = MakeWhatsAppMessage(senderExternalId: "   ");
        Assert.False(agent.ClaimsIntent(message, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_WhatsApp_missing_phoneNumberId_returns_false()
    {
        var agent = BuildAgent();
        var message = MakeWhatsAppMessage(phoneNumberId: null);
        Assert.False(agent.ClaimsIntent(message, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_NonWhatsApp_channel_returns_false()
    {
        var agent = BuildAgent();
        var message = new UnifiedMessage(
            MessageId: "m1", ChannelType: "sms", Text: "hello",
            MediaRefs: [], ReceivedAtUtc: DateTimeOffset.UtcNow,
            SenderExternalId: "555", PhoneNumberId: "PNID");
        Assert.False(agent.ClaimsIntent(message, MakePrincipal()));
    }

    // ── HandleAsync — audio path ──────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Audio_transcription_failure_sends_ack_then_retry_prompt()
    {
        var messenger = new StubWhatsAppMessenger();
        var outboundStore = new StubOutboundMessageStore();
        // Default mediaClient throws → transcription fails
        var agent = BuildAgent(messenger, outboundStore);

        var audioRef = new UnifiedMediaRef("audio-id", "audio", null, null);
        var message = MakeWhatsAppMessage(text: null, mediaRefs: [audioRef]);

        var result = await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("audio_transcription_failed", result.OutcomeCode);
        Assert.Equal(2, messenger.SendCount);    // acknowledgment + retry prompt
        Assert.Equal(2, outboundStore.PersistCount);
    }

    [Fact]
    public async Task HandleAsync_Audio_successful_transcription_proceeds_past_acknowledgment()
    {
        var messenger = new StubWhatsAppMessenger();
        var agent = BuildAgent(
            messenger,
            mediaClient: new StubMetaMediaClient(shouldThrow: false),
            transcriptionProvider: new StubTranscriptionProvider("hola mundo"));

        var audioRef = new UnifiedMediaRef("audio-id", "audio", null, null);
        var message = MakeWhatsAppMessage(text: null, mediaRefs: [audioRef]);

        await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        // At least 2 sends: acknowledgment + LLM response (or fallback since chatRouter is null)
        Assert.True(messenger.SendCount >= 2);
    }

    [Fact]
    public async Task HandleAsync_Audio_full_flow_ack_transcribe_llm_respond()
    {
        // Simulates the complete real flow without any external services:
        // audio arrives → ack sent → downloaded (stub) → transcribed (stub "recuérdame mañana")
        // → LLM responds (stub "Listo, ¿a qué hora?") → second message sent to user
        var messenger = new StubWhatsAppMessenger();
        var outboundStore = new StubOutboundMessageStore();
        const string transcribedText = "recuérdame mañana";
        const string llmReply = "Listo, ¿a qué hora quieres el recordatorio?";

        var agent = BuildAgent(
            messenger, outboundStore,
            mediaClient: new StubMetaMediaClient(shouldThrow: false),
            transcriptionProvider: new StubTranscriptionProvider(transcribedText),
            chatRouter: new StubChatModelRouter(llmReply));

        var audioRef = new UnifiedMediaRef("provider-media-id-123", "audio", "audio/ogg", 8000);
        var message = MakeWhatsAppMessage(text: null, mediaRefs: [audioRef]);

        var result = await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("responded", result.OutcomeCode);
        Assert.Equal(2, messenger.SendCount);                          // ack + llm reply
        Assert.Equal(2, outboundStore.PersistCount);
        Assert.Equal(llmReply, outboundStore.LastMessage?.Body);       // LLM reply was sent
    }

    // ── HandleAsync — empty text (skip path) ─────────────────────────────────

    [Fact]
    public async Task HandleAsync_Empty_text_returns_no_text_skipped_without_sending()
    {
        var messenger = new StubWhatsAppMessenger();
        var agent = BuildAgent(messenger);

        var message = MakeWhatsAppMessage(text: "   ");

        var result = await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("no_text_skipped", result.OutcomeCode);
        Assert.Equal(0, messenger.SendCount);
    }

    [Fact]
    public async Task HandleAsync_Null_text_no_media_returns_no_text_skipped()
    {
        var messenger = new StubWhatsAppMessenger();
        var agent = BuildAgent(messenger);

        var message = MakeWhatsAppMessage(text: null);

        var result = await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("no_text_skipped", result.OutcomeCode);
        Assert.Equal(0, messenger.SendCount);
    }

    // ── Idempotency via outbound store ────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Audio_uses_message_correlationId_for_idempotency()
    {
        var outboundStore = new StubOutboundMessageStore();
        var agent = BuildAgent(outboundStore: outboundStore);

        var correlationId = "corr-xyz";
        var audioRef = new UnifiedMediaRef("audio-id", "audio", null, null);
        var message = new UnifiedMessage(
            MessageId: "m1", ChannelType: ChannelType.WhatsApp, Text: null,
            MediaRefs: [audioRef], ReceivedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: correlationId, SenderExternalId: "521555", PhoneNumberId: "PNID");

        await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.Equal(correlationId, outboundStore.LastMessage?.CorrelationMessageId);
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

file sealed class StubWhatsAppMessenger : IWhatsAppMessenger
{
    public int SendCount { get; private set; }

    public Task MarkReadAndShowTypingAsync(string phoneNumberId, string messageId, CancellationToken ct)
        => Task.CompletedTask;

    public Task SendTextMessageAsync(string phoneNumberId, string recipientWaId, string text, CancellationToken ct)
    {
        SendCount++;
        return Task.CompletedTask;
    }
}

file sealed class StubOutboundMessageStore : IOutboundMessageStore
{
    public int PersistCount { get; private set; }
    public OutboundMessage? LastMessage { get; private set; }

    public Task<bool> TryPersistAsync(OutboundMessage message, CancellationToken ct)
    {
        PersistCount++;
        LastMessage = message;
        return Task.FromResult(true);
    }
}

file sealed class StubConversationHistoryStore : IConversationHistoryStore
{
    public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(
        Guid tenantId, Guid userId, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConversationTurn>>([]);
}

file sealed class StubMetaMediaClient(bool shouldThrow) : IMetaMediaClient
{
    public Task<MetaMediaContent> DownloadAsync(string providerMediaId, CancellationToken cancellationToken)
    {
        if (shouldThrow)
            throw new InvalidOperationException("Stub: media download disabled.");
        return Task.FromResult(new MetaMediaContent([], "audio/ogg", 0));
    }
}

file sealed class StubTranscriptionProvider(string? text) : ITranscriptionProvider
{
    public Task<TranscriptionOutput> TranscribeAsync(
        byte[] audio, string encoding, string? languageHint, CancellationToken cancellationToken)
        => Task.FromResult(new TranscriptionOutput(
            FullTranscription: text ?? string.Empty,
            Segments: [],
            AudioDurationMs: 0,
            ModelInfo: new ModelInfo("Stub", "stub", "v0")));
}

file sealed class StubChatModelRouter(string reply) : IChatModelRouter
{
    public string? LastUserPrompt { get; private set; }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        LastUserPrompt = userPrompt;
        return Task.FromResult(reply);
    }
}

file sealed class StubMemoryRecallService : IMemoryRecallService
{
    public Task<MemoryRecallOutcome> RecallAsync(
        PrincipalScope principal, string queryText, string correlationId, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryRecallOutcome(MemoryStatus.NoResult, null!));
}
