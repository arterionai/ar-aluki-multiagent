# Implementation Plan: Scheduled Reminders

**Branch**: `005-reminders` | **Date**: 2026-06-21 | **Spec**: `specs/005-reminders/spec.md`

**Input**: Feature specification from `specs/005-reminders/spec.md`

## Summary

Implement a reliable, tenant-scoped reminder scheduling system supporting one-shot and recurring reminders with idempotent delivery, quota enforcement, timezone-aware recurrence, snooze semantics, and terminal failure handling. The solution leverages Durable Functions for timer scheduling and retry orchestration, PostgreSQL for reminder state, skill-based lifecycle operations (ReminderCreateSkill, ReminderConfirmSkill), and policy enforcement (BudgetPolicySkill, TenantScopeSkill) to enforce constitution principles and deliver measurable SLAs.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**: 
- `Microsoft.DurableTask.Client`, `Microsoft.DurableTask.Worker.Grpc` (Durable Functions)
- `Npgsql` (PostgreSQL driver)
- `TimeZoneConverter` (timezone handling)
- `Microsoft.Extensions.Hosting`, `Azure.Identity`
- Planned additions: `xUnit`, integration test harness

**Storage**: PostgreSQL (reminders table, recurring rules table, audit log table, quota tracking table)

**Testing**: Unit tests for recurrence logic, timezone transitions, quota enforcement; integration tests for Durable Functions retry and idempotency; contract tests for skill input/output schemas

**Target Platform**: .NET hosted service with Durable Functions runtime

**Project Type**: Backend skill-orchestrated service with long-running workflow (reminder firing, retry, escalation)

**Performance Goals**:
- Reminder firing latency: P95 <= 500ms from scheduled time
- Delivery success rate: >= 99.5% for all scheduled reminders
- Quota evaluation: < 100ms at creation/snooze time

**Constraints**:
- No side effects without tenant scope and PrincipalContext
- Idempotency key: `(tenant_id, reminder_id, scheduled_time_utc)` for delivery tracking
- Retry limit: 3 attempts with exponential backoff (5s, 25s, 125s)
- Terminal outcomes: `delivered`, `delivery_failed`, `expired_undelivered`, `user_cancelled`
- Timezone storage: explicit identifier (e.g., "America/New_York"), not numeric UTC offset
- Quota counting: active (non-completed, non-cancelled) reminders only
- Maximum snooze duration: 24 hours (configurable per tenant tier)

**Scale/Scope**:
- Two reminder types: one-shot and recurring (daily/weekly/monthly cadence)
- Snooze actions with 5 preset durations
- Quota-aware creation with free-tier limits (e.g., 10 active reminders per tenant)
- Terminal delivery outcomes with audit trail and user-visible status

## Constitution Check

**Gate: Must pass before Phase 0 research. Re-check after Phase 1 design.**

### Constitution Principle I: Skill-First Execution ✓
- ReminderCreateSkill: input (tenant_id, user_id, reminder_text, scheduled_time, recurrence_rule), output (reminder_id, confirmation), side effects (persist to DB, emit audit event)
- ReminderConfirmSkill: input (reminder_id, user_approval), output (confirmation_token), side effects (update status, emit delivery_scheduled event)
- SnoozeReminderSkill: input (reminder_id, snooze_duration), output (new_fire_time), side effects (reschedule, increment snooze_count, emit audit event)
- All skills declare idempotency strategy (reminder_id + scheduled_time as natural idempotency key)

### Constitution Principle II: Tenant-Scoped Security by Default ✓
- Principal context required at ingress (resolved from user identity)
- All reminder writes scoped to tenant_id + context_id
- PostgreSQL RLS enforced on reminders table (tenant isolation)
- No quota check or reminder creation without validated tenant scope

### Constitution Principle III: Grounded Memory and Provenance ✓
- Every reminder state change logged in audit_events table (created/scheduled/fired/snoozed/failed/cancelled)
- Delivery outcome traceable to reminder_id + fire_time + delivery_attempt number
- Terminal failure reasons captured in audit log (transient vs. permanent failure category)

### Constitution Principle IV: Durable Session vs. Workflow Separation ✓
- Live session: user requests reminder (Orleans session state)
- Long-running workflow: Durable Functions orchestration (timer scheduling, retries, delivery)
- Boundary enforced: session hands off to Durable Functions workflow after skill commit; no business logic leakage

### Constitution Principle V: Cost-Aware and Observable Intelligence ✓
- Reminder creation and firing emit telemetry: latency, quota_remaining, delivery_status, attempt_count
- Quota enforcement is observable (user sees remaining quota before creation)
- Delivery retries are logged with cost impact (API calls, function invocations)

**Compliance Status**: All principles enforced. No exceptions required.

## Project Structure

### Documentation (this feature)

