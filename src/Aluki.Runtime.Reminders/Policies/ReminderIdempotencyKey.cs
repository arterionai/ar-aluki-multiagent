using System.Security.Cryptography;
using System.Text;

namespace Aluki.Runtime.Reminders.Policies;

/// <summary>
/// Derives the stable idempotency key for a reminder creation so retries of the
/// same request resolve to the same reminder. An explicit <c>reminder_id</c> is
/// used verbatim; otherwise the key is a content hash over the user + text +
/// scheduled time so accidental double-submits collapse to one reminder.
/// </summary>
public static class ReminderIdempotencyKey
{
    public static string Derive(string? explicitId, Guid userId, string reminderText, DateTimeOffset scheduledTimeUtc)
    {
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return explicitId!.Trim();
        }

        var source = $"{userId:N}|{reminderText.Trim()}|{scheduledTimeUtc.ToUniversalTime():O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
