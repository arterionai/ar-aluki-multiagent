using System.Net;
using Npgsql;
using Xunit;
using Xunit.Extensions.Ordering;

[assembly: TestCaseOrderer("Xunit.Extensions.Ordering.TestCaseOrderer", "Xunit.Extensions.Ordering")]
[assembly: TestCollectionOrderer("Xunit.Extensions.Ordering.CollectionOrderer", "Xunit.Extensions.Ordering")]

namespace Aluki.Runtime.E2ETests;

/// <summary>
/// End-to-end live validation: simulates WhatsApp messages from the seeded test number
/// (+14252307522) to the deployed Azure Function, asserts replies in app.outbound_messages
/// and agent selection in dispatch_audit_events.
///
/// Run via: dotnet test --filter "Category=E2E"
/// Trigger in CI via the e2e-whatsapp.yml workflow_dispatch workflow.
///
/// Tests are ordered so note-save (T03) runs before lookup (T04) and deletion (T06).
/// </summary>
[Trait("Category", "E2E")]
[Order(1)]
public sealed class WhatsAppFlowTests(WhatsAppE2EFixture fixture) : IClassFixture<WhatsAppE2EFixture>
{
    private readonly HttpClient _http = fixture.Http;
    private readonly NpgsqlDataSource _db = fixture.Db;
    private readonly string _pnid = fixture.PhoneNumberId;
    private readonly string? _secret = fixture.MetaAppSecret;
    private readonly bool _sheloNabel = fixture.SheloNabelRouting;

    private const string Sender = WhatsAppE2EFixture.SenderWaId;

    // ── T01: Webhook contract ─────────────────────────────────────────────────

