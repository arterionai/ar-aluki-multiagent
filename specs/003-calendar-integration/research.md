# Research: Calendar Integration

## Decision 1: Secure OAuth connect link and callback lifecycle
- Decision: Use short-lived, single-use callback state records bound to tenant, context, user, provider, and anti-forgery nonce; reject expired/replayed/mismatched callbacks without mutating connection state.
- Rationale: Implements FR-002a and SC-009 and aligns with constitution security requirements.
- Alternatives considered:
  - Stateless callback validation only: rejected due to replay risk.
  - Long-lived callback state (>15 minutes): rejected due to expanded attack window.

## Decision 2: Token handling boundaries and redaction policy
- Decision: Confine access/refresh tokens to authorization components, store encrypted-at-rest secrets, and enforce structured redaction in user responses, telemetry payloads, and audit-readable fields.
- Rationale: Required by FR-008a and SC-010.
- Alternatives considered:
  - Return token-derived debug details in user errors: rejected as a data leakage risk.
  - Store plaintext tokens in app tables: rejected due to compliance and breach impact.

## Decision 3: Deterministic provider selection strategy
- Decision: Provider selection order is deterministic by explicit user/provider mention, then persisted default provider, then stable lexical tie-breaker when multiple active providers exist.
- Rationale: Satisfies FR-006 and SC-004 while keeping behavior explainable.
- Alternatives considered:
  - Random provider selection among connected providers: rejected because outcomes are non-repeatable.
  - Always favor one provider globally: rejected because it ignores explicit user intent.

## Decision 4: Timezone canonicalization and DST ambiguity handling
- Decision: Normalize requests to a single canonical timezone before create side effects using request timezone if explicit, otherwise user profile timezone; if still ambiguous (including DST overlap/nonexistent local times), require clarification.
- Rationale: Implements FR-004/FR-004a and SC-011.
- Alternatives considered:
  - Provider-local default timezone fallback: rejected because behavior differs across providers.
  - Silent disambiguation of DST overlap: rejected because it can schedule incorrect times.

## Decision 5: Create confirmation semantics
- Decision: Emit `created` only after provider acknowledgment containing provider event reference; duplicate-safe in-window retries return `previously_created` with the same outcome reference.
- Rationale: Implements FR-007/FR-007a/FR-009a and SC-013.
- Alternatives considered:
  - Optimistic `created` before provider acknowledgment: rejected due to false-positive confirmations.
  - Return generic success without outcome type: rejected due to ambiguity for retries.

## Decision 6: Idempotency and deduplication window
- Decision: Build idempotency keys from normalized create intent (tenant/context/user/provider + canonical title/start/end/timezone) and enforce a 10-minute deduplication window.
- Rationale: Required by FR-009/FR-009a and SC-005/SC-012.
- Alternatives considered:
  - Message-ID-only dedupe: rejected because semantically same retries can have different message IDs.
  - Infinite dedupe window: rejected because legitimate recurring requests could be blocked.

## Decision 7: Retry and workflow boundary
- Decision: Keep normal create path synchronous and non-blocking; hand off long retry/recovery sequences (e.g., transient provider outage) to Durable Functions with explicit progress outcomes.
- Rationale: Aligns with constitution IV and FR-012/SC-007.
- Alternatives considered:
  - Fully synchronous retries until success/failure: rejected for latency risk.
  - Fully asynchronous create for all requests: rejected for poor conversational UX.

## Decision 8: Cross-provider parity controls
- Decision: Use shared validation/clarification pipeline for required fields and timezone handling, with provider adapters only at authorization and final event-creation boundaries.
- Rationale: Ensures FR-010 and SC-008 parity targets.
- Alternatives considered:
  - Provider-specific validation logic per path: rejected due to drift and inconsistent outcomes.
  - Normalize only response format, not validation: rejected because parity must include behavior.

## Decision 9: Auditable outcome taxonomy
- Decision: Emit explicit audit outcomes for connect, disconnect, create-success, create-denied, and authorization-failure with correlation IDs and scope identifiers.
- Rationale: Implements FR-011 and SC-006 with constitution observability requirements.
- Alternatives considered:
  - Log only failures: rejected because successful side effects also require compliance traceability.
  - Event-only telemetry without durable audit record: rejected for insufficient evidence quality.

## Best-practice alignment notes
- Constitution principle I and architecture baseline skill taxonomy: calendar behavior is implemented as explicit skills (`CalendarCreateSkill`, `CalendarProviderSelectionSkill`, `IdempotencyGuardSkill`, `AuditLogSkill`).
- Constitution principle II: all connection and create paths remain tenant/context/principal-scoped before side effects.
- Architecture baseline durable separation: long-running retries are delegated to Durable workflows, preserving Orleans conversational responsiveness.
- Security discipline: secrets remain externalized and no raw token material is included in user-visible channels.
