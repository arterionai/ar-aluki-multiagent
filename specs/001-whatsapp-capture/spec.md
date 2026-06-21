# Feature Specification: WhatsApp Capture Foundation

**Feature Branch**: `001-whatsapp-capture`

**Created**: 2026-06-21

**Status**: Draft

**Input**: User description: "Refine and harden the existing WhatsApp capture foundation specification while keeping scope unchanged."

## Clarifications

### Session 2026-06-21

- Q: What is the idempotency key and duplicate-delivery behavior? -> A: The canonical idempotency key is `(tenant_id, source_channel, provider_message_id)`; duplicate deliveries must return duplicate-safe acknowledgment and must not create or mutate canonical message/media artifacts.
- Q: How are tenant/context derived and enforced? -> A: Capture must resolve `PrincipalContext` before side effects by deriving tenant from channel membership and deriving context from explicit context metadata when present, otherwise defaulting to the principal's primary personal context; unresolved or mismatched derivation is denied and audited.
- Q: What is the unsupported payload fallback policy? -> A: Unsupported payloads are acknowledged as accepted-but-unsupported, persisted as a minimal capture artifact with unsupported classification and raw envelope reference, and surfaced for deferred handling without breaking continuity.
- Q: Which audit events are mandatory? -> A: Mandatory events are `capture.accepted`, `capture.duplicate_suppressed`, `capture.scope_denied`, `capture.unsupported_payload`, `capture.retry_scheduled`, and `capture.failed_terminal`, all with correlation and scope identifiers.
- Q: What are synchronous-path SLA and retry/backoff boundaries? -> A: The synchronous acknowledgment path must meet P95 <=2s and P99 <=3s for valid non-blocking events; transient persistence failures may retry up to 5 attempts with bounded exponential backoff and terminal failure must be audited after retry exhaustion.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Capture inbound WhatsApp messages reliably (Priority: P1)

As a WhatsApp user, I need my inbound messages to be captured consistently so that my information is not lost and can be used by downstream memory features.

**Why this priority**: This is the baseline channel entry point; without reliable capture, all downstream product value is blocked.

**Independent Test**: Can be fully tested by sending valid WhatsApp text, image, audio, and forwarded messages and verifying each is captured once with complete required metadata.

**Acceptance Scenarios**:

1. **Given** a valid inbound WhatsApp text message, **When** the capture flow runs, **Then** exactly one normalized message record is persisted with required identity and provenance fields and an acknowledgment is returned.
2. **Given** a valid inbound WhatsApp image or audio message, **When** the capture flow runs, **Then** exactly one message record and one associated media artifact record are persisted with content type and provenance.
3. **Given** the same inbound delivery is retried by the channel, **When** capture executes again, **Then** no duplicate persisted message is created and processing is marked as duplicate-safe.

---

### User Story 2 - Enforce tenant and context isolation at capture time (Priority: P1)

As a tenant member, I need captured artifacts to remain isolated to my tenant and context so that other tenants or unauthorized users cannot access them.

**Why this priority**: Tenant-scoped security is non-negotiable per constitution and is required before broader feature rollout.

**Independent Test**: Can be fully tested by capturing messages under two separate tenants and validating isolation using authorized and unauthorized access attempts.

**Acceptance Scenarios**:

1. **Given** two tenants with separate contexts, **When** each sends messages, **Then** each tenant can only retrieve its own captured artifacts.
2. **Given** a request missing tenant or principal context, **When** capture is attempted, **Then** the operation is denied and an audit event is recorded.

---

### User Story 3 - Preserve operational reliability and traceability (Priority: P2)

As an operations owner, I need failures and retries in capture processing to be visible and traceable so that no message is silently dropped.

**Why this priority**: Reliability and observability are required to support production operation and incident response.

**Independent Test**: Can be fully tested by injecting transient and permanent failures and confirming retriable behavior, failure outcomes, and audit/telemetry visibility.

**Acceptance Scenarios**:

1. **Given** a transient failure during capture persistence, **When** retryable processing runs, **Then** the message is eventually persisted once and telemetry includes retry attempts.
2. **Given** a permanent processing failure, **When** retries are exhausted, **Then** failure is recorded with reason and correlation identifiers, and no silent success is reported.

---

### Edge Cases

- Duplicate webhook deliveries with identical provider message identifiers.
- Out-of-order delivery where a forwarded message arrives before related media metadata.
- Unsupported or malformed payload shape from channel provider.
- Missing or invalid tenant/context/principal inputs.
- Very large media payload metadata where upload may complete later than initial message capture.
- Acknowledgment path succeeds while persistence path fails.
- Provider retries after initial acknowledgment due to network uncertainty.
- STOP or ALTO consent state active for the target principal or context.

## Requirements *(mandatory)*

### Scope Boundaries

- This feature covers inbound WhatsApp capture only as the first production channel.
- This feature includes message normalization, persistence of message/media artifacts, provenance metadata, deduplication safeguards, and baseline acknowledgment behavior.
- This feature does not include semantic recall, deep extraction, advanced media understanding, calendar/reminder actions, or multi-channel expansion.

### Actors

- End User: Sends inbound WhatsApp messages and receives delivery acknowledgment.
- Tenant Member: Authorized actor whose content is captured within a specific tenant/context boundary.
- Operations Owner: Monitors reliability, retries, failures, and audit evidence.
- Compliance Auditor: Verifies security controls, provenance, and complete audit trail for side effects.

### Functional Requirements

