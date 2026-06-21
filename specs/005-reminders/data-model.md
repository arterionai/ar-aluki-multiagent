# Data Model: Scheduled Reminders

## Entity: reminders
**Purpose**: Core reminder entity storing one-shot and recurring reminder schedules with timezone-aware persistence.

**Table**: `reminders`

**Fields**:
- `reminder_id` (uuid, PK) — Stable identifier for reminder lifecycle
- `tenant_id` (uuid, not null) — Tenant scope (RLS column)
- `context_id` (uuid, not null) — User context (personal, team, etc.)
- `user_id` (uuid, not null) — Creator and recipient of reminder
- `reminder_text` (text, not null) — User-facing reminder message
- `scheduled_time_utc` (timestamptz, not null) — Next fire time in UTC for comparison and timer scheduling
- `original_time_local` (time, not null) — Requested fire time in user's local time (e.g., 9:00 AM); preserved across snoozes
- `timezone` (text, not null) — IANA timezone identifier (e.g., "America/New_York"); authoritative for recurrence
- `reminder_type` (text, not null, enum: one_shot|recurring) — Distinguishes one-shot from recurring
- `recurrence_rule_id` (uuid, nullable, FK → reminder_recurrence_rules) — References recurrence rule if recurring
- `snooze_count` (int, default 0) — Total snoozes applied to this reminder (analytics)
- `quota_tier` (text, nullable) — Entitlement tier at creation time (e.g., "free", "pro"); used for quota limit reference
- `status` (text, not null, enum: scheduled|firing|delivered|delivery_failed|expired_undelivered|user_cancelled) — Lifecycle state
- `delivery_channel` (text, not null, default: in_app) — Target delivery channel (in_app, sms, email, etc.)
- `created_at_utc` (timestamptz, not null) — Creation timestamp
- `updated_at_utc` (timestamptz, not null) — Last status update
- `deleted_at_utc` (timestamptz, nullable) — Soft delete timestamp (user cancelled)

**Validation Rules**:
- `tenant_id`, `user_id`, `context_id` are required for all operations
- `scheduled_time_utc` must be > current time (future time enforcement at application layer)
- `timezone` must be valid IANA identifier; enforced via constraint check or application validation
- `reminder_text` length between 1 and 500 characters
- `snooze_count` >= 0
- `status` transitions follow state machine: scheduled → (firing → delivered | delivery_failed | expired_undelivered) | user_cancelled

**Indexes**:
- `(tenant_id, user_id)` — For listing user's reminders
- `(status, scheduled_time_utc)` — For fire-ready query (WHERE status='scheduled' AND scheduled_time_utc < now())
- `(tenant_id, status)` — For quota counting (active reminder count by tenant)

**RLS Policy**:
```sql
-- Users can only see/modify their own reminders
CREATE POLICY reminder_tenant_isolation ON reminders
  USING (tenant_id = current_setting('app.tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid AND user_id = current_setting('app.user_id')::uuid);
```

---

## Entity: reminder_recurrence_rules
**Purpose**: Encodes recurrence patterns (daily, weekly, monthly) with DST-aware timezone context.

**Table**: `reminder_recurrence_rules`

**Fields**:
- `rule_id` (uuid, PK)
- `reminder_id` (uuid, not null, FK → reminders) — Links to parent reminder
- `tenant_id` (uuid, not null) — RLS column for consistency
- `cadence` (text, not null, enum: daily|weekly|monthly) — Recurrence frequency
- `day_of_week` (text, nullable, array) — Applicable for weekly cadence (e.g., ["Mon", "Wed", "Fri"])
- `day_of_month` (int, nullable) — Applicable for monthly cadence (1-31); 31 interpreted as "last day of month"
- `end_condition` (text, nullable, enum: never|until_date) — Termination rule (no automatic expiration per spec)
- `end_date_utc` (timestamptz, nullable) — If end_condition='until_date', reminder stops after this date
- `active` (boolean, default true) — Soft-delete for rule (user cancels recurring series)
- `created_at_utc` (timestamptz, not null)
- `updated_at_utc` (timestamptz, not null)

**Validation Rules**:
- If `cadence` = 'daily': no day_of_week or day_of_month required
- If `cadence` = 'weekly': day_of_week must be non-empty
- If `cadence` = 'monthly': day_of_month must be in [1, 31]
- If `end_condition` = 'until_date': end_date_utc must be > current time
- If `end_condition` = null or 'never': end_date_utc must be null

