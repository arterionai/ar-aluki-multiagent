using System.Security.Cryptography;
using Aluki.Runtime.Calendar.Security;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for the AES-256-GCM token protector that encrypts OAuth tokens at rest
/// (FR-008a). Verifies round-trip fidelity, tamper/key-mismatch detection, and that
/// misconfiguration fails on use (not construction, so DI build stays healthy).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CalendarTokenProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Round_trips_plaintext()
    {
        var protector = new AesGcmCalendarTokenProtector(NewKey());
        const string secret = "ya29.a0Af_ACCESS_TOKEN_material";

        var protectedValue = protector.Protect(secret);

        Assert.NotEqual(secret, protectedValue);
        Assert.Equal(secret, protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Each_protect_uses_a_fresh_nonce()
    {
        var protector = new AesGcmCalendarTokenProtector(NewKey());

        var a = protector.Protect("same-input");
        var b = protector.Protect("same-input");

        Assert.NotEqual(a, b); // different nonce ⇒ different ciphertext
    }

    [Fact]
    public void Unprotect_with_a_different_key_fails()
    {
        var p1 = new AesGcmCalendarTokenProtector(NewKey());
        var p2 = new AesGcmCalendarTokenProtector(NewKey());

        var ciphertext = p1.Protect("secret");

        Assert.Throws<AuthenticationTagMismatchException>(() => p2.Unprotect(ciphertext));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var protector = new AesGcmCalendarTokenProtector(NewKey());
        var bytes = Convert.FromBase64String(protector.Protect("secret"));
        bytes[^1] ^= 0xFF; // flip a bit in the tag

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Missing_key_throws_on_use_not_on_construction()
    {
        // Construction must not throw (keeps DI/ValidateOnBuild healthy for non-calendar hosts).
        var protector = new AesGcmCalendarTokenProtector("");

        Assert.Throws<InvalidOperationException>(() => protector.Protect("x"));
    }

    [Fact]
    public void Wrong_length_key_throws_on_use()
    {
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var protector = new AesGcmCalendarTokenProtector(shortKey);

        Assert.Throws<InvalidOperationException>(() => protector.Protect("x"));
    }
}
