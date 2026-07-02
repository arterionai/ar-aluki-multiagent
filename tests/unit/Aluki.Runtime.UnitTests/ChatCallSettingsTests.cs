using Aluki.Runtime.Memory.Chat;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class ChatCallSettingsTests
{
    [Fact]
    public void Null_settings_map_to_default_options()
    {
        var options = FoundryChatModelRouter.BuildOptions(null);

        Assert.Null(options.MaxOutputTokenCount);
        Assert.Null(options.Temperature);
    }

    [Fact]
    public void MaxOutputTokens_is_mapped()
    {
        var options = FoundryChatModelRouter.BuildOptions(new ChatCallSettings(MaxOutputTokens: 800));

        Assert.Equal(800, options.MaxOutputTokenCount);
        Assert.Null(options.Temperature);
    }

    [Fact]
    public void Temperature_is_mapped_only_when_set()
    {
        var options = FoundryChatModelRouter.BuildOptions(new ChatCallSettings(Temperature: 0.4f));

        Assert.Null(options.MaxOutputTokenCount);
        Assert.Equal(0.4f, options.Temperature);
    }

    [Fact]
    public void Default_interface_overload_ignores_settings_for_legacy_implementations()
    {
        // Stubs implementing only the 3-arg CompleteAsync must keep working when
        // callers pass ChatCallSettings through the 4-arg default interface method.
        IChatModelRouter router = new LegacyRouter("respuesta");

        var result = router.CompleteAsync("s", "u", new ChatCallSettings(MaxOutputTokens: 10), CancellationToken.None);

        Assert.Equal("respuesta", result.Result);
    }

    private sealed class LegacyRouter(string reply) : IChatModelRouter
    {
        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
            => Task.FromResult(reply);
    }
}
