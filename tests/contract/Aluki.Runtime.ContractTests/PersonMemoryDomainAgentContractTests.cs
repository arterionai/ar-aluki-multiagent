using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Memory;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Memory.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class PersonMemoryDomainAgentContractTests
{
    private static PersonMemoryDomainAgent BuildAgent(
        IMemoryIngestionSink? sink = null,
        IWhatsAppMessenger? messenger = null,
        IOutboundMessageStore? outboundStore = null)
        => new(
            sink: sink ?? new StubMemoryIngestionSink(),
            messenger: messenger ?? new StubWhatsAppMessenger(),
            outboundStore: outboundStore ?? new StubOutboundMessageStore(),
            logger: NullLogger<PersonMemoryDomainAgent>.Instance);

    private static PrincipalContext MakePrincipal() =>
        new(UserId: Guid.NewGuid(), TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(),
            Roles: [], SourceChannel: "whatsapp", CorrelationId: Guid.NewGuid().ToString("N"));

    private static UnifiedMessage MakeWhatsAppMessage(
        string? text = "recuérdame que Fer Amaro la conocí en galería",
        string? senderExternalId = "521555000001",
        string? phoneNumberId = "PNID") =>
        new(MessageId: Guid.NewGuid().ToString("N"),
            ChannelType: ChannelType.WhatsApp,
            Text: text,
            MediaRefs: [],
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString("N"),
            SenderExternalId: senderExternalId,
            PhoneNumberId: phoneNumberId);

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void AgentId_is_memory_person_note()
    {
        var agent = BuildAgent();
        Assert.Equal("memory.person_note", agent.AgentId);
    }

    [Fact]
    public void Priority_is_55()
    {
        var agent = BuildAgent();
        Assert.Equal(55, agent.Priority);
    }

    // ── ClaimsIntent ─────────────────────────────────────────────────────────

    [Fact]
    public void ClaimsIntent_person_note_text_returns_true()
    {
        var agent = BuildAgent();
        var message = MakeWhatsAppMessage();
        Assert.True(agent.ClaimsIntent(message, MakePrincipal()));
    }

    [Theory]
    [InlineData("guarda que Juan es el dentista")]
    [InlineData("anota que María trabaja en TechCorp")]
    [InlineData("recuérdame que Ana es mi prima")]
    public void ClaimsIntent_various_person_note_phrases_return_true(string text)
    {
        var agent = BuildAgent();
        var message = MakeWhatsAppMessage(text: text);
        Assert.True(agent.ClaimsIntent(message, MakePrincipal()));
    }

    [Theory]
    [InlineData("recuérdame llamar a Fer mañana")]
    [InlineData("recuérdame que tengo reunión mañana a las 3")]
    [InlineData("hola, ¿cómo estás?")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ClaimsIntent_reminder_or_unrelated_text_returns_false(string? text)
    {
        var agent = BuildAgent();
        var message = MakeWhatsAppMessage(text: text);
        Assert.False(agent.ClaimsIntent(message, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_non_whatsapp_channel_returns_false()
    {
        var agent = BuildAgent();
        var message = new UnifiedMessage(
            MessageId: "m1", ChannelType: "sms", Text: "guarda que Ana es mi prima",
            MediaRefs: [], ReceivedAtUtc: DateTimeOffset.UtcNow,
            SenderExternalId: "555", PhoneNumberId: "PNID");
        Assert.False(agent.ClaimsIntent(message, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_missing_senderExternalId_returns_false()
    {
        var agent = BuildAgent();
        var message = MakeWhatsAppMessage(senderExternalId: null);
        Assert.False(agent.ClaimsIntent(message, MakePrincipal()));
    }

    // ── HandleAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_saves_to_memory_and_sends_reply()
    {
        var sink = new StubMemoryIngestionSink();
        var messenger = new StubWhatsAppMessenger();
        var outboundStore = new StubOutboundMessageStore();
        var agent = BuildAgent(sink, messenger, outboundStore);

        var message = MakeWhatsAppMessage(text: "recuérdame que Fer Amaro la conocí en galería");

        var result = await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("person_note_saved", result.OutcomeCode);
        Assert.Equal(1, sink.IngestCount);
        Assert.Equal(1, messenger.SendCount);
        Assert.Equal(1, outboundStore.PersistCount);
    }

    [Fact]
    public async Task HandleAsync_reply_contains_note_text()
    {
        var messenger = new StubWhatsAppMessenger();
        var agent = BuildAgent(messenger: messenger);

        const string noteText = "guarda que Juan es el dentista de los niños";
        var message = MakeWhatsAppMessage(text: noteText);

        await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.NotNull(messenger.LastBody);
        Assert.Contains(noteText, messenger.LastBody);
    }

    [Fact]
    public async Task HandleAsync_empty_text_returns_skipped_without_sending()
    {
        var messenger = new StubWhatsAppMessenger();
        var agent = BuildAgent(messenger: messenger);

        var message = MakeWhatsAppMessage(text: "   ");

        var result = await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("no_text_skipped", result.OutcomeCode);
        Assert.Equal(0, messenger.SendCount);
    }

    [Fact]
    public async Task HandleAsync_long_text_truncated_in_reply()
    {
        var messenger = new StubWhatsAppMessenger();
        var agent = BuildAgent(messenger: messenger);

        var longText = "guarda que " + new string('x', 300);
        var message = MakeWhatsAppMessage(text: longText);

        await agent.HandleAsync(message, MakePrincipal(), CancellationToken.None);

        Assert.NotNull(messenger.LastBody);
        Assert.True(messenger.LastBody!.Length < 230, "Reply body should be truncated for very long notes.");
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

file sealed class StubMemoryIngestionSink : IMemoryIngestionSink
{
    public int IngestCount { get; private set; }

    public Task IngestAsync(MemoryIngestionItem item, CancellationToken cancellationToken)
    {
        IngestCount++;
        return Task.CompletedTask;
    }
}

file sealed class StubWhatsAppMessenger : IWhatsAppMessenger
{
    public int SendCount { get; private set; }
    public string? LastBody { get; private set; }

    public Task MarkReadAndShowTypingAsync(string phoneNumberId, string messageId, CancellationToken ct)
        => Task.CompletedTask;

    public Task SendTextMessageAsync(string phoneNumberId, string recipientWaId, string text, CancellationToken ct)
    {
        SendCount++;
        LastBody = text;
        return Task.CompletedTask;
    }
}

file sealed class StubOutboundMessageStore : IOutboundMessageStore
{
    public int PersistCount { get; private set; }

    public Task<bool> TryPersistAsync(OutboundMessage message, CancellationToken ct)
    {
        PersistCount++;
        return Task.FromResult(true);
    }
}