    [Fact, Order(1)]
    public async Task T01_Webhook_always_returns_200()
    {
        var status = await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, E2EHelpers.NewWamid(), "hola");

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact, Order(2)]
    public async Task T02_Invalid_signature_returns_401()
    {
        if (string.IsNullOrEmpty(_secret))
        {
            // When no secret is configured the function skips signature verification.
            Assert.True(true, "Skipped: E2E_META_APP_SECRET not set — signature check inactive.");
            return;
        }

        var status = await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, E2EHelpers.NewWamid(), "hola",
            tamperSignature: true);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    // ── T03: Person note save ─────────────────────────────────────────────────

    [Fact, Order(3)]
    public async Task T03_Person_note_save_replies_anotado()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        var status = await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid, "guarda que Fer es mi prima de Guadalajara");

        Assert.Equal(HttpStatusCode.OK, status);

        var body = await E2EHelpers.WaitForOutboundAsync(_db, Sender, "¡Anotado! 📒");
        Assert.NotNull(body);

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("memory.person_note", agentId);
    }

    // ── T04: Person lookup — found ────────────────────────────────────────────

    [Fact, Order(4)]
    public async Task T04_Person_lookup_found_replies_contact_card()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        var status = await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid, "¿quién es Fer?");

        Assert.Equal(HttpStatusCode.OK, status);

        var body = await E2EHelpers.WaitForOutboundAsync(_db, Sender, "📇 *Fer*");
        Assert.NotNull(body);

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("memory.person_lookup", agentId);
    }

    // ── T05: Person lookup — not found ────────────────────────────────────────

    [Fact, Order(5)]
    public async Task T05_Person_lookup_not_found_replies_no_notes()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid, "¿quién es Zaphod Beeblebrox?");

        var body = await E2EHelpers.WaitForOutboundAsync(_db, Sender, "No tengo notas sobre *Zaphod Beeblebrox*");
        Assert.NotNull(body);

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("memory.person_lookup", agentId);
    }

    // ── T06: Note deletion — match ────────────────────────────────────────────

    [Fact, Order(6)]
    public async Task T06_Note_deletion_match_replies_olvidado()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid, "borra lo de Fer");

        var body = await E2EHelpers.WaitForOutboundAsync(_db, Sender, "Olvidado 🗑️");
        Assert.NotNull(body);
        Assert.Contains("Fer", body);

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("memory.note_deletion", agentId);
    }

    // ── T07: Note deletion — no match ─────────────────────────────────────────

    [Fact, Order(7)]
    public async Task T07_Note_deletion_no_match_replies_not_found()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid, "borra lo de Aragorn");

        var body = await E2EHelpers.WaitForOutboundAsync(_db, Sender, "No encontré notas sobre *Aragorn*");
        Assert.NotNull(body);

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("memory.note_deletion", agentId);
    }

    // ── T08: Reminder scheduled ───────────────────────────────────────────────

    [Fact, Order(8)]
    public async Task T08_Reminder_scheduled_persists_row_and_replies_listo()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        var before = DateTimeOffset.UtcNow;
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid,
            "recuérdame llamar al dentista mañana a las 10");

        // The reminder agent uses LLM parsing — allow up to 45s.
        var body = await E2EHelpers.WaitForAnyOutboundAsync(_db, Sender, before, TimeSpan.FromSeconds(45));
        Assert.NotNull(body);

        // Confirm reply indicates success (✅ Listo) or clarification question.
        // Both are valid — LLM may or may not parse the time from "mañana a las 10".
        var isSuccessOrClarification =
            body.Contains("✅") || body.Contains("Listo") ||
            body.Contains("cuándo") || body.Contains("hora") || body.Contains("fecha");
        Assert.True(isSuccessOrClarification,
            $"Unexpected reminder reply: {body}");

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("reminders.whatsapp_scheduler", agentId);

        if (body.Contains("✅"))
        {
            // If the reminder was created, verify it exists in the DB.
            await using var cmd = _db.CreateCommand("""
                SELECT COUNT(1) FROM app.reminders r
                JOIN app.users_profile u ON u.user_id = r.user_id
                WHERE u.external_auth_id = $1
                  AND r.reminder_text ILIKE '%dentista%'
                  AND r.created_at > $2
                """);
            cmd.Parameters.AddWithValue(Sender);
            cmd.Parameters.AddWithValue(before.UtcDateTime);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.True(count > 0, "Reminder row not found in DB after confirmation reply.");
        }
    }

    // ── T09: Reminder clarification (ambiguous time) ──────────────────────────

    [Fact, Order(9)]
    public async Task T09_Reminder_ambiguous_time_prompts_clarification()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        var before = DateTimeOffset.UtcNow;
        // "recuérdame" without a time expression — no temporal marker.
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid, "recuérdame comprar leche");

        // Allow extra time for LLM call.
        var body = await E2EHelpers.WaitForAnyOutboundAsync(_db, Sender, before, TimeSpan.FromSeconds(45));
        Assert.NotNull(body);
        // Should ask for a time (not confirm success).
        Assert.DoesNotContain("✅", body);

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("reminders.whatsapp_scheduler", agentId);
    }

    // ── T10: Calendar — reconnect or confirmation ─────────────────────────────

    [Fact, Order(10)]
    public async Task T10_Calendar_scheduling_replies_with_calendar_related_text()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        var before = DateTimeOffset.UtcNow;
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid,
            "agéndame una reunión con el equipo mañana a las 3pm");

        var body = await E2EHelpers.WaitForAnyOutboundAsync(_db, Sender, before, TimeSpan.FromSeconds(45));
        Assert.NotNull(body);

        // Either a reconnect prompt (calendar not connected) or a creation confirmation.
        var mentionsCalendar =
            body.Contains("calendario") || body.Contains("conectar") ||
            body.Contains("Google") || body.Contains("Outlook") ||
            body.Contains("agend") || body.Contains("reunión") || body.Contains("creado");
        Assert.True(mentionsCalendar, $"Unexpected calendar reply: {body}");

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("calendar.scheduling", agentId);
    }

    // ── T11: Link save ────────────────────────────────────────────────────────

    [Fact, Order(11)]
    public async Task T11_Link_save_replies_guardado()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid,
            "este artículo está bueno https://example.com/articulo-de-prueba");

        var body = await E2EHelpers.WaitForOutboundAsync(_db, Sender, "Guardado 🔗");
        Assert.NotNull(body);
        Assert.Contains("example.com", body);

        // Link saves go through ConversationalResponseAgent (priority 100).
        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("conversation.whatsapp_response", agentId);
    }

    // ── T12: Conversational LLM — on-scope ───────────────────────────────────

    [Fact, Order(12)]
    public async Task T12_Conversational_on_scope_produces_non_empty_reply()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        var before = DateTimeOffset.UtcNow;
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid, "¿qué notas tengo guardadas?");

        // LLM call — up to 45s.
        var body = await E2EHelpers.WaitForAnyOutboundAsync(_db, Sender, before, TimeSpan.FromSeconds(45));
        Assert.NotNull(body);
        Assert.NotEmpty(body.Trim());

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("conversation.whatsapp_response", agentId);
    }

    // ── T13: Conversational LLM — off-scope ──────────────────────────────────

    [Fact, Order(13)]
    public async Task T13_Conversational_off_scope_redirects_politely()
    {
        if (_sheloNabel) return; // SheloNabelDomainAgent (priority 40) intercepts this wa_id — test not applicable in this configuration.

        var wamid = E2EHelpers.NewWamid();
        var before = DateTimeOffset.UtcNow;
        await E2EHelpers.SendWebhookAsync(
            _http, _secret, _pnid, Sender, wamid, "¿cuál es la receta de la paella valenciana?");

        var body = await E2EHelpers.WaitForAnyOutboundAsync(_db, Sender, before, TimeSpan.FromSeconds(45));
        Assert.NotNull(body);

        // Must NOT answer the recipe question — must redirect.
        Assert.DoesNotContain("arroz", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ingrediente", body, StringComparison.OrdinalIgnoreCase);

        var agentId = await E2EHelpers.WaitForAuditAsync(_db, wamid);
        Assert.Equal("conversation.whatsapp_response", agentId);
    }

    // ── T14: Dispatch audit completeness ─────────────────────────────────────

    [Fact, Order(14)]
    public async Task T14_No_contained_failures_in_audit_during_test_run()
    {
        await using var cmd = _db.CreateCommand("""
            SELECT COUNT(1)
            FROM app.dispatch_audit_events dae
            JOIN app.unified_message_artifact uma
              ON uma.unified_message_id = dae.unified_message_id
            WHERE uma.sender_identity = $1
              AND uma.received_at > NOW() - INTERVAL '10 minutes'
              AND dae.outcome = 'contained_failure'
            """);
        cmd.Parameters.AddWithValue(Sender);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(0, count);
    }
}

