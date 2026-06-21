# Implementation Plan: Personal Memory and Grounded Recall

**Branch**: `002-personal-memory` | **Date**: 2026-06-21 | **Spec**: `specs/002-personal-memory/spec.md`

**Input**: Feature specification from `specs/002-personal-memory/spec.md`

## Summary

Implement personal-memory capture and grounded recall with strict tenant/context boundaries, canonical idempotent memory chains, corroboration-based factual confirmation, deletion-aware evidence handling, topic-grouped responses, and full audit/telemetry coverage. The design follows the architecture baseline and constitution by keeping skills as atomic execution units, preserving Orleans vs Durable separation, and building on the existing runtime bootstrap (`Aluki.Runtime.Abstractions` + `Aluki.Runtime.Host`) without introducing unrelated scope.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**: Existing host abstractions (`ISkill`, `SkillExecutionContext`, `SkillDispatcher`, `PrincipalContext`) plus planned implementation additions: ASP.NET Core minimal APIs (interaction ingress), `Npgsql` for PostgreSQL access, OpenTelemetry/App Insights exporters, and xUnit test tooling.

**Storage**: PostgreSQL with tenancy/artifact/RLS migrations already present (`db/migrations/001..003`), including durable memory artifacts, citation links, deletion markers, and audit events.

**Testing**: Unit tests for intent split, corroboration policy, deletion filtering, and scope guards; integration tests for RLS and idempotent canonical chains; contract tests against `contracts/personal-memory-contract.yaml`; latency and retry-path validation for sync vs async boundaries.

**Target Platform**: Linux-hosted runtime service (container-ready) with Windows local development support.

**Project Type**: Backend runtime service capability slice (memory skills + ingress contract + persistence/audit behavior).

**Performance Goals**:
- Synchronous grounded recall P95 <= 2s for non-blocking paths (SC-006).
- Valid note capture persistence success >= 99.0% within 60s (SC-001).
- Confirmed claims must always carry >= 2 corroborating citations (SC-002).

**Constraints**:
- No read/write side effect without resolved principal/tenant/context scope (FR-003).
- Confirmed factual claims require >= 2 corroborating non-deleted artifacts (FR-005/006).
- One-artifact evidence requires low-confidence plus clarification question (FR-008).
- Deleted artifacts must never appear in recall/citations and must surface gap signaling when relevant (FR-009).
- Long-running synthesis must not block synchronous conversational path (FR-012).

**Scale/Scope**:
- Feature slice only for personal memory capture/recall and topic grouping.
- Reuses already connected channels; no new channel adapters.
- Excludes autonomous action execution (reminders/calendar/tasks).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Skill-First Execution (I)**: PASS. Plan models behavior through explicit skills for classify, capture, retrieve, corroborate, cite, and audit.
- **Tenant-Scoped Security by Default (II)**: PASS. Scope gate precedes every memory read/write; denies without fallback.
- **Grounded Memory and Provenance (III)**: PASS. Claims tied to persisted artifacts with mandatory citations and no-fabrication fallback.
- **Durable Session and Workflow Separation (IV)**: PASS. Non-blocking sync recall path with durable async handoff for long synthesis.
- **Cost-Aware and Observable Intelligence (V)**: PASS. Telemetry dimensions and audit events are mandatory outputs of significant skill executions.

No constitution exceptions required.

## Implementation Phases

### Phase 0 - Research and Decision Consolidation

- Consolidate decisions in `research.md` for intent classification, corroboration thresholding, deletion semantics, scope policy, sync/async boundary, and telemetry schema.
- Resolve all technical unknowns before implementation task generation.

Output:
- `specs/002-personal-memory/research.md`

### Phase 1 - Design and Contracts

- Extract and formalize entities and lifecycle rules for artifacts, citations, topics, deletion markers, and audit records.
- Define external interaction contract for memory processing endpoint, including note capture and recall outcomes.
- Author quickstart validation guide for end-to-end acceptance evidence.
- Update agent context reference to this feature plan.

Output:
- `specs/002-personal-memory/data-model.md`
- `specs/002-personal-memory/contracts/personal-memory-contract.yaml`
- `specs/002-personal-memory/quickstart.md`
- `.github/copilot-instructions.md` (plan pointer)

### Phase 2 - Runtime Foundation Alignment (Implementation-order aware)

- Reuse Implementation Order Phases 1-2 foundations:
  - Security/data core already established via migrations and RLS prerequisites.
  - Runtime contracts already bootstrapped (`ISkill`, `SkillExecutionContext`, `PrincipalContext`, `SkillDispatcher`).
- Introduce memory capability skills in abstraction/host layers:
  - `MemoryIntentClassifierSkill`
  - `MemoryCaptureSkill`
  - `MemoryRecallSkill`
  - `CorroborationPolicySkill`
  - `CitationRenderSkill` (memory-focused behavior)
  - `TopicGroupingSkill`
  - `MemoryAuditSkill`

### Phase 3 - Capture Path (Note-to-store)