**Indexes**:
- `(reminder_id)` — For joining reminders with rules
- `(active, end_date_utc)` — For sweep queries (find expired recurring rules to transition to inactive)

**RLS Policy**:
```sql
CREATE POLICY recurrence_rule_tenant_isolation ON reminder_recurrence_rules
  USING (tenant_id = current_setting('app.tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid);
```

---

## Entity: reminder_delivery_attempts
**Purpose**: Audit trail for each delivery attempt (initial fire + retries), tracking status and failure reasons.

**Table**: `reminder_delivery_attempts`

**Fields**:
- `attempt_id` (uuid, PK)
- `reminder_id` (uuid, not null, FK → reminders)
- `tenant_id` (uuid, not null) — RLS column
- `scheduled_time_utc` (timestamptz, not null) — The fire time for this specific event (recurring reminders fire multiple times)
- `attempt_number` (int, not null) — 1 for initial delivery, 2-3 for retries
- `status` (text, not null, enum: pending|delivered|transient_failure|permanent_failure|retry_scheduled) — Attempt outcome
- `failure_category` (text, nullable, enum: network_timeout|service_unavailable|invalid_recipient|rate_limited|unknown) — Failure classification
- `failure_message` (text, nullable) — Human-readable error detail
- `delivery_timestamp_utc` (timestamptz, nullable) — When delivery was confirmed (only if status='delivered')
- `next_retry_time_utc` (timestamptz, nullable) — Scheduled time for next retry (only if status='retry_scheduled')
- `notification_id` (text, nullable) — Provider-generated ID (e.g., Firebase, SMS gateway) for tracking
- `created_at_utc` (timestamptz, not null) — Attempt initiated
- `updated_at_utc` (timestamptz, not null) — Last status update

**Validation Rules**:
- `(reminder_id, scheduled_time_utc, attempt_number)` combination is unique (idempotency key)
- `attempt_number` >= 1 and <= 3 (enforce bounded retry constraint at application layer)
- If `status` = 'delivered': delivery_timestamp_utc must be present
- If `status` = 'retry_scheduled': next_retry_time_utc must be > current time
- If `status` = 'transient_failure' or 'permanent_failure': failure_category must be non-null

**Indexes**:
- `(reminder_id, scheduled_time_utc, attempt_number)` — Natural idempotency key for delivery dedupe
- `(status, next_retry_time_utc)` — For finding due-for-retry attempts
- `(tenant_id, status)` — For delivery success metrics by tenant

**RLS Policy**:
```sql
CREATE POLICY delivery_attempt_tenant_isolation ON reminder_delivery_attempts
  USING (tenant_id = current_setting('app.tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid);
```

---

## Entity: reminder_audit_events
**Purpose**: Immutable audit log for compliance, debugging, and quota tracking.

**Table**: `reminder_audit_events`

**Fields**:
- `event_id` (uuid, PK)
- `reminder_id` (uuid, not null, FK → reminders)
- `tenant_id` (uuid, not null) — RLS column
- `user_id` (uuid, not null) — Actor who triggered the event (user or system)
- `event_type` (text, not null, enum: created|scheduled|firing|delivered|delivery_failed|snoozed|cancelled|quota_checked|expired_undelivered) — Event category
- `details` (jsonb, nullable) — Event-specific metadata (e.g., snooze_duration, failure_reason, quota_remaining)
- `correlation_id` (text, nullable) — Links to parent request/workflow for tracing
- `created_at_utc` (timestamptz, not null) — Event timestamp

**Validation Rules**:
- event_type must be one of the defined enum values
- For event_type='quota_checked': details should include {quota_remaining: int, quota_limit: int}
- For event_type='delivery_failed': details should include {failure_category: text, attempt_number: int}
- For event_type='snoozed': details should include {snooze_duration_seconds: int, new_scheduled_time_utc: timestamp}

**Indexes**:
- `(tenant_id, event_type)` — For audit queries filtered by event type
- `(reminder_id)` — For viewing full event history of a reminder
- `(created_at_utc)` — For time-range queries (e.g., last 7 days)

