# Feature Specification: Suggestions Admin and Rewards (Starter Baseline)

Feature ID: SB-008A
Status: Draft
Date: 2026-06-21

## 1. Objective

Provide internal staff operations to triage product suggestions and apply reward incentives to submitters with deterministic, auditable, tenant-scoped behavior.

## Clarifications

### Session 2026-06-21

- Q: What is the reward idempotency boundary? -> A: Idempotency boundary is the tuple (tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id), with payload mismatch treated as conflict.
- Q: What is the admin role model for triage actions? -> A: Three-role RBAC model: AdminReviewer (read/classify), AdminApprover (final lifecycle decisions), AdminAuditor (read-only audit access).
- Q: How is audit immutability enforced? -> A: Audit and reward ledgers are append-only WORM; no update/delete, only compensating records.
- Q: What is notification retry behavior after reward grants? -> A: Notification delivery retries are decoupled from grants with bounded exponential backoff and dead-letter on exhaustion.

## 2. Architecture Adaptation

- Admin API and admin UI are isolated from conversational runtime and protected with Entra ID authentication and staff authorization.
- FeedbackAgent emits normalized suggestion lifecycle events that drive triage visibility and reward eligibility.
- Durable workflow handles reward evaluation and grant execution for retry-safe, long-running, idempotent processing.
- Entitlement ledger is the single source of truth for reward grants, reversals, and balances.
- All reads and writes remain tenant-scoped with principal context and auditable state transitions.

## 3. In Scope

- Staff-only dashboard for suggestion queue triage: category assignment, priority assignment, status updates, and reason capture.
- Full audit trail for all state-changing admin actions on status, category, and priority.
- Reward policy execution for base reward on suggestion capture, quality bonus on accepted outcome, and streak bonus for sustained qualified participation.
- Search, filter, and sort capabilities required to operate the queue under high suggestion volume.

## 4. Out of Scope

- Public ranking pages or user-visible leaderboards.
- User voting, market-style prioritization, or community moderation.
- New reward classes outside base reward, quality bonus, and streak bonus.

## 5. User Stories

### US1 - Staff triage dashboard (P1)
Given an authorized admin principal with Reviewer, Approver, or Auditor role, when the dashboard loads, then suggestions are listed with current status, category, priority, submitter metadata, timestamps, and linked attachment context.

### US2 - Submitter rewards (P2)
Given suggestion capture and lifecycle progression, when reward conditions are met, then grants are created exactly once per rule and reflected in the entitlement ledger.

### US3 - Queue filtering at scale (P3)
Given high suggestion volume, when staff apply filter, search, and sort controls, then the queue remains responsive and supports efficient triage completion.

## 6. Functional Requirements

### 6.1 Access and Authorization

FR-001: Only authenticated staff principals with one of these roles can access admin operations: AdminReviewer, AdminApprover, AdminAuditor.

FR-001A: AdminReviewer can list/detail suggestions and perform category/priority updates plus captured -> under_review transitions.

FR-001B: AdminApprover can perform all AdminReviewer actions and execute under_review -> accepted|rejected and accepted|rejected -> archived transitions.

FR-001C: AdminAuditor has read-only access to suggestion detail, audit trail, and reward decision records; mutation operations are forbidden.

FR-002: All admin operations must validate tenant scope and role authorization before reading or mutating suggestion data; denied actions must include an auditable authorization outcome.

FR-003: Unauthorized and forbidden requests must be denied without exposing suggestion content.

### 6.2 Triage Operations

FR-004: Staff can update suggestion status using a controlled lifecycle with valid transition enforcement.

FR-005: Staff can set and update category and priority values from an approved classification set.

FR-006: Each status/category/priority change must persist actor identity, previous value, new value, timestamp, tenant, and reason code in the audit trail.

FR-007: Dashboard list supports pagination and deterministic sorting for consistent queue navigation.

FR-008: Dashboard supports filter by status, category, priority, date range, and free-text search over submitter identifier and suggestion summary.

### 6.3 Reward Evaluation and Granting

FR-009: Base reward is evaluated on first successful suggestion capture event and granted once per eligible suggestion.

FR-010: Quality bonus is evaluated on transition to accepted status and granted once per eligible suggestion.

FR-011: Streak bonus is evaluated against policy-defined consecutive qualified activity and granted once per streak milestone.

FR-012: Reward grants must be idempotent under retries, duplicate events, and concurrent processing attempts.

FR-012A: The idempotency boundary for each reward rule is exactly (tenant_id, submitter_user_id, suggestion_id, reward_rule_type, source_event_id); repeated attempts with the same tuple must return the original grant outcome without side effects.

FR-012B: If an attempt reuses the same idempotency tuple but differs in policy version, grant amount, or rule metadata, processing must fail as conflict, emit telemetry, and write an auditable conflict decision.

FR-013: Entitlement ledger records each grant with immutable grant reference, rule type, source event reference, amount, tenant, submitter, and processing timestamp.

FR-013A: Audit and entitlement records are append-only WORM artifacts; update/delete operations are prohibited, and corrections must be represented as compensating records linked to the original reference.

FR-014: Reward policy caps must be enforced before grant creation; attempts that exceed policy limits must be rejected and auditable.

### 6.4 Notifications and Operational Integrity

FR-015: When a reward is granted, the system must enqueue submitter notification according to allowed message window/template constraints.