- Implement note capture flow with pre-side-effect scope validation.
- Persist canonical memory artifact chain by source identity with idempotency guards.
- Ensure updates extend existing chain version, preserving provenance.
- Emit audit and telemetry for accepted, duplicate-suppressed, and denied outcomes.

### Phase 4 - Recall Path (Grounded retrieval)

- Implement scoped retrieval over non-deleted memory artifacts.
- Enforce claim confirmation rule (>= 2 corroborating artifacts).
- Return low-confidence + clarification question when only one artifact supports claim.
- Return explicit no-result on absent evidence.
- Signal deletion-caused evidence gap when relevant.
- Render topic-grouped output with claim-level citations.

### Phase 5 - Workflow Boundary and Reliability

- Keep standard recall path synchronous and non-blocking.
- Delegate long-running synthesis/rebuild operations to durable workflow execution.
- Enforce retry-safe semantics and observable retry counters for async branches.

### Phase 6 - Validation and Evidence

- Execute contract, unit, and integration suites mapped to FR/SC requirements.
- Verify scope-denial audit completeness and telemetry dimensions.
- Verify SC metrics for latency, corroboration compliance, duplicate suppression, and low-confidence handling.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Ambiguous intent classification for short/elliptical messages | Wrong capture vs recall behavior | Add deterministic classifier policy and ambiguity fallback clarification path |
| False confirmation from weak evidence | Trust/compliance regression | Hard gate requiring two corroborating artifacts for confirmed claims |
| Scope leakage under cross-channel continuity | Security boundary violation | Enforce principal/tenant/context filters before retrieval and citation selection |
| Duplicate canonical artifacts on retried events | Data quality and recall inconsistency | DB unique idempotency key and canonical chain updates only |
| Deleted evidence still influencing results | Non-compliant recall output | Exclude deleted artifacts in retrieval/citation pipeline and test explicitly |
| Latency spikes from heavy synthesis | Miss SC-006 | Keep non-blocking synchronous path and offload expensive synthesis to durable async workflow |

## Dependencies

1. Existing security/data foundation from `db/migrations/001_init_tenancy.sql`, `002_init_artifacts.sql`, `003_enable_rls.sql`.
2. Principal context resolution contracts in `src/Aluki.Runtime.Abstractions/Security/PrincipalContext.cs`.
3. Skill orchestration contracts in `src/Aluki.Runtime.Abstractions/Skills/ISkill.cs` and `src/Aluki.Runtime.Abstractions/Orchestration/SkillDispatcher.cs`.
4. Runtime host bootstrap and configuration in `src/Aluki.Runtime.Host`.
5. Observability and audit pipeline integration points (OpenTelemetry/App Insights and audit store).

## Acceptance and Success-Criteria Coverage

| Requirement/Outcome | Plan Coverage | Validation Evidence |
|---|---|---|
| FR-001, FR-002 | Phases 2-3 (intent split + canonical persistence) | Contract + integration tests for note capture outcomes |
| FR-003, FR-014, SC-005 | Phases 2-3 and 6 (strict scope enforcement + auditable denials) | Scope mismatch tests and audit record assertions |
| FR-004, SC-008 | Phase 3 (idempotent source identity + canonical chain updates) | Duplicate ingestion tests with zero extra canonicals |
| FR-005, FR-006, SC-002 | Phase 4 (corroboration policy + citation rendering) | Claim-level citation assertions requiring >=2 artifacts |
| FR-007, SC-003 | Phase 4 (explicit no-result) | No-evidence scenario tests with no fabricated facts |
| FR-008, SC-009 | Phase 4 (single-artifact low-confidence + clarification) | Single-artifact scenario tests |
| FR-009, SC-004 | Phase 4 (deletion exclusion + gap signaling) | Deleted artifact recall tests |
| FR-010, FR-011, SC-007 | Phase 4 (topic grouping + cross-channel continuity) | Multi-channel grouped recall acceptance tests |
| FR-012, SC-006 | Phases 5-6 (sync/async boundary and latency validation) | Load baseline report and bounded async behavior checks |
| FR-013 | Phases 3-6 (telemetry dimensions) | Telemetry assertions by skill execution |

## Project Structure

### Documentation (this feature)

```text
specs/002-personal-memory/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── personal-memory-contract.yaml
└── tasks.md                # generated later by /speckit.tasks
```

### Source Code (repository root)

```text
db/
└── migrations/

src/
├── Aluki.Runtime.Abstractions/
│   ├── Orchestration/
│   ├── Security/
│   └── Skills/
└── Aluki.Runtime.Host/
   ├── Program.cs
   ├── Worker.cs
   └── appsettings*.json

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: Preserve the existing two-project bootstrap and extend via memory skills and host-level endpoints; add a dedicated `tests/` hierarchy for evidence-driven validation without introducing new service boundaries.

## Post-Design Constitution Re-Check

- Skill-first boundaries remain explicit and side effects are declared: PASS
- Tenant/context scope is enforced before memory reads/writes: PASS
- Grounded recall requires corroborated evidence and citations: PASS
- Session/live handling stays separated from long-running workflows: PASS
- Telemetry and audit observability requirements are fully represented: PASS

## Complexity Tracking

No constitution violations or waivers.
