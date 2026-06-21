# Quickstart: Validation Scenarios for Scheduled Reminders

This guide documents end-to-end validation scenarios to verify the reminders feature implementation. Each scenario includes prerequisites, setup commands, execution steps, and expected outcomes.

## Prerequisites

- .NET 10 runtime installed
- PostgreSQL database running (connection string in `appsettings.Development.json`)
- Azure Durable Functions runtime (local or cloud)
- Test data fixtures loaded (migrations 004, 005, 006 applied)
- Principal context resolver configured (Entra auth or local mock)

## Scenario 1: Create One-Shot Reminder

**Objective**: Verify that a one-shot reminder is created, persisted with correct scheduled time, and fires at the expected UTC time.

**Prerequisite State**:
- User authenticated with valid tenant_id, user_id, context_id
- Quota not exceeded (active_count < quota_limit)
- Local time zone: America/New_York

**Setup**:
```bash
# Prepare test user context
$tenantId = "00000000-0000-0000-0000-000000000001"
$userId = "00000000-0000-0000-0000-000000000002"
$contextId = "00000000-0000-0000-0000-000000000003"

# Set environment variables for principal context mock
$env:TEST_TENANT_ID = $tenantId
$env:TEST_USER_ID = $userId
$env:TEST_CONTEXT_ID = $contextId

# Start runtime host
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
```

**Execution Steps**:

1. **Submit reminder creation request** via Orleans session:
```csharp
// Pseudo-code: Orleans client calls ReminderCreateSkill
var request = new ReminderCreateRequest
{
    ReminderText = "Team standup at 2 PM",
    LocalFireTime = new TimeOnly(14, 0, 0),     // 2 PM
    Timezone = "America/New_York",
    ReminderType = "one_shot"
};

var skill = serviceProvider.GetRequiredService<ReminderCreateSkill>();
var result = await skill.ExecuteAsync(
    new SkillExecutionContext 
    { 
        TenantId = tenantId,
        UserId = userId,
        ContextId = contextId
    },
    request);

Assert.That(result.ReminderId, Is.Not.EqualTo(Guid.Empty));
Assert.That(result.Status, Is.EqualTo("scheduled"));
```

2. **Query database to verify persistence**:
```sql
SELECT reminder_id, scheduled_time_utc, original_time_local, timezone, status
FROM reminders
WHERE tenant_id = '00000000-0000-0000-0000-000000000001'
  AND reminder_text = 'Team standup at 2 PM';
```

3. **Verify scheduled_time_utc is correct**:
   - If request submitted at 1:30 PM ET (13:30) on June 21, 2026
   - Local fire time requested: 2 PM ET
   - Expected scheduled_time_utc: 2026-06-21T18:00:00Z (6 PM UTC = 2 PM ET in summer)
   - Verify: `SELECT scheduled_time_utc FROM reminders WHERE reminder_id = '<result.ReminderId>'` returns `2026-06-21T18:00:00Z`

4. **Wait for reminder to fire** (or mock timer in test):
```csharp
// In test environment: manually trigger DF orchestrator
var orchestrationClient = new DurableTaskClient("http://localhost:7071");
var instanceId = $"reminder-{reminderId}";
await orchestrationClient.TerminateInstanceAsync(instanceId);  // Simulate time passage
```

5. **Query delivery attempts**:
```sql
SELECT attempt_id, status, delivery_timestamp_utc
FROM reminder_delivery_attempts
WHERE reminder_id = '<reminderId>'
  AND scheduled_time_utc = '2026-06-21T18:00:00Z';
```

**Expected Outcomes**:
- ✓ Reminder persisted with status='scheduled'
- ✓ scheduled_time_utc is 2 PM ET converted to UTC (6 PM UTC in June)
- ✓ One row in reminder_delivery_attempts with attempt_number=1, status='delivered'
- ✓ delivery_timestamp_utc is within 5 seconds of scheduled_time_utc
- ✓ reminder.status updated to 'delivered'
- ✓ Audit event created: {event_type: "delivered", reminder_id, user_id, created_at_utc}

