using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Memory;
using Aluki.Runtime.Memory.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aluki.Runtime.ContractTests;

[Trait("Category", "Contract")]
public sealed class NoteDeletionDomainAgentContractTests
{
    private static NoteDeletionDomainAgent BuildAgent(
        INoteDeletionService? deletionService = null,
        IWhatsAppMessenger? messenger = null,
        IOutboundMessageStore? outboundStore = null)
        => new(
            deletionService: deletionService ?? new StubNoteDeletionService(),
            messenger: messenger ?? new StubDeletionWhatsAppMessenger(),
            outboundStore: outboundStore ?? new StubDeletionOutboundMessageStore(),
            logger: NullLogger<NoteDeletionDomainAgent>.Instance);

    private static PrincipalContext MakePrincipal() =>
        new(UserId: Guid.NewGuid(), TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(),
            Roles: [], SourceChannel: "whatsapp", CorrelationId: Guid.NewGuid().ToString("N"));

    private static UnifiedMessage MakeWhatsAppMessage(
        string? text = "borra lo de Fer",
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
    public void AgentId_is_memory_note_deletion()
    {
        Assert.Equal("memory.note_deletion", BuildAgent().AgentId);
    }

    [Fact]
    public void Priority_is_57()
    {
        Assert.Equal(57, BuildAgent().Priority);
    }

    // ── ClaimsIntent ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("borra lo de Fer")]
    [InlineData("olvida lo de la galería")]
    [InlineData("forget about Bob")]
    public void ClaimsIntent_deletion_phrases_return_true(string text)
    {
        Assert.True(BuildAgent().ClaimsIntent(MakeWhatsAppMessage(text: text), MakePrincipal()));
    }

    [Theory]
    [InlineData("guarda que Fer es mi prima")]
    [InlineData("¿Quién es Fer?")]
    [InlineData("hola, ¿cómo estás?")]
    [InlineData(null)]
    public void ClaimsIntent_non_deletion_text_returns_false(string? text)
    {
        Assert.False(BuildAgent().ClaimsIntent(MakeWhatsAppMessage(text: text), MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_non_whatsapp_channel_returns_false()
    {
        var message = new UnifiedMessage(
            MessageId: "m1", ChannelType: "sms", Text: "borra lo de Fer",
            MediaRefs: [], ReceivedAtUtc: DateTimeOffset.UtcNow,
            SenderExternalId: "555", PhoneNumberId: "PNID");
        Assert.False(BuildAgent().ClaimsIntent(message, MakePrincipal()));
    }

    // ── HandleAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_deleted_notes_echoed_in_reply()
    {
        var deletion = new StubNoteDeletionService(["Fer Amaro la conocí en galería"]);
        var messenger = new StubDeletionWhatsAppMessenger();
        var agent = BuildAgent(deletion, messenger);

        var result = await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("note_deletion_done", result.OutcomeCode);
        Assert.Contains("Olvidado 🗑️", messenger.LastBody);
        Assert.Contains("• Fer Amaro la conocí en galería", messenger.LastBody);
        Assert.Equal("Fer", deletion.LastTopic);
    }

    [Fact]
    public async Task HandleAsync_no_match_replies_not_found()
    {
        var messenger = new StubDeletionWhatsAppMessenger();
        var agent = BuildAgent(new StubNoteDeletionService(), messenger);

        var result = await agent.HandleAsync(
            MakeWhatsAppMessage(text: "borra lo de Marcos"), MakePrincipal(), CancellationToken.None);

        Assert.Equal("note_deletion_no_match", result.OutcomeCode);
        Assert.Contains("No encontré notas sobre *Marcos*", messenger.LastBody);
    }

    [Fact]
    public async Task HandleAsync_failure_replies_fallback_and_succeeds()
    {
        var messenger = new StubDeletionWhatsAppMessenger();
        var agent = BuildAgent(new ThrowingNoteDeletionService(), messenger);

        var result = await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("note_deletion_error", result.OutcomeCode);
        Assert.Equal(1, messenger.SendCount);
    }

    [Fact]
    public async Task HandleAsync_long_deleted_note_truncated_in_reply()
    {
        var deletion = new StubNoteDeletionService([new string('x', 400)]);
        var messenger = new StubDeletionWhatsAppMessenger();
        var agent = BuildAgent(deletion, messenger);

        await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        var noteLine = messenger.LastBody!.Split('\n').Single(l => l.StartsWith("• "));
        Assert.True(noteLine.Length < 170, "Each deleted note in the reply should be truncated.");
    }

    [Fact]
    public async Task HandleAsync_persists_outbound_record()
    {
        var outbound = new StubDeletionOutboundMessageStore();
        var agent = BuildAgent(new StubNoteDeletionService(["nota"]), outboundStore: outbound);

        await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(1, outbound.PersistCount);
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

file sealed class StubNoteDeletionService : INoteDeletionService
{
    private readonly IReadOnlyList<string> _deleted;

    public StubNoteDeletionService(IReadOnlyList<string>? deleted = null) => _deleted = deleted ?? [];

    public string? LastTopic { get; private set; }

    public Task<IReadOnlyList<string>> DeleteNotesAsync(
        PrincipalScope scope, string topic, string correlationId, CancellationToken cancellationToken)
    {
        LastTopic = topic;
        return Task.FromResult(_deleted);
    }
}

file sealed class ThrowingNoteDeletionService : INoteDeletionService
{
    public Task<IReadOnlyList<string>> DeleteNotesAsync(
        PrincipalScope scope, string topic, string correlationId, CancellationToken cancellationToken)
        => throw new InvalidOperationException("db down");
}

file sealed class StubDeletionWhatsAppMessenger : IWhatsAppMessenger
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

file sealed class StubDeletionOutboundMessageStore : IOutboundMessageStore
{
    public int PersistCount { get; private set; }

    public Task<bool> TryPersistAsync(OutboundMessage message, CancellationToken ct)
    {
        PersistCount++;
        return Task.FromResult(true);
    }
}
