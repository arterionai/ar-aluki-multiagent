# Research: Scheduled Reminders

## Decision 1: Durable Functions for Timer Scheduling and Retry Orchestration
- **Decision**: Use Azure Durable Functions (DF) as the orchestration runtime for timer scheduling, delivery, and retry workflows rather than background job queue or in-process scheduler.
- **Rationale**: 
  - DF provides built-in support for long-running timers (days/weeks/months) without external clock dependency
  - Retry and escalation workflows are declarative and observable (DF history)
  - Bounded retry with configurable backoff and terminal states match specification requirements (3 retries, exponential backoff)
  - Aligns with constitution principle IV (durable workflow separation from live session state)
  - Orleans sessions handle user interaction; DF handles long-term scheduling/delivery
- **Alternatives considered**:
  - Background job queue (Hangfire, etc.): rejected because no native timer support for long durations; scheduled job persistence adds complexity
  - In-process scheduler (NCrontab, etc.): rejected because process restarts lose timer state; single points of failure
  - External cron service: rejected because adds operational complexity and latency; DF provides native support

## Decision 2: Explicit Timezone String Storage (Not UTC Offset)
- **Decision**: Store reminder times and recurrence rules with explicit timezone identifier (e.g., "America/New_York", "Europe/London") not numeric UTC offset; use standard IANA timezone database.
- **Rationale**:
  - Numeric UTC offsets do not capture DST rules; timezone string does
  - When user timezone changes, recurrence rules remain correct in new timezone (spec clarification)
  - DST transitions automatically handled by timezone library (TimeZoneConverter); no manual logic needed
  - Matches real-world reminder UX (user thinks "9 AM", not "UTC-5" or "UTC-4")
- **Alternatives considered**:
  - UTC-only storage with separate user offset at query time: rejected because DST handling becomes manual and fragile
  - Store both string + offset: rejected because increases schema complexity and potential data inconsistency

## Decision 3: Recurrence Rule Entity (Separate from Reminder)
- **Decision**: Store recurrence rules in a separate table (`reminder_recurrence_rules`) linked by foreign key from `reminders`, not embedded in reminder row.
- **Rationale**:
  - Recurrence rules are complex (day-of-week, day-of-month, end condition) and not all reminders have them
  - Separate entity allows versioning and auditing of rule changes without updating reminder row
  - Supports future features (bulk rule updates, templates)
  - Simpler query logic: reminders with recurrence rules → join on rule_id
- **Alternatives considered**:
  - JSON column for rules: rejected because hard to query and index; rules need independent lifecycle
  - Rules computed at runtime from first fire + cadence: rejected because timezone changes break assumptions

## Decision 4: Idempotency Key at Delivery Boundary
- **Decision**: Natural idempotency key for delivery attempt tracking is `(reminder_id, scheduled_time_utc, attempt_number)`. Prevents duplicate delivery when DF retries or on crash-recovery.
- **Rationale**:
  - reminder_id uniquely identifies the logical reminder
  - scheduled_time_utc (not created_time_utc) identifies the specific fire event (important for recurring reminders)
  - attempt_number tracks which retry this is (1st delivery, 2nd retry, etc.)
  - Combination is guaranteed unique and stable across restarts
- **Alternatives considered**:
  - reminder_id only: rejected because same reminder fires multiple times (recurring); need to track per-fire attempt
  - Hash of full delivery payload: rejected because DF payload can vary on retries; need stable key

## Decision 5: Bounded Retry with Terminal Audited Failure
- **Decision**: Retry delivery up to 3 times with exponential backoff (5s, 25s, 125s, total ~2.5 min). After exhaustion, transition to `delivery_failed` terminal state; emit audit event; do not retry further.
- **Rationale**:
  - Matches specification requirement (3 retries, exponential backoff)
  - Bounded retry prevents queue starvation and operational non-determinism
  - Terminal state with audit trail ensures visibility and compliance traceability
  - Aligns with constitution principle III (grounded memory with provenance)
- **Alternatives considered**:
  - Unlimited retries with backoff: rejected due to unbounded queue growth and non-deterministic SLAs
  - No retries: rejected because transient failures (network hiccups) would be silently lost
  - Retry until end-of-day: rejected because time-bounded retry (24h max) is more testable and predictable