---

## Scenario 2: Create Recurring Daily Reminder and Verify Across DST Transition

**Objective**: Verify recurring daily reminder fires at the same local time even when timezone transitions (DST).

**Prerequisite State**:
- User in America/New_York timezone
- Recurring daily reminder requested for 9 AM
- Test date range: March 8, 2026 (before DST) through March 10, 2026 (after DST)

**Setup**:
```bash
# DST transition in New York: March 8, 2026 at 2 AM clocks spring forward to 3 AM
# So 9 AM on March 8 → 9 AM on March 9 (no offset change at 9 AM)
# Verify: same local time despite UTC offset change

$timezone = "America/New_York"
$fireTimeLocal = "09:00:00"  # 9 AM local
$startDateUtc = "2026-03-08T14:00:00Z"  # 9 AM ET = 2 PM UTC (UTC-5 before DST)
$endDateUtc = "2026-03-10T14:00:00Z"    # 9 AM EDT = 1 PM UTC (UTC-4 after DST, next day)
```

**Execution Steps**:

1. **Create recurring daily reminder**:
```csharp
var request = new ReminderCreateRequest
{
    ReminderText = "Morning standup",
    LocalFireTime = new TimeOnly(9, 0, 0),
    Timezone = "America/New_York",
    ReminderType = "recurring",
    RecurrenceRule = new RecurrenceRule
    {
        Cadence = "daily",
        StartDate = new DateTime(2026, 3, 8, 14, 0, 0, DateTimeKind.Utc),
        EndCondition = "never"
    }
};

var result = await skill.ExecuteAsync(context, request);
Assert.That(result.ReminderId, Is.Not.EqualTo(Guid.Empty));
```

2. **Query recurrence rule**:
```sql
SELECT cadence, timezone FROM reminder_recurrence_rules
WHERE reminder_id = '<reminderId>';
-- Expected: cadence='daily', (timezone implicit from reminders.timezone)
```

3. **Advance time to March 8, 2026 at 9 AM ET**:
   - Orchestrator calculates scheduled_time_utc = 2026-03-08T14:00:00Z
   - Timer fires, delivery attempt logged
   - Status: 'delivered'

4. **Verify next occurrence on March 9, 2026**:
   - DST transition happens at 2 AM ET (clocks spring forward to 3 AM)
   - 9 AM ET on March 9 = 1 PM UTC (offset is now UTC-4)
   - Scheduled_time_utc for next fire = 2026-03-09T13:00:00Z (9 AM EDT)
   - Verify: same local time (9 AM) despite UTC offset change

```sql
SELECT scheduled_time_utc, original_time_local
FROM reminders
WHERE reminder_id = '<reminderId>';
-- Expected: original_time_local = 09:00:00 (preserved)

SELECT scheduled_time_utc FROM reminder_delivery_attempts
WHERE reminder_id = '<reminderId>'
ORDER BY scheduled_time_utc;
-- Expected: 2026-03-08T14:00:00Z, 2026-03-09T13:00:00Z (offsets differ, but local time same)
```

**Expected Outcomes**:
- ✓ First fire at 2026-03-08T14:00:00Z (9 AM EST)
- ✓ Second fire at 2026-03-09T13:00:00Z (9 AM EDT, 1 hour earlier in UTC due to DST)
- ✓ Both fires at same local time (9 AM) in user's America/New_York timezone
- ✓ Audit log shows two delivery events with correct timestamps
- ✓ No manual DST adjustment logic needed; handled transparently by timezone library

---

## Scenario 3: Snooze Reminder and Verify Reschedule Without Duplicate Delivery

**Objective**: Verify snooze reschedules the same reminder to (current_time + duration) in local timezone without creating duplicates.

