using Aluki.Runtime.Capture.Retry;
using Aluki.Runtime.Capture.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class CaptureRetryPolicyTests
{
    private static CaptureRetryPolicy Policy(int max = 5, int baseMs = 100, int maxMs = 2000) =>
        new(Options.Create(new CaptureOptions
        {
            Retry = new RetryOptions { MaxAttempts = max, BaseDelayMilliseconds = baseMs, MaxDelayMilliseconds = maxMs }
        }));

    [Fact]
    public void Caps_attempts_at_configured_maximum()
    {
        var policy = Policy(max: 5);

        Assert.Equal(5, policy.MaxAttempts);
        Assert.True(policy.HasAttemptsRemaining(4));
        Assert.False(policy.HasAttemptsRemaining(5));
    }

    [Fact]
    public void ComputeDelay_grows_exponentially_and_is_capped()
    {
        var policy = Policy(baseMs: 100, maxMs: 500);

        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.ComputeDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.ComputeDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(400), policy.ComputeDelay(3));
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.ComputeDelay(4)); // capped
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.ComputeDelay(10)); // stays capped
    }

    [Fact]
    public void Classifies_transient_and_permanent_faults()
    {
        Assert.True(CaptureRetryPolicy.IsTransient(new TimeoutException()));
        Assert.False(CaptureRetryPolicy.IsTransient(new InvalidOperationException()));
        Assert.False(CaptureRetryPolicy.IsTransient(new ArgumentException()));
    }
}