## Decision 6: Quota Counting on Active Reminders Only
- **Decision**: Quota count increments on reminder creation and decrements when reminder reaches terminal state (completed/cancelled/delivery_failed/expired_undelivered). Snoozed instances count as active until terminal.
- **Rationale**:
  - Matches specification: "only active (not snoozed, not completed) reminders count against quota"
  - Motivates user to clean up completed reminders (implicit quota self-service)
  - Prevents quota exhaustion by old completed reminders
  - Simplifies quota logic: single boolean (active yes/no) not complex state machines
- **Alternatives considered**:
  - Count all reminders including completed: rejected because completed reminders take quota forever
  - Count snoozed separately: rejected because spec says snoozed counts as active

## Decision 7: Delivery Status Model (Enum-Based States)
- **Decision**: Reminder state machine includes: `scheduled` → `firing` → `delivered` | `delivery_failed` | `expired_undelivered` | (user_cancelled any time)
- **Rationale**:
  - Simple, observable state machine
  - Terminal states prevent re-processing (idempotent)
  - `expired_undelivered` captures 24h+ retry-timeout case (spec requirement)
  - Audit log captures transitions for compliance and debugging
- **Alternatives considered**:
  - Single status field with sub-fields for retry attempt: rejected because states are hierarchical; enum better expresses progression
  - Separate "delivery_status" table: rejected because adds query complexity; single status field sufficient

## Decision 8: Pre-Commit Quota Check (No Runtime Quota Exhaustion Recovery)
- **Decision**: At reminder creation time, validate `user_active_reminder_count < tenant_quota_limit`. If exceeded, reject with clear error. If quota is reduced post-commit or user creates on multiple devices, reminder still fires but is marked with quota_status in audit.
- **Rationale**:
  - Matches specification: "pre-fire quota check... block if exceeded"
  - Simple implementation: single SELECT on quota table at creation
  - Consistent UX: user gets immediate feedback, not delayed rejection
  - Specification allows runtime quota exhaustion (fires with delivery note); no recovery needed
- **Alternatives considered**:
  - Runtime quota check at fire time: rejected because user gets no UX feedback at creation
  - Queued reminders with quota backlog: rejected because overcomplicates state machine and spec doesn't require it

## Decision 9: PrincipalContext Gate Before All Side Effects
- **Decision**: All reminder skills (create, confirm, snooze) require resolved `PrincipalContext` (tenant_id, user_id, context_id) before any DB write. If principal cannot be resolved, return auth error; no partial state.
- **Rationale**:
  - Constitution principle II: tenant-scoped security by default
  - Prevents cross-tenant data leakage
  - Makes all side effects auditable to principal
  - Aligns with existing skill framework in host
- **Alternatives considered**:
  - Best-effort principal fallback to default: rejected due to cross-tenant risk
  - Post-write audit: rejected because side effects must be authorized before execution

## Decision 10: Snooze Reschedules by Duration, Not Target Time
- **Decision**: Snooze action accepts duration (5 min, 15 min, 30 min, 1 hour, next day) and reschedules reminder to `current_time + duration` in user's local timezone (not target time + duration).
- **Rationale**:
  - Matches specification: "snooze reschedules the same reminder instance to fire at (current_time + snooze_duration)"
  - User perceives snooze as "postpone this for N minutes"
  - For recurring reminders, snooze only affects current instance, not future occurrences (spec requirement)
  - Snooze count incremented for analytics
- **Alternatives considered**:
  - Snooze postpones to next occurrence time: rejected because unclear for one-shot reminders
  - Snooze creates duplicate reminder: rejected because spec requires "same instance" rescheduled

## Best-Practice Notes Applied

1. **Architecture Alignment**: Durable Functions + PostgreSQL persistence + skill-based side effects follow `docs/ARCHITECTURE_BASELINE.md` (Durable Functions for long workflows, skills for business operations)
2. **Delivery Ordering**: Follows `docs/IMPLEMENTATION_ORDER.md` pipeline (security core → data core → orchestration → ingress)
3. **RLS Enforcement**: Row-level security on `reminders`, `reminder_delivery_attempts`, `reminder_audit_events` tables scoped by tenant_id (release-blocking control)
4. **Idempotency First**: Natural idempotency key at DB boundary prevents duplicate delivery under crash/retry scenarios
5. **Observable Audit Trail**: Every state change (created/scheduled/fired/snoozed/failed) logged in audit table with principal context; enables compliance evidence and debugging

---

**Gate**: All decisions documented with rationale and alternatives. Ready for Phase 1 design artifact generation.
