namespace Aluki.Runtime.Calendar.Security;

public sealed class ProviderTokenBoundary
{
    private readonly string _material;

    private ProviderTokenBoundary(string raw) => _material = raw;

    public static ProviderTokenBoundary Wrap(string rawToken) => new(rawToken);

    // Only called by provider adapters internally — never exposed to user-facing paths.
    public string Unwrap() => _material;

    public override string ToString() => "[REDACTED]";
}