**Prerequisite State**:
- Reminder exists with status='firing' (notification pending)
- Current time: 2 PM ET, reminder scheduled_time_utc was 2 PM ET
- User selects snooze duration: 15 minutes

**Setup**:
```bash
$reminderId = "<from Scenario 1>"
$currentTimeLocal = "14:00"  # 2 PM ET
$snoozeDuration = 900        # 15 minutes in seconds
```

**Execution Steps**:

1. **Trigger snooze action** via Orleans session:
```csharp
var skill = serviceProvider.GetRequiredService<SnoozeReminderSkill>();
var request = new SnoozeRequest
{
    ReminderId = reminderId,
    SnoozeDurationSeconds = 900  // 15 minutes
};

var result = await skill.ExecuteAsync(context, request);
Assert.That(result.NewScheduledTimeUtc, Is.Not.EqualTo(originalScheduledTimeUtc));
```

2. **Verify reminder state updated**:
```sql
SELECT scheduled_time_utc, snooze_count, status
FROM reminders
WHERE reminder_id = '<reminderId>';
-- Expected: 
--   scheduled_time_utc = 2026-06-21T18:15:00Z (original 6 PM UTC + 15 min = 6:15 PM UTC)
--   snooze_count = 1
--   status = 'scheduled' (re-armed for next fire)
```

3. **Verify only one delivery attempt exists** (no duplicate):
```sql
SELECT COUNT(*) FROM reminder_delivery_attempts
WHERE reminder_id = '<reminderId>'
  AND attempt_number = 1;
-- Expected: 1 row (original delivery attempt)
```

4. **Verify audit event created**:
```sql
SELECT event_type, details FROM reminder_audit_events
WHERE reminder_id = '<reminderId>'
  AND event_type = 'snoozed'
ORDER BY created_at_utc DESC;
-- Expected: {event_type: 'snoozed', details: {snooze_duration_seconds: 900, new_scheduled_time_utc: '2026-06-21T18:15:00Z'}}
```

**Expected Outcomes**:
- ✓ Reminder rescheduled to current_time + 15 min (2:15 PM ET)
- ✓ scheduled_time_utc updated to 2026-06-21T18:15:00Z
- ✓ snooze_count incremented to 1
- ✓ No duplicate in reminder_delivery_attempts
- ✓ Audit event logged with new fire time
- ✓ Snooze does not affect future occurrences (recurring reminders unaffected)

---

## Scenario 4: Enforce Quota Limit and Block Creation

**Objective**: Verify quota enforcement blocks reminder creation when user exceeds limit.

**Prerequisite State**:
- Tenant quota_limit = 10 active reminders
- User has 10 active reminders (status in [scheduled, firing, delivery_failed])
- User attempts to create 11th reminder

**Setup**:
```bash
# Create 10 active reminders first (via Scenario 1 repeated 10 times)
For i = 1 To 10:
    Create reminder with status='scheduled'

# Verify quota table
SELECT active_reminder_count, quota_limit FROM reminder_quotas WHERE tenant_id = '<tenantId>';
-- Expected: active_reminder_count = 10, quota_limit = 10
```

**Execution Steps**:

1. **Attempt to create 11th reminder**:
```csharp
var request = new ReminderCreateRequest { ... };
var skill = serviceProvider.GetRequiredService<ReminderCreateSkill>();

var exception = Assert.ThrowsAsync<QuotaExceededException>(
    async () => await skill.ExecuteAsync(context, request));

Assert.That(exception.Message, Does.Contain("quota_limit"));
Assert.That(exception.Message, Does.Contain("10"));
```

2. **Verify no reminder created** (count remains 10):
```sql
SELECT COUNT(*) FROM reminders
WHERE tenant_id = '<tenantId>'
  AND status IN ('scheduled', 'firing', 'delivery_failed')
  AND deleted_at_utc IS NULL;
-- Expected: 10 (no 11th reminder)
```

