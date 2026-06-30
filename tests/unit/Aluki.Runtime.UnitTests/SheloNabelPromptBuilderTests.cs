using Aluki.Runtime.SheloNabel;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class SheloNabelPromptBuilderTests
{
    private static readonly SheloNabelPromptBuilder Builder = new();

    // ── Persona and brand ────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_Contains_persona_name_Nabel()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("Nabel", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_Contains_Shelo_NABEL_brand()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("Sheló NABEL", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_Contains_brand_protection_rule()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("PROTECCIÓN DE MARCA", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── Customer memory injection ─────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_With_null_memory_does_not_include_memory_section()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.DoesNotContain("## Memoria de clientes de Jaime", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_With_memory_includes_it_in_prompt()
    {
        var prompt = Builder.BuildSystemPrompt("María, 45 años, piel seca");
        Assert.Contains("Memoria de clientes de Jaime", prompt);
        Assert.Contains("María", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_With_whitespace_memory_omits_section()
    {
        var prompt = Builder.BuildSystemPrompt("   ");
        Assert.DoesNotContain("## Memoria de clientes de Jaime", prompt);
    }

    // ── Scope restriction ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_Contains_scope_section()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("ALCANCE Y SEGURIDAD", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_Scope_section_lists_out_of_scope_topics()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("recetas", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("médico", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("código", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_Scope_section_provides_redirect_phrase()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        // Must provide a redirect sentence for out-of-scope requests
        Assert.Contains("más allá de lo que puedo hacer", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── Prompt injection defense ───────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_Contains_injection_defense_section()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("PROMPT INJECTION", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_Injection_defense_covers_ignore_instrucciones()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("ignora las instrucciones anteriores", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_Injection_defense_covers_actua_como()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("actúa como", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_Injection_defense_covers_DAN_pattern()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("DAN", prompt);
    }

    // ── URL / link handling ────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_URL_handling_explains_no_internet_access()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("internet", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_URL_handling_mentions_URLs_as_plain_text()
    {
        var prompt = Builder.BuildSystemPrompt(null);
        Assert.Contains("texto plano", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── User prompt builders ───────────────────────────────────────────────────────

    [Fact]
    public void BuildQueryUserPrompt_Includes_message_in_Jaime_section()
    {
        var prompt = Builder.BuildQueryUserPrompt("¿qué le recomiendas a María?", null, null);
        Assert.Contains("Mensaje de Jaime", prompt);
        Assert.Contains("María", prompt);
    }

    [Fact]
    public void BuildReminderUserPrompt_Includes_system_state()
    {
        var prompt = Builder.BuildReminderUserPrompt("recuérdame el martes", "Recordatorio creado: martes a las 9am", null);
        Assert.Contains("Estado del sistema", prompt);
        Assert.Contains("Recordatorio creado", prompt);
    }

    [Fact]
    public void BuildSaleUserPrompt_Includes_venta_registrada_section()
    {
        var prompt = Builder.BuildSaleUserPrompt("le vendí la crema a María", "Venta registrada", null);
        Assert.Contains("venta registrada", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
