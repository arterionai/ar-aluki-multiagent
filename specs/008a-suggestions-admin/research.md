# Research Report: Suggestions Admin and Rewards (SB-008A)

**Date**: 2026-06-21 | **Status**: Complete | **Spec Session**: 2026-06-21

## Scope

This research resolves technical decisions required to implement the suggestions admin and rewards feature with deterministic idempotency, role-separated admin access, immutable append-only ledgers, and decoupled notification retry.

## Decision 1: Reward Idempotency Boundary

**Decision**: Use the exact tuple `(tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id)` as the idempotency boundary for reward processing.

**Rationale**:
- Matches clarified requirement FR-012A exactly.
- Prevents duplicate grant issuance across retries and concurrent workers.
- Supports deterministic replay outcomes by requiring same boundary and same payload to be treated as duplicate no-op.

**Alternatives considered**:
- Boundary without `source_event_id`: rejected because it cannot distinguish distinct legitimate rule triggers.
- Boundary with only synthetic `idempotency_key`: rejected because it weakens explainability and enforcement against payload mutations.

## Decision 2: Conflict-on-Payload-Mismatch Behavior

**Decision**: For the same idempotency tuple, mismatch in policy version, amount, or rule metadata returns `conflict` with no grant side effect and writes an auditable decision.

**Rationale**:
- Aligns with FR-012B and AC-003A.
- Prevents hidden non-idempotent mutations during reprocessing.
- Creates a deterministic, observable operational signal for policy drift or bug scenarios.

**Alternatives considered**:
- Auto-overwrite existing grant payload: rejected because it breaks immutability and audit correctness.
- Last-write-wins conflict policy: rejected because concurrent retries would become non-deterministic.

## Decision 3: Admin Role Model and Action Matrix

**Decision**: Enforce three-role RBAC model:
- `AdminReviewer`: list/detail, category/priority updates, `captured -> under_review`
- `AdminApprover`: all reviewer actions plus final transitions (`under_review -> accepted|rejected`, `accepted|rejected -> archived`)
- `AdminAuditor`: read-only access to suggestion details, audit trail, and reward decisions

**Rationale**:
- Matches FR-001A/B/C.
- Separates operational triage from approval authority.
- Preserves least-privilege and supports clear access-audit reporting.

**Alternatives considered**:
- Single admin super-role: rejected for weak separation of duties.
- Reviewer + auditor only: rejected because approval boundaries become ambiguous.

## Decision 4: Audit and Entitlement Immutability

**Decision**: Implement audit and entitlement stores as append-only WORM ledgers. No update/delete operations; corrections are represented by compensating records linked via `correction_of_*` references.

**Rationale**:
- Directly satisfies FR-013A.
- Preserves forensic and compliance traceability.
- Simplifies operational reasoning by making all history explicit and non-destructive.

**Alternatives considered**:
- Soft-delete or status overwrite: rejected due to history mutation risk.
- Mutable rows with history snapshots: rejected as more complex and weaker against accidental overwrite.

## Decision 5: Notification Retry Decoupled from Granting

**Decision**: Reward grant commit and notification delivery are decoupled workflows. Notification delivery retries only the notification side with bounded backoff schedule: `1m, 5m, 15m, 60m, 360m` (five attempts total), then dead-letter.

**Rationale**:
- Satisfies FR-016 and FR-016A.
- Guarantees grant accounting does not change during notification failures.
- Supports operator replay from dead-letter without touching entitlement ledger.

**Alternatives considered**:
- Inline synchronous notification in grant transaction: rejected due to coupling and failure blast radius.
- Unlimited retries: rejected due to non-terminating behavior and operational uncertainty.

## Decision 6: Durable Orchestration and Concurrency Handling

**Decision**: Use Durable Functions for reward and notification orchestration with a database-enforced unique idempotency boundary and transactional ledger writes.

**Rationale**:
- Matches constitution durable workflow separation.
- Handles concurrent processing races using constraint-driven resolution.
- Keeps workflow retries deterministic across restarts.

**Alternatives considered**:
- In-memory lock-only approach: rejected because it fails under scale-out and process restarts.
- Queue consumer without durable orchestration: rejected because long retry chains are harder to observe and recover.

## Best Practices Applied

- Skill-first side effects with explicit contract boundaries.
- Tenant + principal checks on all reads/writes.
- Structured telemetry for grant outcomes: granted, skipped, rejected, duplicate, conflict.
- Immutable append-only data discipline for audit and entitlement records.
- Dead-letter terminal path with explicit operator replay.

## Research Outcome

All prior clarifications are resolved and reflected in technical choices. No remaining `NEEDS CLARIFICATION` items remain for this feature planning scope.