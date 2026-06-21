# Aluki Runtime Constitution

## Core Principles

### I. Skill-First Execution (Non-Negotiable)
All business behavior is implemented as explicit skills with stable contracts.
Agents orchestrate and route; they do not own irreversible business side effects.
Every new capability must declare:
- input and output schemas,
- side effects,
- idempotency strategy,
- telemetry events,
- authorization scope.

### II. Tenant-Scoped Security by Default (Non-Negotiable)
No operation executes without tenant scope and principal context.
No data read or write executes without tenant and context constraints.
Row-level security is mandatory for persisted memory and graph artifacts.

Required checks in every data-touching change:
- principal context resolved,
- tenant and context enforced,
- audit trail emitted.

### III. Grounded Memory and Provenance
The platform must not fabricate recall.
Any recall response must be traceable to persisted evidence with citations.
If evidence is insufficient or ambiguous, the system asks for clarification instead of guessing.

### IV. Durable Session and Workflow Separation
Live conversational state is handled by Orleans runtime.
Long-running, retryable, and wait-heavy processes are handled by Durable Functions.
Changes must preserve this boundary and avoid mixing long process logic inside session handlers.

### V. Cost-Aware and Observable Intelligence
Default model routing prioritizes value models and escalates only when needed.
Every significant skill execution emits telemetry (latency, cost, result, errors).
Operational decisions require observable evidence, not intuition.

## Architecture and Delivery Constraints

1. Runtime baseline
- Orleans for live sessions.
- Durable Functions for long workflows.
- PostgreSQL + pgvector as durable memory substrate.

2. Data discipline
- Messages, memory entities, relations, and embeddings are first-class persisted artifacts.
- Idempotency keys are required for ingestion and retried workflows.

3. Security discipline
- STOP or ALTO consent flows must be enforceable across channels.
- Secrets must be externalized (no hardcoded credentials).

4. Performance and quality targets
- P95 synchronous conversational response target: <= 2 seconds for non-blocking flows.
- Extraction and OCR flows can be async but must provide explicit state transitions.

## Workflow and Quality Gates

The mandatory workflow is:
1. speckit.constitution
2. speckit.specify
3. speckit.clarify
4. speckit.plan
5. speckit.tasks
6. speckit.implement
7. speckit.analyze

Minimum quality gates before merge:
1. Spec traceability: code changes map to accepted spec requirements.
2. Test evidence: unit/integration coverage for touched behavior.
3. Security checks: tenant scope and authorization preserved.
4. Observability checks: logging and telemetry for new critical paths.
5. Documentation updates: architecture or runbook updates when behavior changes.

## Governance

This constitution supersedes ad hoc implementation preferences.
Any exception requires explicit justification in the corresponding plan/task artifacts.

Amendment policy:
1. Propose change with rationale and migration impact.
2. Review against architecture baseline and active specs.
3. Approve and update this file in the same PR.

Compliance policy:
1. Every PR must include a short constitution compliance checklist.
2. Missing compliance evidence blocks merge.

**Version**: 1.0.0 | **Ratified**: 2026-06-21 | **Last Amended**: 2026-06-21
