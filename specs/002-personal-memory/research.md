# Research: Personal Memory and Grounded Recall

## Decision 1: Intent split at ingress (`note-to-store` vs `recall-query`)
- Decision: Classify each inbound memory interaction into `note-to-store` or `recall-query` before selecting downstream skill chain.
- Rationale: Directly satisfies FR-001 and keeps Skill-First orchestration explicit per constitution principle I.
- Alternatives considered:
  - Single unified handler with late branching: rejected because it obscures side effects and complicates auditability.
  - Rule-only post-processing classification: rejected because persistence/read paths could execute before intent certainty.

## Decision 2: Canonical memory artifact chain by source identity
- Decision: Use source identity idempotency `(tenant_id, source_channel, source_identity)` and extend one canonical artifact chain for updates.
- Rationale: Satisfies FR-004 and prevents duplicate canonicals under retries or redelivery.
- Alternatives considered:
  - Append-only new canonical per update: rejected because it violates canonical chain requirement and increases ambiguity.
  - Hash-only dedupe on content: rejected because semantically equivalent edits and transport retries are not reliably represented.

## Decision 3: Corroboration policy for confirmed factual claims
- Decision: A claim is `confirmed` only with at least two corroborating, in-scope, non-deleted artifacts; one artifact yields `low-confidence` plus clarification question.
- Rationale: Implements clarification policy and FR-005/FR-006/FR-008 with explicit confidence semantics.
- Alternatives considered:
  - Single-artifact confirmation: rejected by spec clarification and grounded-memory principle.
  - Model confidence scoring without artifact threshold: rejected as non-deterministic for compliance.

## Decision 4: Strict scope gate with deny-without-fallback
- Decision: Enforce principal, tenant, and context checks before every read/write operation; deny out-of-scope without broad fallback.
- Rationale: Required by FR-003 and constitution principle II.
- Alternatives considered:
  - Fallback to tenant-wide retrieval on context mismatch: rejected due to boundary violation risk.
  - Deferred scope checks in repository layer only: rejected because side effects could already occur.

## Decision 5: Deletion-aware retrieval semantics
- Decision: Exclude deleted artifacts from retrieval/citation and explicitly signal deletion-caused evidence gaps.
- Rationale: Implements FR-009 and SC-004 while preserving provenance transparency.
- Alternatives considered:
  - Soft-include deleted in hidden confidence scoring: rejected because it contaminates user-visible grounding.
  - Hard no-result without explanation: rejected because spec requires explicit gap signaling.

## Decision 6: Topic grouping and cross-channel continuity
- Decision: Retrieve by tenant/context memory graph and return grouped topic clusters with claim-level citations; include artifacts from already connected channels in same scope.
- Rationale: Satisfies FR-010 and FR-011 while aligned to architecture baseline memory/graph layer.
- Alternatives considered:
  - Per-channel silo retrieval: rejected because it breaks continuity requirement.
  - Flat ungrouped result list: rejected because topic coherence is a success criterion.

## Decision 7: Sync vs async boundary
- Decision: Keep recall response path non-blocking for standard retrieval/synthesis; offload long-running rebuild/synthesis to durable workflow path with retry-safe resume.
- Rationale: Meets FR-012 and SC-006; aligns with constitution principle IV and architecture baseline separation.
- Alternatives considered:
  - Execute all synthesis synchronously: rejected due to latency risk.
  - Execute all retrieval asynchronously: rejected due to poor conversational UX for normal queries.

## Decision 8: Telemetry and audit event schema for memory skills
- Decision: Emit skill-level telemetry dimensions (`request`, `scope`, `latency_ms`, `result`, `cost_estimate`, `retry_count`) and auditable side-effect/denial records with correlation IDs.
- Rationale: Implements FR-013/FR-014 and constitution principle V.
- Alternatives considered:
  - Aggregate-only endpoint metrics: rejected because insufficient for compliance and root-cause analysis.
  - Audit only on failures: rejected because successful side effects also require traceability.

## Best-practice notes applied from project baselines
- Architecture baseline: Skill-first execution, grounded recall with provenance, and durable-memory substrate in PostgreSQL + pgvector.
- Constitution: non-negotiable tenant scope, no fabrication, explicit contracts, and observability.
- Implementation order: prioritize Security and Data core before full feature behavior; leverage existing migrations (`001..003`) and scope middleware patterns before adding recall skills.
- Bootstrap constraints: build on existing projects `Aluki.Runtime.Abstractions` and `Aluki.Runtime.Host`; avoid introducing unrelated service boundaries in this slice.
