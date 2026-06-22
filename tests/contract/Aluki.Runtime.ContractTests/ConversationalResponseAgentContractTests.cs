using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Conversation;
using Aluki.Runtime.Memory;
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
        ConversationOptions? options = null)
    {
        return new ConversationalResponseAgent(
            ingestionSink: null!,
            recallService: null!,
            chatRouter: null!,
            messenger: messenger ?? new StubWhatsAppMessenger(),
            historyStore: new StubConversationHistoryStore(),
            outboundStore: outboundStore ?? new StubOutboundMessageStore(),
            promptBuilder: new ConversationPromptBuilder(),
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

    // ── HandleAsync — audio path (US3) ────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Audio_message_sends_acknowledgment_and_returns_audio_acknowledged()
    {
        var messenger = new StubWhatsAppMessenger();
        var outboundStore = new StubOutboundMessageStore();
        var agent = BuildAgent(messenger, outboundStore);

        var audioRef = new UnifiedMediaRef("audio-id", "audio", null, null);
        var message = MakeWhatsAppMessage(text: null, mediaRefs: [audioRef]);

        var result = await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("audio_acknowledged", result.OutcomeCode);
        Assert.Equal(1, messenger.SendCount);
    }

    [Fact]
    public async Task HandleAsync_Audio_message_persists_outbound_record()
    {
        var messenger = new StubWhatsAppMessenger();
        var outboundStore = new StubOutboundMessageStore();
        var agent = BuildAgent(messenger, outboundStore);

        var audioRef = new UnifiedMediaRef("audio-id", "audio", null, null);
        var message = MakeWhatsAppMessage(text: null, mediaRefs: [audioRef]);

        await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.Equal(1, outboundStore.PersistCount);
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