FR-016: Notification failures must not duplicate grants; retries must target notification delivery only.

FR-016A: Notification delivery uses bounded exponential backoff with five attempts (1m, 5m, 15m, 60m, 360m); after final failure the notification enters dead-letter state requiring explicit operator replay, and grant accounting remains unchanged.

FR-017: Every reward decision (granted, skipped, rejected, duplicate) must emit structured telemetry and an auditable decision record.

## 7. Data Entities and State Contracts

### 7.1 Suggestion Admin View Entity

- suggestion_id
- tenant_id
- submitter_user_id
- current_status
- current_category
- current_priority
- created_at
- updated_at
- last_admin_action_at
- attachment_count
- summary_excerpt

### 7.2 Suggestion Admin Audit Entity

- audit_id
- tenant_id
- suggestion_id
- actor_user_id
- action_type (status_change, category_change, priority_change)
- old_value
- new_value
- reason_code
- created_at
- immutable_sequence_no
- record_hash
- correction_of_audit_id (nullable)

### 7.3 Reward Entitlement Entity

- entitlement_id
- tenant_id
- submitter_user_id
- suggestion_id
- reward_rule_type (base, quality, streak)
- source_event_id
- grant_amount
- grant_status
- granted_at
- idempotency_key
- correction_of_entitlement_id (nullable)

## 8. Lifecycle and Rules

### 8.1 Triage Status Lifecycle

The admin triage lifecycle for this feature uses:

- captured
- under_review
- accepted
- rejected
- archived

Allowed transitions:

- captured -> under_review
- under_review -> accepted
- under_review -> rejected
- accepted -> archived
- rejected -> archived

No reverse transitions are allowed from archived.

### 8.2 Reward Rule Triggers

- Base reward trigger: first valid capture event for a suggestion.
- Quality bonus trigger: first transition to accepted.
- Streak bonus trigger: rule-defined streak milestone based on consecutive qualified outcomes.

Each trigger must map to a stable idempotency key derived from tenant, submitter, suggestion, reward rule type, and source event identifier.

Idempotency boundary rules:

- A reward grant is considered the same operation only when all boundary fields match exactly.
- Same boundary plus same payload is duplicate replay and must be a no-op.
- Same boundary plus different payload is a conflict and must be rejected as non-idempotent mutation attempt.
- For streak rewards, source_event_id must reference a closed streak window identifier to prevent duplicate grants during recalculation.

## 9. Acceptance Criteria

AC-001: Any non-staff request to admin operations returns denied access and no suggestion payload.

AC-002: For each admin mutation, exactly one audit record is created with actor, old/new values, reason code, and timestamp.

AC-003: Duplicate lifecycle or retry events do not create duplicate entitlement grants.

AC-003A: Reprocessing the same idempotency tuple returns the original decision and entitlement reference; changing payload under that tuple returns conflict and creates no new entitlement.

AC-004: Reward grants that exceed configured cap are not issued and include auditable rejection reason.

AC-005: Queue filtering and sorting return deterministic, paginated results under high-volume datasets.

AC-006: Reward notifications follow allowed messaging-window/template constraints and do not change grant accounting correctness.

AC-007: Exhausted notification retries result in a dead-letter notification record and no additional reward grant records.

## 10. Non-Functional Requirements

- NFR-001 (Security): Tenant and principal context enforcement is mandatory for all suggestion and reward operations.
- NFR-002 (Reliability): Reward workflow is retry-safe and idempotent across duplicate events and transient failures.
- NFR-003 (Observability): Triage and reward flows emit telemetry for access outcomes, state transitions, grant decisions, and failures.
- NFR-004 (Auditability): All side effects that alter status, classification, priority, or reward balance are traceable by immutable records.
- NFR-005 (Operational Reliability): Notification retry policy must guarantee bounded retry completion within 24 hours before terminal dead-letter state.

## 11. Dependencies

- Suggestion capture domain (SB-007) provides lifecycle events and suggestion artifacts.
- Identity and tenant authorization model from architecture baseline provides staff and scope enforcement.
- Entitlement ledger service provides canonical reward accounting store.
- Channel delivery constraints govern reward notification eligibility and template use.

## 12. Assumptions

- Entra group membership assignment is managed outside this feature, while role permissions and action matrix are defined by this specification.
- Reward cap thresholds and streak milestones are policy-configured and available at runtime.
- Suggestion capture events provide stable event identifiers suitable for idempotency.
- Notification templates and allowed windows are pre-approved and available.

## 13. Risks and Edge Cases

- Concurrent admin updates on the same suggestion must resolve deterministically and preserve a complete audit trail.
- Late or out-of-order lifecycle events must not produce invalid status transitions or duplicate rewards.
- Reward rule changes over time must not retroactively alter already finalized grants.
- Tenant boundary violations must fail closed and be observable as security events.

## 14. Constitution Alignment

- Skill-first execution preserved: FeedbackAgent and reward workflow orchestrate explicit, contract-driven behavior.
- Tenant-scoped security preserved: principal context and tenant constraints are mandatory for every operation.
- Grounded and auditable behavior preserved: all reward and triage decisions are evidence-backed and traceable.
- Durable separation preserved: long-running reward processing remains in durable workflow, not session handlers.
- Observable governance preserved: decision and transition telemetry is required for operational monitoring and policy enforcement.
