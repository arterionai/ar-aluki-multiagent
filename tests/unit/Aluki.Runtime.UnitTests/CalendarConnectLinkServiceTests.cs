using System.Security.Cryptography;
using Aluki.Runtime.Abstractions.Skills.Calendar;
using Aluki.Runtime.Calendar;
using Aluki.Runtime.Calendar.Connect;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// Unit tests for the signed connect-link service: links round-trip, are tamper-proof,
/// expire, and never leak across a different signing key.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CalendarConnectLinkServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Context = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static CalendarConnectLinkService Build(string? key = null, int ttlMinutes = 30)
    {
        var options = Options.Create(new CalendarOptions
        {
            CallbackBaseUrl = "https://func.example.net",
            LinkSigningKey = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ConnectLinkExpiryMinutes = ttlMinutes,
        });
        return new CalendarConnectLinkService(options);
    }

    private static string TokenFrom(string url) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["token"]!;

    [Fact]
    public void Start_url_points_at_consent_page_and_round_trips()
    {
        var svc = Build();
        var url = svc.CreateStartUrl(Tenant, Context, User, CalendarProvider.Google);

        Assert.StartsWith("https://func.example.net/api/calendar/connect/start?token=", url);

        Assert.True(svc.TryValidateToken(TokenFrom(url), out var payload));
        Assert.Equal(Tenant, payload.TenantId);
        Assert.Equal(Context, payload.ContextId);
        Assert.Equal(User, payload.UserId);
        Assert.Equal(CalendarProvider.Google, payload.Provider);
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var svc = Build();
        var token = TokenFrom(svc.CreateStartUrl(Tenant, Context, User, CalendarProvider.Outlook));
        var tampered = token[..^2] + (token[^1] == 'A' ? "B" : "A");

        Assert.False(svc.TryValidateToken(tampered, out _));
    }

    [Fact]
    public void Token_from_a_different_key_is_rejected()
    {
        var minted = Build().CreateStartUrl(Tenant, Context, User, CalendarProvider.Outlook);
        var other = Build(); // different random key

        Assert.False(other.TryValidateToken(TokenFrom(minted), out _));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var svc = Build(ttlMinutes: -1); // already expired
        var token = TokenFrom(svc.CreateStartUrl(Tenant, Context, User, CalendarProvider.Google));

        Assert.False(svc.TryValidateToken(token, out _));
    }

    [Fact]
    public void Garbage_tokens_are_rejected()
    {
        var svc = Build();
        Assert.False(svc.TryValidateToken(null, out _));
        Assert.False(svc.TryValidateToken("", out _));
        Assert.False(svc.TryValidateToken("not-a-token", out _));
        Assert.False(svc.TryValidateToken("a.b", out _));
    }

    [Fact]
    public void Falls_back_to_token_encryption_key_when_link_key_absent()
    {
        var encKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var options = Options.Create(new CalendarOptions
        {
            CallbackBaseUrl = "https://func.example.net",
            TokenEncryptionKey = encKey, // no LinkSigningKey
        });
        var svc = new CalendarConnectLinkService(options);

        var token = TokenFrom(svc.CreateStartUrl(Tenant, Context, User, CalendarProvider.Outlook));
        Assert.True(svc.TryValidateToken(token, out _));
    }
}
