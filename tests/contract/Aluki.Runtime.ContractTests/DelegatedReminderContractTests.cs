using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.DelegatedReminders;
using Aluki.Runtime.DelegatedReminders.Configuration;
using Aluki.Runtime.DelegatedReminders.Delivery;
using Aluki.Runtime.DelegatedReminders.Persistence;
using Aluki.Runtime.DelegatedReminders.Policies;
using Aluki.Runtime.DelegatedReminders.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Contract tests for the delegated reminder request/response shape and
/// pre-persistence validation (400). These paths return before any scope/DB
/// access, so no PostgreSQL is required; accepted paths are covered by the
/// integration suite.
/// </summary>
[Trait("Category", "Contract")]
public sealed class DelegatedReminderContractTests
{
    private static DelegatedReminderService BuildService()
    {
        var config = new ConfigurationBuilder().Build();
        var factory = new NpgsqlConnectionFactory(config);
        return new DelegatedReminderService(
            new DelegatedReminderScopeGuard(factory),
            new DelegatedReminderStore(factory),
            new ThrowingDelegatedDeliveryChannel(),
            Options.Create(new DelegatedReminderOptions()),
            NullLogger<DelegatedReminderService>.Instance);
    }

    private static DelegatedPrincipalContext ValidPrincipal() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static DateTimeOffset FutureDue() => DateTimeOffset.UtcNow.AddHours(2);

    // ── Missing required fields → 400 ────────────────────────────────────────

    [Fact]
    public async Task Missing_principal_returns_400()
    {
        var request = new CreateDelegatedReminderRequest(
            "c1", null, PrincipalContext: null,
            SenderIdentity: "+14252307522",
            RecipientIdentity: "+525512345678",
            RecipientName: null, RecipientPhoneE164: null, RecipientWhatsappHandle: null,
            Content: "Dentist appointment",
            DueTimeUtc: FutureDue());

        var result = await BuildService().CreateAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        var error = Assert.IsType<DelegatedReminderErrorResponse>(result.Body);
        Assert.Equal(DelegatedReminderErrorCode.InvalidPayload, error.Code);
        Assert.Equal("c1", error.CorrelationId);
    }

    [Fact]
    public async Task Missing_sender_identity_returns_400()
    {
        var request = new CreateDelegatedReminderRequest(
            null, null, ValidPrincipal(),
            SenderIdentity: "  ",
            RecipientIdentity: "+525512345678",
            RecipientName: null, RecipientPhoneE164: null, RecipientWhatsappHandle: null,
            Content: "Dentist appointment",
            DueTimeUtc: FutureDue());

        var result = await BuildService().CreateAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        var error = Assert.IsType<DelegatedReminderErrorResponse>(result.Body);
        Assert.Equal(DelegatedReminderErrorCode.InvalidPayload, error.Code);
    }

    [Fact]
    public async Task Missing_recipient_identity_returns_400()
    {
        var request = new CreateDelegatedReminderRequest(
            null, null, ValidPrincipal(),
            SenderIdentity: "+14252307522",
            RecipientIdentity: null,
            RecipientName: null, RecipientPhoneE164: null, RecipientWhatsappHandle: null,
            Content: "Dentist appointment",
            DueTimeUtc: FutureDue());

        var result = await BuildService().CreateAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Missing_content_returns_400()
    {
        var request = new CreateDelegatedReminderRequest(
            null, null, ValidPrincipal(),
            SenderIdentity: "+14252307522",
            RecipientIdentity: "+525512345678",
            RecipientName: null, RecipientPhoneE164: null, RecipientWhatsappHandle: null,
            Content: "   ",
            DueTimeUtc: FutureDue());

        var result = await BuildService().CreateAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Missing_due_time_returns_400()
    {
        var request = new CreateDelegatedReminderRequest(
            null, null, ValidPrincipal(),
            SenderIdentity: "+14252307522",
            RecipientIdentity: "+525512345678",
            RecipientName: null, RecipientPhoneE164: null, RecipientWhatsappHandle: null,
            Content: "Dentist appointment",
            DueTimeUtc: null);

        var result = await BuildService().CreateAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Past_due_time_returns_400()
    {
        var request = new CreateDelegatedReminderRequest(
            null, null, ValidPrincipal(),
            SenderIdentity: "+14252307522",
            RecipientIdentity: "+525512345678",
            RecipientName: null, RecipientPhoneE164: null, RecipientWhatsappHandle: null,
            Content: "Dentist appointment",
            DueTimeUtc: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await BuildService().CreateAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Content_too_long_returns_400()
    {
        var request = new CreateDelegatedReminderRequest(
            null, null, ValidPrincipal(),
            SenderIdentity: "+14252307522",
            RecipientIdentity: "+525512345678",
            RecipientName: null, RecipientPhoneE164: null, RecipientWhatsappHandle: null,
            Content: new string('x', 1001),
            DueTimeUtc: FutureDue());

        var result = await BuildService().CreateAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    // ── Cancel validation → 400 ───────────────────────────────────────────────

    [Fact]
    public async Task Cancel_missing_principal_returns_400()
    {
        var request = new CancelDelegatedReminderRequest("c2", PrincipalContext: null);

        var result = await BuildService().CancelAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        var error = Assert.IsType<DelegatedReminderErrorResponse>(result.Body);
        Assert.Equal("c2", error.CorrelationId);
    }

    // ── List validation → 400 ─────────────────────────────────────────────────

    [Fact]
    public async Task List_missing_principal_returns_400()
    {
        var result = await BuildService().ListAsync(null, "c3", CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        var error = Assert.IsType<DelegatedReminderErrorResponse>(result.Body);
        Assert.Equal("c3", error.CorrelationId);
    }

    // ── Retry policy ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    public void Retry_backoff_matches_spec(int attempt, int expectedSeconds)
    {
        var backoff = DelegatedReminderRetryPolicy.BackoffSeconds(attempt);
        Assert.Equal(expectedSeconds, backoff);
    }

    [Fact]
    public void Retry_exhausted_after_max_attempts()
    {
        var options = new DelegatedReminderOptions { MaxDeliveryAttempts = 5 };
        var now = DateTimeOffset.UtcNow;
        var retry = DelegatedReminderRetryPolicy.NextRetry(now, 5, options.MaxDeliveryAttempts);
        Assert.Null(retry);
    }

    [Fact]
    public void Retry_window_totals_31_seconds()
    {
        var total = 0;
        for (var i = 1; i <= 5; i++)
        {
            total += DelegatedReminderRetryPolicy.BackoffSeconds(i);
        }

        Assert.Equal(31, total);
    }
}

/// <summary>Delivery channel stub that throws — ensures contract tests do not reach delivery.</summary>
internal sealed class ThrowingDelegatedDeliveryChannel : IDelegatedReminderDeliveryChannel
{
    public Task<DelegatedDeliveryResult> DeliverAsync(
        DelegatedDeliveryRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Contract tests must not reach the delivery channel.");
}
