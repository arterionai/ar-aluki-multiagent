using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Abstractions.Orchestration.Dispatch;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Aluki.Runtime.Reminders.Dispatch;
using Xunit;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Contract tests for ReminderDomainAgent covering identity and ClaimsIntent.
/// HandleAsync scenarios are in ReminderDispatchIntegrationTests because
/// ReminderService/ReminderScopeGuard/ReminderStore are sealed concrete types
/// that require a real PostgreSQL connection.
/// Scenarios 11–18 of the test coverage plan.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ReminderDomainAgentContractTests
{
    // ClaimsIntent and AgentId/Priority don't use any injected dependencies,
    // so passing null! is safe for these tests.
    private static ReminderDomainAgent BuildAgentForClaims() =>
        new(null!, null!, null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<ReminderDomainAgent>.Instance);

    private static PrincipalContext MakePrincipal() =>
        new(UserId: Guid.NewGuid(), TenantId: Guid.NewGuid(), ContextId: Guid.NewGuid(),
            Roles: ["OWNER"], SourceChannel: "whatsapp", CorrelationId: Guid.NewGuid().ToString("N"));

    private static UnifiedMessage MakeMessage(string? text, string? senderExternalId = "14252307522", string? phoneNumberId = "10827382989") =>
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
    public void AgentId_is_reminders_whatsapp_scheduler()
    {
        var agent = BuildAgentForClaims();
        Assert.Equal("reminders.whatsapp_scheduler", agent.AgentId);
    }

    [Fact]
    public void Priority_is_60()
    {
        var agent = BuildAgentForClaims();
        Assert.Equal(60, agent.Priority);
    }

    [Fact]
    public void Priority_is_below_CalendarDomainAgent_50_and_above_Conversational_100()
    {
        var agent = BuildAgentForClaims();
        Assert.True(agent.Priority > 50, "Must come after CalendarDomainAgent (50)");
        Assert.True(agent.Priority < 100, "Must come before ConversationalResponseAgent (100)");
    }

    // ── ClaimsIntent — Spanish triggers (scenarios 11–13) ────────────────────

    [Theory]
    [InlineData("recuérdame en 30 minutos revisar el correo")]   // basic accent
    [InlineData("recuerdame mañana a las 9am")]                  // no accent variant
    [InlineData("avisame el lunes comprar pan")]
    [InlineData("ponme un recordatorio el viernes")]
    [InlineData("me puedes recordar mañana")]
    [InlineData("puedes recordarme hacer ejercicio")]
    [InlineData("recordarme que tengo cita el lunes")]
    [InlineData("crea un recordatorio para el martes")]
    [InlineData("que no se me olvide llamar a mamá")]
    public void ClaimsIntent_SpanishReminderTriggers_returns_true(string text)
    {
        var agent = BuildAgentForClaims();
        Assert.True(agent.ClaimsIntent(MakeMessage(text), MakePrincipal()), $"Expected true for: '{text}'");
    }

    // ── ClaimsIntent — English triggers (scenarios 14–15) ────────────────────

    [Theory]
    [InlineData("remind me in 5 minutes to call John")]
    [InlineData("set a reminder for tomorrow at 3pm")]
    [InlineData("can you remind me about the meeting")]
    [InlineData("create a reminder to review the report")]
    public void ClaimsIntent_EnglishReminderTriggers_returns_true(string text)
    {
        var agent = BuildAgentForClaims();
        Assert.True(agent.ClaimsIntent(MakeMessage(text), MakePrincipal()), $"Expected true for: '{text}'");
    }

    // ── ClaimsIntent — real production message that caused the bug ────────────

    [Fact]
    public void ClaimsIntent_RealProductionMessage_returns_true()
    {
        var agent = BuildAgentForClaims();
        const string realMessage =
            "Hola Aluki, me puedes recordar el próximo martes 30 de junio, mandarle a María el enlace de amazon para las cubiertas del reposabrazos de mi silla de oficina?";
        Assert.True(agent.ClaimsIntent(MakeMessage(realMessage), MakePrincipal()));
    }

    // ── ClaimsIntent — non-reminder messages (scenarios 16–17) ───────────────

    [Theory]
    [InlineData("hola cómo estás")]
    [InlineData("agéndame una reunión el viernes")]   // calendar, not reminder
    [InlineData("¿cuál es mi saldo?")]
    [InlineData("quiero saber el clima")]
    [InlineData("cuéntame un chiste")]
    public void ClaimsIntent_NonReminderMessages_returns_false(string text)
    {
        var agent = BuildAgentForClaims();
        Assert.False(agent.ClaimsIntent(MakeMessage(text), MakePrincipal()), $"Expected false for: '{text}'");
    }

    // ── ClaimsIntent — missing routing fields (scenario 18 + guards) ─────────

    [Fact]
    public void ClaimsIntent_EmptyText_returns_false()
    {
        Assert.False(BuildAgentForClaims().ClaimsIntent(MakeMessage(string.Empty), MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_WhitespaceText_returns_false()
    {
        Assert.False(BuildAgentForClaims().ClaimsIntent(MakeMessage("   "), MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_NullText_returns_false()
    {
        Assert.False(BuildAgentForClaims().ClaimsIntent(MakeMessage(null), MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_NullSenderExternalId_returns_false()
    {
        var msg = MakeMessage("recuérdame algo", senderExternalId: null);
        Assert.False(BuildAgentForClaims().ClaimsIntent(msg, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_EmptySenderExternalId_returns_false()
    {
        var msg = MakeMessage("recuérdame algo", senderExternalId: "  ");
        Assert.False(BuildAgentForClaims().ClaimsIntent(msg, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_NullPhoneNumberId_returns_false()
    {
        var msg = MakeMessage("recuérdame algo", phoneNumberId: null);
        Assert.False(BuildAgentForClaims().ClaimsIntent(msg, MakePrincipal()));
    }

    [Fact]
    public void ClaimsIntent_NonWhatsApp_channel_returns_false()
    {
        var agent = BuildAgentForClaims();
        var msg = new UnifiedMessage(
            MessageId: "m1", ChannelType: "sms", Text: "recuérdame algo",
            MediaRefs: [], ReceivedAtUtc: DateTimeOffset.UtcNow,
            SenderExternalId: "14252307522", PhoneNumberId: "108");
        Assert.False(agent.ClaimsIntent(msg, MakePrincipal()));
    }
}
