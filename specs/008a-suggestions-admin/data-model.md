# Data Model: Suggestions Admin and Rewards (SB-008A)

**Version**: 1.0 | **Date**: 2026-06-21 | **Storage**: PostgreSQL (RLS + append-only ledgers)

## Entity Overview

1. `suggestion_admin_view` (read model for triage queue)
2. `suggestion_admin_audit_ledger` (append-only WORM audit records)
3. `reward_entitlement_ledger` (append-only WORM reward records)
4. `reward_notification_delivery` (delivery attempt and dead-letter tracking)
5. `reward_decision_record` (normalized decision evidence by outcome)

## 1) suggestion_admin_view

Represents triage-facing operational state derived from suggestion domain records.

### Fields
- `suggestion_id` (uuid, PK in source aggregate)
- `tenant_id` (uuid, indexed, RLS scoped)
- `submitter_user_id` (text)
- `current_status` (enum: `captured|under_review|accepted|rejected|archived`)
- `current_category` (text)
- `current_priority` (enum or text)
- `created_at` (timestamptz)
- `updated_at` (timestamptz)
- `last_admin_action_at` (timestamptz, nullable)
- `attachment_count` (int)
- `summary_excerpt` (text)

### Validation Rules
- Status transitions must follow controlled transition graph.
- Category/priority values must come from approved classification sets.
- View reads are tenant-scoped and principal-authorized.

## 2) suggestion_admin_audit_ledger (append-only)

Immutable evidence for all admin state-changing actions and authorization outcomes.

### Fields
- `audit_id` (uuid, PK)
- `tenant_id` (uuid, indexed)
- `suggestion_id` (uuid, indexed)
- `actor_user_id` (text)
- `actor_role` (enum: `AdminReviewer|AdminApprover|AdminAuditor|System`)
- `action_type` (enum: `status_change|category_change|priority_change|authorization_denied|compensation`)
- `old_value` (jsonb, nullable)
- `new_value` (jsonb, nullable)
- `reason_code` (text)
- `created_at` (timestamptz)
- `immutable_sequence_no` (bigint, monotonic per tenant)
- `record_hash` (text)
- `correction_of_audit_id` (uuid, nullable)

### Immutability Rules
- No `UPDATE`/`DELETE` allowed.
- Corrections only via insert with `action_type=compensation` and linkage through `correction_of_audit_id`.

## 3) reward_entitlement_ledger (append-only)

Canonical reward accounting records.

### Fields
- `entitlement_id` (uuid, PK)
- `tenant_id` (uuid, indexed)
- `submitter_user_id` (text, indexed)
- `suggestion_id` (uuid, indexed)
- `reward_rule_type` (enum: `base|quality|streak`)
- `source_event_id` (text)
- `grant_amount` (numeric(18,4))
- `grant_status` (enum: `granted|rejected|duplicate|conflict|compensation`)
- `policy_version` (text)
- `rule_metadata` (jsonb)
- `idempotency_key` (text)
- `granted_at` (timestamptz)
- `correction_of_entitlement_id` (uuid, nullable)

### Idempotency Rules
- Unique boundary columns: `(tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id)`.
- If same boundary and same payload: return existing result (`duplicate` replay semantics).
- If same boundary and payload mismatch: return `conflict`, do not insert grant side effect row, and append decision audit.

### Policy Rules
- Policy cap validation precedes grant insert.
- Exceeding cap yields `rejected` decision with reason.

## 4) reward_notification_delivery

Tracks post-grant notification attempts independent from grant accounting.

### Fields
- `notification_delivery_id` (uuid, PK)
- `tenant_id` (uuid, indexed)
- `entitlement_id` (uuid, FK to entitlement ledger reference)
- `submitter_user_id` (text)
- `template_id` (text)
- `delivery_state` (enum: `pending|retrying|delivered|dead_letter`)
- `attempt_no` (int)
- `next_attempt_at` (timestamptz, nullable)
- `last_error_code` (text, nullable)
- `last_error_message` (text, nullable)
- `dead_letter_at` (timestamptz, nullable)
- `operator_replay_required` (bool)
- `created_at` (timestamptz)
- `updated_at` (timestamptz)

### Retry Rules
- Attempt schedule: 1m, 5m, 15m, 60m, 360m.
- Maximum five attempts, then `dead_letter`.
- Notification retry does not alter entitlement records.

## 5) reward_decision_record

Normalized decision evidence for observability and audits.

### Fields
- `decision_id` (uuid, PK)
- `tenant_id` (uuid, indexed)
- `decision_type` (enum: `granted|skipped|rejected|duplicate|conflict`)
- `decision_reason` (text)
- `idempotency_boundary` (jsonb)
- `entitlement_id` (uuid, nullable)
- `telemetry_correlation_id` (text)
- `created_at` (timestamptz)

## Lifecycle Models

### Triage Status Lifecycle
- `captured -> under_review` (Reviewer/Approver)
- `under_review -> accepted|rejected` (Approver only)
- `accepted|rejected -> archived` (Approver only)
- No reverse transitions from `archived`

### Reward Decision Lifecycle
- Receive event -> validate role/context/policy -> evaluate idempotency -> produce one decision outcome
- Outcome may emit ledger entry (`granted`) or decision-only record (`rejected|conflict|duplicate|skipped`)

### Notification Lifecycle
- `pending -> retrying -> delivered`
- `pending|retrying -> dead_letter` after attempt 5 failure
- Operator replay creates new delivery attempt chain linked to same entitlement

## Concurrency and Integrity

- Enforce idempotency boundary with unique index.
- Use transactional insert-or-select behavior for concurrent duplicate processing.
- Record all authorization denies and conflict outcomes in audit/decision stores.
- Apply tenant RLS policies on all tables keyed by `tenant_id`.

## Security and Audit Constraints

- All table access requires principal context + tenant scope.
- AdminAuditor has read-only query rights for audit/reward data.
- Mutations by AdminAuditor are forbidden and auditable as denied attempts.

## Suggested Database Constraints

1. `UNIQUE (tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id)` on `reward_entitlement_ledger`.
2. Trigger or permissions policy preventing `UPDATE`/`DELETE` on append-only tables.
3. Check constraints for valid status transitions (or enforced in service layer + audited).
4. Indexes for queue filtering: `(tenant_id, current_status, current_priority, created_at)` and search keys for submitter/summary.