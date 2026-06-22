namespace Aluki.Runtime.Host.Calendar;

public sealed class CalendarOptions
{
    public string CallbackBaseUrl { get; init; } = "";
    public int OAuthStateExpiryMinutes { get; init; } = 10;
    public int DeduplicationWindowMinutes { get; init; } = 10;
    public OutlookProviderOptions Outlook { get; init; } = new();
    public GoogleProviderOptions Google { get; init; } = new();
}

public sealed class OutlookProviderOptions
{
    public bool Enabled { get; init; }
    public string ClientId { get; init; } = "";
    public string TenantId { get; init; } = "common";
    public string[] Scopes { get; init; } = [];
}

public sealed class GoogleProviderOptions
{
    public bool Enabled { get; init; }
    public string ClientId { get; init; } = "";
    public string[] Scopes { get; init; } = [];
}
