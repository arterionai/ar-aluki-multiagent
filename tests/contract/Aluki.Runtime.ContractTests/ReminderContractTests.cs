using System.Text.Json;
using Aluki.Runtime.Capture.Persistence;
using Aluki.Runtime.Reminders;
using Aluki.Runtime.Reminders.Configuration;
using Aluki.Runtime.Reminders.Delivery;
using Aluki.Runtime.Reminders.Persistence;
using Aluki.Runtime.Reminders.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aluki.Runtime.ContractTests;

/// <summary>
/// Contract tests for the reminders request/response shape and pre-persistence
/// validation (400). These paths return before any scope/DB access, so no
/// PostgreSQL is required; accepted paths are covered by the integration suite.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ReminderContractTests
{
    private static ReminderService BuildService()
    {
        var config = new ConfigurationBuilder().Build();
        var factory = new NpgsqlConnectionFactory(config);
        return new ReminderService(
            new ReminderScopeGuard(factory),
            new ReminderStore(factory),
            new ThrowingDeliveryChannel(),
            Options.Create(new ReminderOptions()),
            NullLogger<ReminderService>.Instance);
    }

    private static ReminderPrincipalContext ValidPrincipal() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task Missing_principal_returns_400()
    {
        var request = new CreateReminderRequest(
            null, "c1", PrincipalContext: null, ReminderText: "Call Ana",
            ScheduledTimeUtc: DateTimeOffset.UtcNow.AddHours(1), Timezone: null,
            ReminderType: "one_shot", Recurrence: null, DeliveryChannel: null);

        var result = await BuildService().CreateAsync(request, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        var error = Assert.IsType<ReminderErrorResponse>(result.Body);
        Assert.Equal(ReminderErrorCode.InvalidPayload, error.Code);
        Assert.Equal("c1", error.CorrelationId);
    }

    [Fact]
    public async Task Missing_text_returns_400()
    {
        var request = new CreateReminderRequest(
            null, null, ValidPrincipal(), ReminderText: "   ",
            ScheduledTimeUtc: DateTimeOffset.UtcNow.AddHours(1), Timezone: null,
            ReminderType: "one_shot", Recurrence: null, DeliveryChannel: null);

        var result = await BuildService().CreateAsync(request, CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Past_scheduled_time_returns_400()
    {
        var request = new CreateReminderRequest(
            null, null, ValidPrincipal(), ReminderText: "Call Ana",
            ScheduledTimeUtc: DateTimeOffset.UtcNow.AddHours(-1), Timezone: null,
            ReminderType: "one_shot", Recurrence: null, DeliveryChannel: null);

        var result = await BuildService().CreateAsync(request, CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Invalid_timezone_returns_400()
    {
        var request = new CreateReminderRequest(
            null, null, ValidPrincipal(), ReminderText: "Call Ana",
            ScheduledTimeUtc: DateTimeOffset.UtcNow.AddHours(1), Timezone: "Not/AZone",
            ReminderType: "one_shot", Recurrence: null, DeliveryChannel: null);

        var result = await BuildService().CreateAsync(request, CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Recurring_request_returns_422_unsupported()
    {
        var request = new CreateReminderRequest(
            null, null, ValidPrincipal(), ReminderText: "Standup",
            ScheduledTimeUtc: DateTimeOffset.UtcNow.AddHours(1), Timezone: null,
            ReminderType: "recurring",
            Recurrence: new ReminderRecurrenceInput("daily", null, null, "never", null),
            DeliveryChannel: null);

        var result = await BuildService().CreateAsync(request, CancellationToken.None);
        Assert.Equal(422, result.StatusCode);
        var body = Assert.IsType<ReminderResponse>(result.Body);
        Assert.Equal(ReminderErrorCode.UnsupportedRecurrence, body.Error!.Code);
    }

    [Fact]
    public void Response_serializes_contract_fields_in_snake_case()
    {
        var response = new ReminderResponse(
            ReminderResponseStatus.Created, "c1",
            Reminder: new ReminderDto(
                Guid.NewGuid(), "Call Ana", DateTimeOffset.UtcNow, "America/Mexico_City",
                ReminderType.OneShot, ReminderStatus.Scheduled, 0, "in_app"),
            Quota: new QuotaInfo(1, 10, "free"));

        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("created", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("reminder", out var reminder));
        Assert.True(reminder.TryGetProperty("reminder_id", out _));
        Assert.True(reminder.TryGetProperty("scheduled_time_utc", out _));
        Assert.True(reminder.TryGetProperty("snooze_count", out _));
        Assert.True(root.TryGetProperty("quota", out var quota));
        Assert.True(quota.TryGetProperty("active_count", out _));
        Assert.True(quota.TryGetProperty("entitlement_tier", out _));
    }

    private sealed class ThrowingDeliveryChannel : IReminderDeliveryChannel
    {
        public Task<ReminderDeliveryResult> DeliverAsync(ReminderDeliveryRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("should not be called on validation paths");
    }
}
