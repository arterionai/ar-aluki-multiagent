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
public sealed class PersonLookupDomainAgentContractTests
{
    private static PersonLookupDomainAgent BuildAgent(
        IPersonLookupService? lookupService = null,
        IWhatsAppMessenger? messenger = null,
        IOutboundMessageStore? outboundStore = null)
        => new(
            lookupService: lookupService ?? new StubPersonLookupService(),
            messenger: messenger ?? new StubLookupWhatsAppMessenger(),
            outboundStore: outboundStore ?? new StubLookupOutboundMessageStore(),
            logger: NullLogger<PersonLookupDomainAgent>.Instance);

    private static PrincipalContext MakePrincipal() =>
        new(UserId: Guid.NewGuid(), TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(),
            Roles: [], SourceChannel: "whatsapp", CorrelationId: Guid.NewGuid().ToString("N"));

    private static UnifiedMessage MakeWhatsAppMessage(
        string? text = "¿Quién es Fer?",
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
    public void AgentId_is_memory_person_lookup()
    {
        Assert.Equal("memory.person_lookup", BuildAgent().AgentId);
    }

    [Fact]
    public void Priority_is_58()
    {
        Assert.Equal(58, BuildAgent().Priority);
    }

    // ── ClaimsIntent ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("¿Quién es Fer?")]
    [InlineData("qué sabes de Ana")]
    [InlineData("who is Bob")]
    public void ClaimsIntent_lookup_phrases_return_true(string text)
    {
        Assert.True(BuildAgent().ClaimsIntent(MakeWhatsAppMessage(text: text), MakePrincipal()));
    }

    [Theory]
    [InlineData("guarda que Fer es mi prima")]
    [InlineData("recuérdame llamar a Fer mañana")]
    [InlineData("hola, ¿cómo estás?")]
    [InlineData(null)]
    public void ClaimsIntent_non_lookup_text_returns_false(string? text)
    {
        Assert.False(BuildAgent().ClaimsIntent(MakeWhatsAppMessage(text: text), MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_non_whatsapp_channel_returns_false()
    {
        var message = new UnifiedMessage(
            MessageId: "m1", ChannelType: "sms", Text: "¿Quién es Fer?",
            MediaRefs: [], ReceivedAtUtc: DateTimeOffset.UtcNow,
            SenderExternalId: "555", PhoneNumberId: "PNID");
        Assert.False(BuildAgent().ClaimsIntent(message, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_missing_senderExternalId_returns_false()
    {
        Assert.False(BuildAgent().ClaimsIntent(MakeWhatsAppMessage(senderExternalId: null), MakePrincipal()));
    }

    // ── HandleAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_single_note_is_answered_not_withheld()
    {
        // The key regression vs the generic recall path: ONE note must produce a card,
        // not a low-confidence clarification question.
        var lookup = new StubPersonLookupService(["Fer Amaro la conocí en galería afuera de la casa de bluey"]);
        var messenger = new StubLookupWhatsAppMessenger();
        var agent = BuildAgent(lookup, messenger);

        var result = await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("person_lookup_answered", result.OutcomeCode);
        Assert.NotNull(messenger.LastBody);
        Assert.Contains("📇 *Fer*", messenger.LastBody);
        Assert.Contains("• Fer Amaro la conocí en galería", messenger.LastBody);
    }

    [Fact]
    public async Task HandleAsync_multiple_notes_listed_in_card()
    {
        var lookup = new StubPersonLookupService(["nota uno", "nota dos", "nota tres"]);
        var messenger = new StubLookupWhatsAppMessenger();
        var agent = BuildAgent(lookup, messenger);

        await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(3, messenger.LastBody!.Split('\n').Count(l => l.StartsWith("• ")));
    }

    [Fact]
    public async Task HandleAsync_no_notes_replies_save_hint()
    {
        var messenger = new StubLookupWhatsAppMessenger();
        var agent = BuildAgent(new StubPersonLookupService(), messenger);

        var result = await agent.HandleAsync(
            MakeWhatsAppMessage(text: "¿Quién es Marcos?"), MakePrincipal(), CancellationToken.None);

        Assert.Equal("person_lookup_no_notes", result.OutcomeCode);
        Assert.Contains("No tengo notas sobre *Marcos*", messenger.LastBody);
        Assert.Contains("guarda que Marcos", messenger.LastBody);
    }

    [Fact]
    public async Task HandleAsync_lookup_failure_replies_fallback_and_succeeds()
    {
        var messenger = new StubLookupWhatsAppMessenger();
        var agent = BuildAgent(new ThrowingPersonLookupService(), messenger);

        var result = await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("person_lookup_error", result.OutcomeCode);
        Assert.Equal(1, messenger.SendCount);
    }

    [Fact]
    public async Task HandleAsync_long_note_truncated_in_card()
    {
        var lookup = new StubPersonLookupService([new string('x', 400)]);
        var messenger = new StubLookupWhatsAppMessenger();
        var agent = BuildAgent(lookup, messenger);

        await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        var noteLine = messenger.LastBody!.Split('\n').Single(l => l.StartsWith("• "));
        Assert.True(noteLine.Length < 170, "Each note in the card should be truncated.");
        Assert.EndsWith("…", noteLine);
    }

    [Fact]
    public async Task HandleAsync_persists_outbound_record()
    {
        var outbound = new StubLookupOutboundMessageStore();
        var agent = BuildAgent(new StubPersonLookupService(["nota"]), outboundStore: outbound);

        await agent.HandleAsync(MakeWhatsAppMessage(), MakePrincipal(), CancellationToken.None);

        Assert.Equal(1, outbound.PersistCount);
    }

    [Fact]
    public async Task HandleAsync_passes_extracted_name_to_lookup()
    {
        var lookup = new StubPersonLookupService(["nota"]);
        var agent = BuildAgent(lookup);

        await agent.HandleAsync(
            MakeWhatsAppMessage(text: "¿Quién es Sofía Núñez?"), MakePrincipal(), CancellationToken.None);

        Assert.Equal("Sofía Núñez", lookup.LastPersonName);
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

file sealed class StubPersonLookupService : IPersonLookupService
{
    private readonly IReadOnlyList<string> _notes;

    public StubPersonLookupService(IReadOnlyList<string>? notes = null) => _notes = notes ?? [];

    public string? LastPersonName { get; private set; }

    public Task<IReadOnlyList<string>> FindNotesAsync(
        PrincipalScope scope, string personName, string correlationId, CancellationToken cancellationToken)
    {
        LastPersonName = personName;
        return Task.FromResult(_notes);
    }
}

file sealed class ThrowingPersonLookupService : IPersonLookupService
{
    public Task<IReadOnlyList<string>> FindNotesAsync(
        PrincipalScope scope, string personName, string correlationId, CancellationToken cancellationToken)
        => throw new InvalidOperationException("embedding backend down");
}

file sealed class StubLookupWhatsAppMessenger : IWhatsAppMessenger
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

file sealed class StubLookupOutboundMessageStore : IOutboundMessageStore
{
    public int PersistCount { get; private set; }

    public Task<bool> TryPersistAsync(OutboundMessage message, CancellationToken ct)
    {
        PersistCount++;
        return Task.FromResult(true);
    }
}
