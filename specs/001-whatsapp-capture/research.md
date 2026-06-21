# Research: WhatsApp Capture Foundation

## Decision 1: Keep runtime bootstrap and add webhook ingress in host
- Decision: Extend `Aluki.Runtime.Host` from heartbeat worker bootstrap into webhook-capable runtime entrypoint while preserving existing abstractions contracts.
- Rationale: The repository already has contracts (`ISkill`, `SkillExecutionContext`, `PrincipalContext`, `SkillDispatcher`) and a host suitable for incremental ingress hardening.
- Alternatives considered:
  - Build a separate ingress service now: rejected to avoid early topology complexity before baseline behavior is validated.
  - Implement capture directly in agent logic: rejected due to constitution skill-first rule and side-effect governance requirements.

## Decision 2: Use canonical idempotency key at DB boundary
- Decision: Enforce uniqueness on `(tenant_id, source_channel, provider_message_id)` with duplicate-safe acknowledgment behavior.
- Rationale: This exactly matches clarified FR-013 and enables deterministic suppression under retries/redeliveries.
- Alternatives considered:
  - Hash full payload as idempotency key: rejected because provider retries may alter non-canonical envelope fields.
  - In-memory dedupe cache only: rejected because it is not durable across restarts/concurrency.

## Decision 3: PrincipalContext resolution as pre-side-effect gate
- Decision: Derive tenant from channel membership and context from explicit metadata or default personal context before writes.
- Rationale: Aligns with constitution tenant-first security and FR-005/FR-006/FR-014.
- Alternatives considered:
  - Best-effort fallback to global context: rejected due to cross-tenant leakage risk.
  - Post-write validation: rejected because side effects must never happen prior to authorization.

## Decision 4: Bounded retry with terminal audited failure
- Decision: Retry transient persistence up to 5 attempts with bounded exponential backoff; emit `capture.failed_terminal` on exhaustion.
- Rationale: Meets FR-017 and SC-009 while preventing silent loss.
- Alternatives considered:
  - Unlimited retries: rejected due to queue starvation and non-deterministic operational behavior.
  - No retries in capture path: rejected because transient failures would reduce SC-001 success rate.

## Decision 5: Accepted-but-unsupported fallback for unknown payloads
- Decision: Persist a minimal unsupported capture artifact with scope, provenance, and raw envelope reference.
- Rationale: Preserves continuity and auditability per FR-010/FR-015.
- Alternatives considered:
  - Hard reject unsupported payloads: rejected due to continuity loss and operational blind spots.
  - Drop unsupported payloads silently: rejected due to compliance and traceability violations.

## Decision 6: Mandatory telemetry and audit event set
- Decision: Emit mandatory lifecycle events and critical-stage telemetry fields (latency/result/failure category) with correlation IDs.
- Rationale: Required for FR-012/FR-016 and measurable SC tracking.
- Alternatives considered:
  - Rely on generic host logs only: rejected as insufficient for acceptance evidence and SLO reporting.

## Best-Practice Notes Applied
- Architecture layering follows `docs/ARCHITECTURE_BASELINE.md` with skill execution and durable workflow separation.
- Delivery order respects `docs/IMPLEMENTATION_ORDER.md`: security/data core before ingress hardening and skill expansion.
- RLS and tenant scope are treated as release-blocking controls, not optional validation.
