# Research: Domain Agents Runtime

**Date**: 2026-06-21 | **Feature**: SB-009B Domain Agents Runtime

## Resolved Clarifications

### C1: Routing Precedence

**Decision**: Enforce fixed routing precedence for every dispatch cycle:
1. Policy gating
2. Eligibility evaluation
3. Deterministic selection
4. Fallback only if zero eligible agents

**Rationale**: This sequence guarantees security and consent enforcement before any domain execution and provides deterministic, replayable routing behavior.

**Alternatives considered**:
- Eligibility before policy: rejected because unauthorized agents could be evaluated before consent/tenant checks.
- Fallback as early shortcut: rejected because it hides true eligibility outcomes and weakens audit integrity.

---

### C2: Tie-Break Strategy

**Decision**: Resolve equal-priority eligibility ties by:
1. Canonical agent identifier ascending lexical order
2. Registration timestamp ascending order when identifiers are equivalent

**Rationale**: Provides deterministic total ordering without random or runtime-dependent behavior.

**Alternatives considered**:
- Random selection: rejected due to non-determinism and test flakiness.
- Last-registered-wins: rejected due to operational instability during rollout/restart.

---

### C3: Fallback Policy

**Decision**: Invoke `MemoryAgent` fallback only when no agents are eligible after policy gating and eligibility evaluation.

**Rationale**: Preserves primary routing semantics while retaining capture behavior for unclaimed intents.

**Alternatives considered**:
- Fallback on selected-agent failure: rejected because it masks primary-route failures and violates clarified requirement FR-015.
- Fallback before deterministic selection: rejected because it bypasses eligible domain agents.

---

### C4: Failure Containment

**Decision**: Selected-agent failures are contained to the current dispatch cycle and converted to explicit containment outcomes; runtime continues processing subsequent messages.

**Rationale**: Prevents localized faults from becoming global availability incidents and supports recovery/triage from auditable evidence.

**Alternatives considered**:
- Global circuit break on single agent failure: rejected due to excessive blast radius.
- Silent retries without audit: rejected due to observability and compliance gaps.

---

### C5: Immutable Audit Obligations

**Decision**: Persist immutable dispatch evidence per cycle including:
- message identity and normalization fingerprint,
- tenant/principal/consent inputs,
- evaluated agents and eligibility outcomes,
- selected agent or explicit fallback reason,
- tie-break rationale when applicable,
- containment result for failures,
- correlation ID and timestamp.

**Rationale**: Enables deterministic replay validation, incident response, and compliance traceability.

**Alternatives considered**:
- Mutable aggregate-only logs: rejected because they cannot prove exact cycle decisions.
- Partial audits (success-only): rejected due to missing denial/failure lineage.

## Technology and Pattern Decisions

### Dispatch Determinism Pattern

**Decision**: Normalize dispatch input into a stable routing key and execute pure deterministic evaluation steps with explicit sort keys.

**Rationale**: Ensures identical replay outcomes for identical inputs (SC-001, SC-006).

### Audit Persistence Pattern

**Decision**: Store cycle-level immutable events in PostgreSQL append-only table with tenant-scoped RLS and correlation indices.

**Rationale**: Aligns with existing persistence architecture and constitution security constraints.

### Containment Pattern

**Decision**: Wrap selected-agent execution in cycle-scoped error boundary; emit containment event and terminate cycle without fallback substitution.

**Rationale**: Satisfies FR-010, FR-015, FR-016 and keeps runtime available.

## Phase 0 Completion

All identified unknowns are resolved. No remaining `NEEDS CLARIFICATION` items block Phase 1 design artifacts.
