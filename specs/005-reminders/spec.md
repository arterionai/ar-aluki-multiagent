# Feature Specification: Scheduled Reminders (Starter Baseline)

Feature ID: SB-005
Status: Draft
Date: 2026-06-21

## 1. Objective

Let users schedule one-shot and recurring reminders from conversational commands with reliable delivery semantics.

## 2. Architecture Adaptation

- SchedulingAgent handles reminder intent and slot extraction.
- ReminderCreateSkill and ReminderConfirmSkill execute reminder lifecycle operations.
- Durable Functions own timer scheduling, retries, and overdue follow-up workflow.
- BudgetPolicySkill and TenantScopeSkill run before side effects.

## 3. In Scope

- One-shot reminders.
- Recurring reminders (daily/weekly/monthly).
- Done and snooze actions.
- Free-tier quota and entitlement-aware checks.

## 4. Out of Scope

- Complex recurrence editors.
- Cross-calendar conflict optimization.

## 5. User Stories

### US1 - One-shot reminder (P1)
Given user requests reminder at specific time, when schedule is accepted, then user gets confirmation and reminder notification at due time.

### US2 - Recurring reminder (P2)
Given recurring cadence request, when parsed, then recurring schedule is persisted and triggered on each cadence.

### US3 - Quota enforcement (P2)
Given free-tier limits reached, when new reminder is requested, then system blocks creation and returns clear upsell/limit message.

## 6. Acceptance Criteria

- Reminder firing is idempotent and audit logged.
- Snooze reschedules correctly without creating duplicates.
- Quota evaluation is derived from active records and entitlements.

## 7. Clarifications

### Recurrence Boundary Semantics
- **Start boundary**: Recurrence activation is inclusive; reminder fires at or after the requested start time on the first occurrence.
- **End boundary**: Recurring reminders persist indefinitely unless explicitly ended by user. No automatic expiration.
- **Cadence alignment**: Daily reminders fire at the user's preferred local time each day. Weekly/monthly reminders preserve the requested day-of-week or day-of-month in the user's home timezone.

### Timezone Drift Handling
- **User's home timezone** is the authoritative reference; all recurrence times stored in user's configured timezone (not UTC offset).
- **DST transitions**: When a user's timezone transitions (e.g., spring forward/fall back), recurring reminders shift with the local time. A 9 AM daily reminder stays at 9 AM local time through DST changes.
- **Timezone changes**: If user changes timezone in settings, active recurring reminders remain on their *original* cadence in the *new* timezone (no back-correction). New reminders adopt the updated timezone.
- **Storage**: All reminder datetimes and recurrence rules stored with explicit timezone identifier (e.g., "America/New_York"), not numeric UTC offset.

### Snooze Semantics
- **Snooze operation**: User selects snooze duration from preset options: 5 min, 15 min, 30 min, 1 hour, next day (at same local time). Snooze reschedules the *same reminder instance* (one-shot or next occurrence of recurring) to fire at (current_time + snooze_duration) in user's local timezone.
- **Defer time window**: Maximum snooze duration is 1 day. Attempting to snooze beyond 1 day rounds to "next day at original time" for recurring reminders, or caps to current_time + 24h for one-shots.
- **State preservation**: Snooze increments a `snooze_count` field for analytics. Does not reset reminder status or trigger a new notification immediately.
- **Recurring interaction**: Snoozing a recurring reminder instance does not affect the next scheduled occurrence; each instance is rescheduled independently.

### Quota Collision Behavior
- **Pre-fire quota check**: At reminder creation time, validate user's active reminder count against tenant quota (e.g., 10 free-tier active reminders per tenant). Block if exceeded.
- **Runtime quota exhaustion**: If a reminder is scheduled to fire and user is now over quota (e.g., quota was reduced, or user created reminders on multiple devices), the reminder still fires with a delivery note indicating quota status. Snooze is still available. Subsequent reminder creation requests are blocked until user reduces active count.
- **Quota counting**: Only *active* (not snoozed, not completed) reminders count against quota. Snoozed instances count as active until fired or explicitly deleted.

### Terminal Delivery Outcomes
- **Successful delivery**: Reminder notification is sent to user's preferred channel (in-app notification or channel specified in delivery contract). Marked as `delivered` with timestamp.
- **Retry on transient failure**: If delivery fails (e.g., temporary network error, service unavailable), retry up to 3 times with exponential backoff (5s, 25s, 125s) over ~2 minutes.
- **Terminal failure after retry exhaustion**: After 3 failed attempts, reminder transitions to `delivery_failed` state and is flagged in audit log. No further retries. User is *not* notified of delivery failure (to avoid cascade). Reminder remains visible in history as "Failed to send".
- **Overdue handling**: If reminder is not delivered within 24 hours of due time (due to service downtime or repeated failures), it transitions to `expired_undelivered` and is removed from active queue. One-shot reminders in this state do not retry; recurring reminders proceed to next scheduled occurrence.
- **Idempotency**: If same reminder (identified by unique reminder ID + fire timestamp) is processed twice, delivery is attempted only once. Duplicate attempts are deduplicated via idempotency key (reminder_id + scheduled_time hash).