**RLS Policy**:
```sql
CREATE POLICY audit_event_tenant_isolation ON reminder_audit_events
  USING (tenant_id = current_setting('app.tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid);
```

---

## Entity: reminder_quotas
**Purpose**: Tracks active reminder count per tenant and enforces entitlement-based limits.

**Table**: `reminder_quotas`

**Fields**:
- `quota_id` (uuid, PK)
- `tenant_id` (uuid, not null, unique) — One row per tenant
- `active_reminder_count` (int, not null, default 0) — Running count of active reminders
- `quota_limit` (int, not null, default 10) — Maximum active reminders for this tenant
- `entitlement_tier` (text, not null, default: free, enum: free|pro|enterprise) — Tenant tier determining limit
- `last_checked_at_utc` (timestamptz, not null) — When count was last reconciled
- `created_at_utc` (timestamptz, not null)
- `updated_at_utc` (timestamptz, not null)

**Validation Rules**:
- `active_reminder_count` >= 0 and <= `quota_limit` (enforced at application layer; quota_limit is source of truth)
- `quota_limit` >= 1
- `entitlement_tier` must be one of the defined tiers

**Indexes**:
- `(tenant_id)` — For quick lookup during reminder creation
- `(entitlement_tier)` — For bulk tier updates (e.g., upgrade campaign)

**RLS Policy**: 
Not strictly needed since one row per tenant, but for safety:
```sql
CREATE POLICY quota_tenant_isolation ON reminder_quotas
  USING (tenant_id = current_setting('app.tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid);
```

---

## State Machine: Reminder Lifecycle

```
              [scheduled]
               /        \
          snooze()      (timer fires)
             /                \
        [scheduled]       [firing]
                               |
                        (delivery attempt)
                         /    |     \
                        /     |      \
                  success  transient  permanent
                    |      failure     failure
               [delivered]   |           |
                             |      (exhaust retries)
                        (retry)          |
                            |      [delivery_failed]
                            |
                        [firing] (next retry)
                        
    Any state → [user_cancelled] (explicit cancel)
    
    [scheduled] + (24h + fire_time elapsed) → [expired_undelivered]
```

---

## Migration and Initialization Script

See `db/migrations/004_init_reminders.sql`, `005_init_reminder_rules.sql`, `006_init_reminder_audit.sql` for full schema with constraints, indexes, RLS policies, and partition strategy (if any).

**Key Migration Considerations**:
1. Timezone identifier validation: ensure IANA database is available in deployment environment
2. RLS policy activation: test with sample tenant_id to verify no data leakage
3. Recurrence rule backward compatibility: handle future changes to cadence rules (e.g., custom intervals)

---

## Idempotency Strategy

All side effects in the reminder service use the natural idempotency key `(reminder_id, scheduled_time_utc, attempt_number)` tracked in `reminder_delivery_attempts`:

1. **Reminder Creation**: `reminder_id` generated by application (stable UUID); if same creation request retried, duplicate insert violates PK constraint → error returned, no duplicate created
2. **Reminder Delivery**: `(reminder_id, scheduled_time_utc, attempt_number)` tuple is unique; if DF retries delivery, second INSERT into `reminder_delivery_attempts` with same key → duplicate key error handled gracefully (no re-delivery)
3. **Snooze**: Update to `scheduled_time_utc` on reminders table is idempotent (same snooze request updates to same time); snooze audit event keyed by `event_id`, so duplicate snooze requests create multiple audit events (acceptable for visibility)

---

## Quota Counting Logic

**Active Reminder Count**:
```sql
SELECT COUNT(*) FROM reminders 
WHERE tenant_id = $1 
  AND status IN ('scheduled', 'firing', 'delivery_failed') 
  AND deleted_at_utc IS NULL;
```

**Quota Check (at creation time)**:
```sql
SELECT 
  (SELECT COUNT(*) FROM reminders WHERE tenant_id = $1 AND status IN ('scheduled', 'firing', 'delivery_failed') AND deleted_at_utc IS NULL) as active_count,
  (SELECT quota_limit FROM reminder_quotas WHERE tenant_id = $1) as quota_limit
WHERE active_count < quota_limit;
```

If query returns no rows or active_count >= quota_limit, reject reminder creation with error `QUOTA_EXCEEDED`.

---

**Gate**: Data model complete. Ready for design artifact validation and quickstart scenario authoring.
