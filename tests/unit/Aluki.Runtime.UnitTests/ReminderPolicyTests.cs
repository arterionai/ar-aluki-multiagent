using Aluki.Runtime.Reminders.Policies;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>SB-005: pure reminder policy logic (idempotency key, snooze clamping).</summary>
[Trait("Category", "Unit")]
public sealed class ReminderPolicyTests
{
    [Fact]
    public void Explicit_reminder_id_is_used_verbatim()
    {
        var key = ReminderIdempotencyKey.Derive("  my-id  ", Guid.NewGuid(), "text", DateTimeOffset.UtcNow);
        Assert.Equal("my-id", key);
    }

    [Fact]
    public void Derived_key_is_stable_for_same_inputs()
    {
        var user = Guid.NewGuid();
        var when = DateTimeOffset.Parse("2026-07-01T15:00:00Z");
        var a = ReminderIdempotencyKey.Derive(null, user, "Call Ana", when);
        var b = ReminderIdempotencyKey.Derive(null, user, "Call Ana", when);
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length); // sha256 hex
    }

    [Fact]
    public void Derived_key_differs_for_different_time()
    {
        var user = Guid.NewGuid();
        var a = ReminderIdempotencyKey.Derive(null, user, "Call Ana", DateTimeOffset.Parse("2026-07-01T15:00:00Z"));
        var b = ReminderIdempotencyKey.Derive(null, user, "Call Ana", DateTimeOffset.Parse("2026-07-01T16:00:00Z"));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(null, 300)]      // default to smallest preset
    [InlineData(0, 300)]         // non-positive -> smallest preset
    [InlineData(-5, 300)]
    [InlineData(900, 900)]       // within range preserved
    [InlineData(999999, 86400)]  // over the 24h cap -> capped
    public void Snooze_duration_is_clamped(int? requested, int expected)
    {
        Assert.Equal(expected, ReminderSnoozePolicy.ResolveDurationSeconds(requested, 86_400));
    }

    [Fact]
    public void Snooze_next_fire_time_adds_duration()
    {
        var now = DateTimeOffset.Parse("2026-07-01T10:00:00Z");
        Assert.Equal(now.AddMinutes(15), ReminderSnoozePolicy.NextFireTime(now, 900));
    }
}
