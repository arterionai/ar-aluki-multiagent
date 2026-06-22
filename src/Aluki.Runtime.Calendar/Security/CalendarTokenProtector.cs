using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Aluki.Runtime.Calendar.Security;

/// <summary>
/// Encrypts/decrypts OAuth token material before it is persisted. Plaintext tokens
/// must never be written to the database or logs (FR-008a). Backed by a symmetric key
/// supplied via configuration (a Key Vault reference in the deployed environment).
/// </summary>
public interface ICalendarTokenProtector
{
    /// <summary>Encrypts plaintext, returning base64(nonce || ciphertext || tag).</summary>
    string Protect(string plaintext);

    /// <summary>Reverses <see cref="Protect"/>. Throws if the key/ciphertext is invalid.</summary>
    string Unprotect(string protectedValue);
}

/// <summary>
/// AES-256-GCM token protector. The key comes from <c>Calendar:TokenEncryptionKey</c>
/// (base64-encoded 32 bytes). Layout of the protected value is
/// nonce(12) || ciphertext(n) || tag(16), base64-encoded.
/// </summary>
public sealed class AesGcmCalendarTokenProtector : ICalendarTokenProtector
{
    private const int NonceSize = 12; // AesGcm.NonceByteSizes
    private const int TagSize = 16;   // AesGcm.TagByteSizes

    private readonly Lazy<byte[]> _key;

    public AesGcmCalendarTokenProtector(IOptions<CalendarOptions> options)
        : this(options.Value.TokenEncryptionKey)
    {
    }

    public AesGcmCalendarTokenProtector(string base64Key)
    {
        // Validate lazily: an empty/invalid key must not break DI construction (ValidateOnBuild)
        // for hosts that never touch the calendar connect/create path. It throws on first use.
        _key = new Lazy<byte[]>(() => ResolveKey(base64Key));
    }

    private static byte[] ResolveKey(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException(
                "Calendar token encryption key is not configured. Set 'Calendar:TokenEncryptionKey' " +
                "to a base64-encoded 32-byte key (a Key Vault reference in deployed environments).");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("'Calendar:TokenEncryptionKey' must be valid base64.", ex);
        }

        if (key.Length != 32)
            throw new InvalidOperationException(
                $"'Calendar:TokenEncryptionKey' must decode to 32 bytes (AES-256); got {key.Length}.");

        return key;
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key.Value, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var combined = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, combined, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceSize + cipher.Length, TagSize);

        return Convert.ToBase64String(combined);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);

        var combined = Convert.FromBase64String(protectedValue);
        if (combined.Length < NonceSize + TagSize)
            throw new CryptographicException("Protected token value is too short to be valid.");

        var cipherLength = combined.Length - NonceSize - TagSize;
        var nonce = new byte[NonceSize];
        var cipher = new byte[cipherLength];
        var tag = new byte[TagSize];
        Buffer.BlockCopy(combined, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(combined, NonceSize, cipher, 0, cipherLength);
        Buffer.BlockCopy(combined, NonceSize + cipherLength, tag, 0, TagSize);

        var plain = new byte[cipherLength];
        using var aes = new AesGcm(_key.Value, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