```text
specs/005-reminders/
├── plan.md                 # This file
├── research.md             # Phase 0 output (technical decisions)
├── data-model.md           # Phase 1 output (entities and schema)
├── contracts/
│   └── reminder-delivery-contract.yaml    # Phase 1 output (delivery payload schema)
├── quickstart.md           # Phase 1 output (validation guide)
└── tasks.md                # Phase 2 output (generated by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Aluki.Runtime.Abstractions/
│   ├── Skills/
│   │   ├── ReminderCreateSkill.cs          # Skill interface + context
│   │   ├── ReminderConfirmSkill.cs
│   │   ├── SnoozeReminderSkill.cs
│   │   └── ReminderLifecycleSkill.cs
│   ├── Orchestration/
│   │   └── ReminderOrchestrator.cs         # Durable Functions orchestrator
│   └── Models/
│       ├── Reminder.cs
│       ├── RecurrenceRule.cs
│       └── DeliveryOutcome.cs
│
├── Aluki.Runtime.Host/
│   ├── Skills/
│   │   ├── ReminderCreateSkillImpl.cs       # Skill implementations
│   │   ├── ReminderConfirmSkillImpl.cs
│   │   └── SnoozeReminderSkillImpl.cs
│   ├── Orchestration/
│   │   ├── ReminderOrchestrator.cs         # Durable Functions activities
│   │   └── ReminderTimerActivity.cs
│   └── Data/
│       ├── ReminderRepository.cs
│       └── ReminderQuotaRepository.cs
│
└── Aluki.Runtime.Functions/
    ├── Activities/
    │   ├── ScheduleReminderActivity.cs     # Durable activity: schedule timer
    │   ├── DeliverReminderActivity.cs      # Durable activity: send notification
    │   └── HandleFailedDeliveryActivity.cs
    └── Orchestrators/
        └── ReminderLifecycleOrchestrator.cs # Orchestrator: timer → delivery → retries → terminal

db/
├── migrations/
│   ├── 004_init_reminders.sql              # Reminders table + RLS
│   ├── 005_init_reminder_rules.sql         # Recurrence rules table
│   └── 006_init_reminder_audit.sql         # Audit event log
```

## Phase 0: Research Artifacts

**Prerequisite**: Technical context validation and dependency research.

**Deliverables**:
- research.md: Architecture decisions (Durable Functions vs. background jobs, recurrence library vs. custom, timezone handling)
- Best practices applied: Skill-first execution, principal context gates, idempotency at DB boundary, bounded retries with terminal audited failure

## Phase 1: Design Artifacts

**Prerequisite**: research.md complete.

**Deliverables**:
1. **data-model.md**: Entity definitions
   - `reminders` table (reminder_id, tenant_id, context_id, user_id, scheduled_time_utc, original_time_utc, recurrence_rule_id, snooze_count, status, timezone)
   - `reminder_recurrence_rules` table (rule_id, reminder_id, cadence, day_of_week, day_of_month, end_condition)
   - `reminder_delivery_attempts` table (attempt_id, reminder_id, scheduled_time_utc, attempt_number, status, failure_reason, delivered_at_utc)
   - `reminder_audit_events` table (event_id, reminder_id, event_type, principal_id, timestamp, details)
   - `reminder_quotas` table (tenant_id, context_id, user_id, active_reminder_count, quota_limit, entitlement_tier)
   - RLS policies enforcing tenant isolation

2. **contracts/reminder-delivery-contract.yaml**: Delivery payload schema
   - Input: `(reminder_id, user_id, tenant_id, scheduled_time_utc, reminder_text, delivery_channel, snooze_options[])`
   - Output: `(delivery_status, notification_id, delivery_timestamp_utc, next_retry_time_utc?)`
   - Expected channels: in-app notification, SMS (future), email (future)

3. **quickstart.md**: Validation scenarios
   - Scenario 1: Create one-shot reminder, verify persisted with correct scheduled time
   - Scenario 2: Create recurring daily reminder, verify fire at expected times across timezone transitions
   - Scenario 3: Snooze a reminder, verify reschedule without duplicate delivery
   - Scenario 4: Test quota enforcement (create > limit, verify rejection)
   - Scenario 5: Simulate delivery failure and verify retry with exponential backoff

## Key Decisions Made

1. **Durable Functions for Timer Orchestration**: Schedule long-running timers using DF orchestrator with retry/escalation workflows rather than background job queue or external scheduler
2. **Timezone Explicit Storage**: Store timezone identifier string ("America/New_York") not UTC offset; handle DST transitions transparently
3. **Recurrence Rule Persistence**: Store recurrence rule as separate entity, not computed at runtime; avoids edge cases with timezone changes
4. **Idempotency Key at Delivery**: `(reminder_id, scheduled_time_utc)` natural key for delivery attempt tracking; prevents duplicate delivery on retries
5. **Terminal Failure Outcomes**: Bounded retries (3 attempts) with terminal states; no silent loss or unbounded retry loops
6. **Quota Counting**: Active reminders only (status in [scheduled, snoozed, firing]); completed/cancelled do not count

## Dependencies and Risks

| Dependency | Impact | Mitigation |
|---|---|---|
| Durable Functions availability | Reminder firing delayed if DF service degrades | Multi-region failover in infra plan; monitor invocation latency |
| PostgreSQL RLS performance | Quota check / audit queries on large tables | Indexed queries on (tenant_id, user_id); partition by tenant if scale warrants |
| Timezone library correctness | Recurrence misalignment during DST edge cases | Use well-maintained library (TimeZoneConverter); validate with test cases spanning historical DST changes |
| Delivery channel reliability | Reminders not reaching user if notification channel fails | Retry up to 3 times; user can re-snooze; delivery_failed state visible in history |

## Gate: Technical Readiness

- [ ] research.md complete (decisions documented with rationale)
- [ ] data-model.md complete (all entities defined with validation rules)
- [ ] contracts generated with input/output schemas
- [ ] quickstart.md complete (runnable validation scenarios)
- [ ] No "NEEDS CLARIFICATION" markers remain in Technical Context

---

**Next Step**: Execute Phase 0 research to validate architecture decisions, then Phase 1 design to generate all artifacts.