- **FR-001**: System MUST accept valid inbound WhatsApp text, image, audio, and forwarded-message events and normalize them into a unified message representation.
- **FR-002**: System MUST persist one canonical capture record per unique inbound message event and preserve immutable provenance to the source message.
- **FR-003**: System MUST persist required identity and scope attributes on each captured artifact: tenant identifier, context identifier, initiating user identifier, and source channel.
- **FR-004**: System MUST enforce idempotent processing so repeated inbound deliveries of the same message do not produce duplicate capture records.
- **FR-005**: System MUST enforce tenant and principal scope checks before any capture read/write side effect is executed.
- **FR-006**: System MUST reject capture operations that lack valid tenant, context, or principal scope and MUST emit an audit event for the denial.
- **FR-007**: System MUST return a minimal acknowledgment outcome for accepted inbound events and a controlled failure outcome for rejected/invalid events.
- **FR-008**: System MUST record audit evidence for each capture side effect, denial, and terminal failure with correlation identifiers sufficient for traceability.
- **FR-009**: System MUST process transient failures using retry-safe behavior and MUST ensure eventual single-record persistence or terminal audited failure.
- **FR-010**: System MUST classify unsupported inbound content into a controlled unsupported-content outcome without breaking session continuity.
- **FR-011**: System MUST respect active consent-stop states by enforcing policy-defined behavior before capture side effects proceed.
- **FR-012**: System MUST emit telemetry for each critical capture stage, including latency, result status, and failure category.
- **FR-013**: System MUST treat `(tenant_id, source_channel, provider_message_id)` as the canonical idempotency key and MUST suppress duplicate deliveries by returning duplicate-safe acknowledgment without creating or mutating canonical message/media artifacts.
- **FR-014**: System MUST derive and validate `PrincipalContext` before side effects by resolving tenant from channel membership and context from explicit context metadata when present, otherwise the principal's default personal context; unresolved, mismatched, or unauthorized scope MUST be denied and audited.
- **FR-015**: System MUST handle unsupported payloads with accepted-but-unsupported fallback behavior that persists a minimal artifact containing unsupported classification, raw envelope reference, tenant/context scope, and provenance metadata.
- **FR-016**: System MUST emit the mandatory audit event set for capture lifecycle outcomes: `capture.accepted`, `capture.duplicate_suppressed`, `capture.scope_denied`, `capture.unsupported_payload`, `capture.retry_scheduled`, and `capture.failed_terminal`.
- **FR-017**: System MUST bound transient retry behavior to a maximum of 5 attempts with bounded exponential backoff and MUST produce a terminal audited failure outcome when retry budget is exhausted.

### Non-Goals

- Generating semantic answers from captured content.
- Performing deep entity extraction, summarization, OCR interpretation, or transcription interpretation quality scoring.
- Implementing additional channel adapters beyond WhatsApp.
- Designing incentive/admin workflows tied to feedback or suggestions.

### Dependencies

- Tenant identity, membership, and principal-context resolution are available and enforced.
- Durable persistence and artifact storage for message and media capture are available.
- Row-level isolation policies and audit infrastructure are operational.
- Channel webhook contract provides a stable unique message identity for deduplication.
- Observability pipeline is available for capture telemetry and failure investigation.

### Key Entities *(include if feature involves data)*

- **Inbound Message Event**: Provider-originated inbound event carrying message identifiers, sender identity, timestamps, content envelope, and delivery metadata.
- **Unified Message Artifact**: Normalized, tenant-scoped record representing the captured message with source channel, message type, and provenance links.
- **Media Artifact**: Associated record for image/audio payload metadata and retrieval reference linked to a unified message artifact.
- **Idempotency Record**: Deduplication marker that associates a unique inbound event identity with one canonical capture result.
- **Audit Event**: Immutable operational/compliance record describing capture action, denial, retry, or failure with actor, scope, and correlation identifiers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 99.5% of valid inbound WhatsApp messages are captured successfully as a single canonical record within 60 seconds of receipt.
- **SC-002**: Duplicate inbound deliveries produce duplicate persisted message artifacts in 0% of tested retry/redelivery scenarios.
- **SC-003**: 100% of persisted capture artifacts include required scope and provenance fields.
- **SC-004**: 100% of denied capture attempts due to missing/invalid scope produce an auditable denial record.
- **SC-005**: 100% of terminal capture failures are observable with failure reason and correlation identifiers; silent-loss incidents remain at 0.
- **SC-006**: P95 end-user acknowledgment time for valid non-blocking inbound events is 2 seconds or less under baseline load.
- **SC-007**: P99 end-user acknowledgment time for valid non-blocking inbound events is 3 seconds or less under baseline load.
- **SC-008**: 100% of duplicate-delivery tests using identical `(tenant_id, source_channel, provider_message_id)` produce zero additional canonical message/media artifacts and emit `capture.duplicate_suppressed`.
- **SC-009**: 100% of retry-exhausted transient-failure test cases stop after at most 5 attempts and emit `capture.failed_terminal` with correlation and scope identifiers.

## Assumptions

- WhatsApp is the only inbound channel targeted for this foundation increment.
- Upstream channel authentication and signature validation are handled before this feature's core capture processing begins.
- A stable principal context can be derived for each valid inbound event through existing tenancy/membership rules.
- Required audit and telemetry sinks are available in the deployment environment.
- Capture acknowledgment wording may be minimal and standardized; rich conversational responses are deferred to downstream features.