3. **Verify audit event logged**:
```sql
SELECT event_type, details FROM reminder_audit_events
WHERE tenant_id = '<tenantId>'
  AND event_type = 'quota_checked'
ORDER BY created_at_utc DESC;
-- Expected: {event_type: 'quota_checked', details: {quota_remaining: 0, quota_limit: 10, result: 'rejected'}}
```

4. **Delete one reminder, then retry creation**:
```csharp
// Soft-delete a reminder
var deleteSkill = serviceProvider.GetRequiredService<DeleteReminderSkill>();
await deleteSkill.ExecuteAsync(context, new DeleteRequest { ReminderId = oldReminderId });

// Verify quota decremented
SELECT active_reminder_count FROM reminder_quotas WHERE tenant_id = '<tenantId>';
-- Expected: active_reminder_count = 9

// Retry creation
var result = await skill.ExecuteAsync(context, request);
Assert.That(result.ReminderId, Is.Not.EqualTo(Guid.Empty));
```

**Expected Outcomes**:
- ✓ 11th reminder creation rejected with QuotaExceededException
- ✓ Error message includes quota_limit (10)
- ✓ No reminder row created in database
- ✓ Audit event logged with quota_checked and result='rejected'
- ✓ After deleting one reminder, quota_count decremented to 9
- ✓ 11th reminder creation succeeds after quota available

---

## Scenario 5: Test Delivery Failure and Retry with Exponential Backoff

**Objective**: Verify delivery failure triggers 3 retries with exponential backoff, then marks reminder as terminal.

**Prerequisite State**:
- Reminder scheduled for fire at T0
- Delivery service configured to fail with transient errors (mocked)
- Test duration: ~2.5 minutes to cover all retries

**Setup**:
```bash
# Configure mock delivery service to fail transient (first 2 attempts)
# Environment: TEST_DELIVERY_MOCK_FAILURES = "1,2"  (fail attempt 1 and 2)

$reminderId = "<from Scenario 1>"
$T0 = "2026-06-21T18:00:00Z"  # Scheduled fire time
```

**Execution Steps**:

1. **Trigger reminder fire at T0**:
   - Orchestrator calls DeliverReminderActivity(attempt=1)
   - Mock returns: transient_failure (network timeout)

2. **Verify first attempt logged**:
```sql
SELECT attempt_id, attempt_number, status, next_retry_time_utc
FROM reminder_delivery_attempts
WHERE reminder_id = '<reminderId>' AND attempt_number = 1;
-- Expected: status='transient_failure', next_retry_time_utc='2026-06-21T18:00:05Z' (T0 + 5s)
```

3. **Wait 5 seconds**:
   - Orchestrator retries at T0+5s
   - DeliverReminderActivity(attempt=2) called
   - Mock returns: transient_failure (service unavailable)

4. **Verify second attempt logged**:
```sql
SELECT attempt_id, attempt_number, status, next_retry_time_utc
FROM reminder_delivery_attempts
WHERE reminder_id = '<reminderId>' AND attempt_number = 2;
-- Expected: status='transient_failure', next_retry_time_utc='2026-06-21T18:00:30Z' (T0 + 25s)
```

5. **Wait 25 seconds** (total ~30s from T0):
   - Orchestrator retries at T0+30s
   - DeliverReminderActivity(attempt=3) called
   - Mock returns: delivered (success on 3rd attempt)

6. **Verify third attempt logged as success**:
```sql
SELECT attempt_id, attempt_number, status, delivery_timestamp_utc
FROM reminder_delivery_attempts
WHERE reminder_id = '<reminderId>' AND attempt_number = 3;
-- Expected: status='delivered', delivery_timestamp_utc='2026-06-21T18:00:30Z'
```

7. **Verify reminder status updated**:
```sql
SELECT status FROM reminders WHERE reminder_id = '<reminderId>';
-- Expected: status='delivered' (no further retries)
```

