namespace Aluki.Runtime.Calendar;

public sealed class CalendarOptions
{
    public string CallbackBaseUrl { get; init; } = "";
    public int OAuthStateExpiryMinutes { get; init; } = 10;
    public int DeduplicationWindowMinutes { get; init; } = 10;

    /// <summary>
    /// Base64-encoded 32-byte (AES-256) key used to encrypt OAuth tokens at rest.
    /// In the deployed environment this is a Key Vault reference.
    /// </summary>
    public string TokenEncryptionKey { get; init; } = "";

    public OutlookProviderOptions Outlook { get; init; } = new();
    public GoogleProviderOptions Google { get; init; } = new();
}

public sealed class OutlookProviderOptions
{
    public bool Enabled { get; init; }
    public string ClientId { get; init; } = "";

    /// <summary>Confidential client secret (Key Vault reference in deployed environments).</summary>
    public string ClientSecret { get; init; } = "";

    public string TenantId { get; init; } = "common";
    public string[] Scopes { get; init; } = [];
}

public sealed class GoogleProviderOptions
{
    public bool Enabled { get; init; }
    public string ClientId { get; init; } = "";

    /// <summary>Confidential client secret (Key Vault reference in deployed environments).</summary>
    public string ClientSecret { get; init; } = "";

    public string[] Scopes { get; init; } = [];
}
