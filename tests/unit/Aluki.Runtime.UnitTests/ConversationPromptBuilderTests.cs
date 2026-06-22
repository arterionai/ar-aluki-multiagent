using Aluki.Runtime.Abstractions.Conversation;
using Aluki.Runtime.Conversation;
using Aluki.Runtime.Memory;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class ConversationPromptBuilderTests
{
    private static readonly ConversationPromptBuilder Builder = new();

    // ── System prompt ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_Returns_nonempty_string()
    {
        var prompt = Builder.BuildSystemPrompt();
        Assert.False(string.IsNullOrWhiteSpace(prompt));
    }

    [Fact]
    public void BuildSystemPrompt_Contains_grounding_instruction()
    {
        var prompt = Builder.BuildSystemPrompt();
        Assert.Contains("ONLY answer based on", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystemPrompt_Contains_no_invention_instruction()
    {
        var prompt = Builder.BuildSystemPrompt();
        Assert.Contains("never invent", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── User prompt — current message always present ───────────────────────────

    [Fact]
    public void BuildUserPrompt_Includes_current_message()
    {
        var prompt = Builder.BuildUserPrompt("hello world", [], null);
        Assert.Contains("hello world", prompt);
        Assert.Contains("## Current message", prompt);
    }

    [Fact]
    public void BuildUserPrompt_No_history_omits_recent_conversation_section()
    {
        var prompt = Builder.BuildUserPrompt("hi", [], null);
        Assert.DoesNotContain("## Recent conversation", prompt);
    }

    // ── User prompt — conversation history ────────────────────────────────────

    [Fact]
    public void BuildUserPrompt_With_inbound_turn_labels_speaker_User()
    {
        var history = new List<ConversationTurn>
        {
            new("what time is it?", "inbound", DateTimeOffset.UtcNow.AddMinutes(-2))
        };

        var prompt = Builder.BuildUserPrompt("again", history, null);

        Assert.Contains("User: what time is it?", prompt);
    }

    [Fact]
    public void BuildUserPrompt_With_outbound_turn_labels_speaker_Aluki()
    {
        var history = new List<ConversationTurn>
        {
            new("It is 3pm.", "outbound", DateTimeOffset.UtcNow.AddMinutes(-1))
        };

        var prompt = Builder.BuildUserPrompt("ok", history, null);

        Assert.Contains("Aluki: It is 3pm.", prompt);
    }

    [Fact]
    public void BuildUserPrompt_Includes_all_history_turns_in_order()
    {
        var history = new List<ConversationTurn>
        {
            new("first", "inbound", DateTimeOffset.UtcNow.AddMinutes(-3)),
            new("second", "outbound", DateTimeOffset.UtcNow.AddMinutes(-2)),
            new("third", "inbound", DateTimeOffset.UtcNow.AddMinutes(-1))
        };

        var prompt = Builder.BuildUserPrompt("now", history, null);

        var firstIdx = prompt.IndexOf("User: first", StringComparison.Ordinal);
        var secondIdx = prompt.IndexOf("Aluki: second", StringComparison.Ordinal);
        var thirdIdx = prompt.IndexOf("User: third", StringComparison.Ordinal);

        Assert.True(firstIdx < secondIdx, "first turn should appear before second");
        Assert.True(secondIdx < thirdIdx, "second turn should appear before third");
    }

    // ── User prompt — memory context ─────────────────────────────────────────

    [Fact]
    public void BuildUserPrompt_With_null_recall_omits_memory_section()
    {
        var prompt = Builder.BuildUserPrompt("hi", [], null);
        Assert.DoesNotContain("## Memory context", prompt);
    }

    [Fact]
    public void BuildUserPrompt_With_empty_claims_omits_memory_section()
    {
        var recall = new RecallResult(null, null, null, [], []);
        var prompt = Builder.BuildUserPrompt("hi", [], recall);
        Assert.DoesNotContain("## Memory context", prompt);
    }

    [Fact]
    public void BuildUserPrompt_With_claims_includes_memory_section()
    {
        var claim = new RecallClaim("c1", "User likes coffee", "confirmed", []);
        var recall = new RecallResult("high", null, null, [], [claim]);

        var prompt = Builder.BuildUserPrompt("what do I drink?", [], recall);

        Assert.Contains("## Memory context", prompt);
        Assert.Contains("User likes coffee", prompt);
    }

    [Fact]
    public void BuildUserPrompt_Multiple_claims_all_appear()
    {
        var claims = new[]
        {
            new RecallClaim("c1", "User is from Mexico", "confirmed", []),
            new RecallClaim("c2", "User speaks Spanish", "confirmed", [])
        };
        var recall = new RecallResult("high", null, null, [], claims);

        var prompt = Builder.BuildUserPrompt("tell me about myself", [], recall);

        Assert.Contains("User is from Mexico", prompt);
        Assert.Contains("User speaks Spanish", prompt);
    }

    [Fact]
    public void BuildUserPrompt_Memory_section_appears_before_history()
    {
        var claim = new RecallClaim("c1", "some fact", "confirmed", []);
        var recall = new RecallResult("high", null, null, [], [claim]);
        var history = new List<ConversationTurn>
        {
            new("previous message", "inbound", DateTimeOffset.UtcNow.AddMinutes(-1))
        };

        var prompt = Builder.BuildUserPrompt("current", history, recall);

        var memIdx = prompt.IndexOf("## Memory context", StringComparison.Ordinal);
        var histIdx = prompt.IndexOf("## Recent conversation", StringComparison.Ordinal);
        Assert.True(memIdx < histIdx, "memory context should appear before conversation history");
    }
}