8. **Verify audit events**:
```sql
SELECT event_type, details FROM reminder_audit_events
WHERE reminder_id = '<reminderId>'
ORDER BY created_at_utc;
-- Expected: 
--   {event_type: 'firing', ...}
--   {event_type: 'delivery_failed', details: {..., attempt_number: 1, failure_category: 'network_timeout'}}
--   {event_type: 'delivery_failed', details: {..., attempt_number: 2, failure_category: 'service_unavailable'}}
--   {event_type: 'delivered', details: {..., attempt_number: 3}}
```

**Expected Outcomes**:
- ✓ First attempt fails with transient_failure; retry scheduled at T0+5s
- ✓ Second attempt fails with transient_failure; retry scheduled at T0+25s
- ✓ Third attempt succeeds; reminder marked delivered
- ✓ Total time from fire to delivery: ~30 seconds (within 2.5 min SLA)
- ✓ All attempts tracked in reminder_delivery_attempts with correct timings
- ✓ Audit events logged for each attempt
- ✓ No further retries after success

---

## Scenario 6: Terminal Failure After Retry Exhaustion

**Objective**: Verify permanent failure or exhausted retries marks reminder as terminal.

**Prerequisite State**:
- Reminder scheduled for fire at T0
- Delivery service configured to fail permanently (mocked)

**Setup**:
```bash
$reminderId = "<new reminder>"
$T0 = "2026-06-21T18:00:00Z"
# Environment: TEST_DELIVERY_MOCK_FAILURES = "1,2,3"  (fail all attempts)
```

**Execution Steps**:

1. **Trigger fire and exhaust retries**:
   - T0: attempt=1 fails (transient, scheduled retry at T0+5s)
   - T0+5s: attempt=2 fails (transient, scheduled retry at T0+30s)
   - T0+30s: attempt=3 fails (permanent: invalid_recipient)

2. **Verify all attempts logged**:
```sql
SELECT COUNT(*) FROM reminder_delivery_attempts
WHERE reminder_id = '<reminderId>';
-- Expected: 3 rows (all attempts)

SELECT status FROM reminder_delivery_attempts
WHERE reminder_id = '<reminderId>' AND attempt_number = 3;
-- Expected: status='permanent_failure' (not 'transient_failure')
```

3. **Verify reminder marked terminal**:
```sql
SELECT status FROM reminders WHERE reminder_id = '<reminderId>';
-- Expected: status='delivery_failed' (terminal state)
```

4. **Verify audit event with failure reason**:
```sql
SELECT event_type, details FROM reminder_audit_events
WHERE reminder_id = '<reminderId>'
  AND event_type = 'delivery_failed'
ORDER BY created_at_utc DESC;
-- Expected: {event_type: 'delivery_failed', details: {failure_category: 'invalid_recipient', attempt_number: 3}}
```

5. **Verify quota decremented** (if applicable):
```sql
SELECT active_reminder_count FROM reminder_quotas WHERE tenant_id = '<tenantId>';
-- Expected: count decreased by 1 (failed reminder no longer active)
```

**Expected Outcomes**:
- ✓ All 3 attempts executed; no 4th retry
- ✓ Final attempt returns permanent_failure (invalid_recipient)
- ✓ Reminder status set to delivery_failed (terminal)
- ✓ No further retries scheduled
- ✓ Audit log captures failure reason and attempt number
- ✓ Quota count updated (failed reminder no longer active)

---

## Summary of Validation Coverage

| Scenario | Feature Tested | Gate |
|---|---|---|
| 1 | One-shot creation, persistence, fire | Core functionality |
| 2 | Recurring daily, DST handling, timezone correctness | Advanced scheduling |
| 3 | Snooze without duplicates, idempotency | User actions |
| 4 | Quota enforcement, blocking over-quota | Entitlement |
| 5 | Transient retry, exponential backoff, success | Resilience |
| 6 | Terminal failure, no over-retry, audit trail | Failure handling |

All scenarios must pass before release.

---

**Gate**: Quickstart scenarios complete. Ready for task generation and implementation.
