# Quickstart: Validate Personal Memory and Grounded Recall

This guide validates the feature behavior end-to-end against the specification and contract artifacts.

## Prerequisites

- .NET 10 SDK installed.
- PostgreSQL reachable with migrations applied:
  - `db/migrations/001_init_tenancy.sql`
  - `db/migrations/002_init_artifacts.sql`
  - `db/migrations/003_enable_rls.sql`
- Runtime host configuration available (local values or Key Vault-backed):
  - `PostgresConnectionString`
- Repo builds successfully:
  - `dotnet restore Aluki.Runtime.slnx`
  - `dotnet build Aluki.Runtime.slnx -v minimal`

## Validation Contract

- Interaction API contract: `specs/002-personal-memory/contracts/personal-memory-contract.yaml`
- Data entities and rules: `specs/002-personal-memory/data-model.md`

## Run the host

1. Start runtime host:

```powershell
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
```

2. Confirm host starts without scope/migration errors.

## Scenario A: Note capture canonical persistence (FR-001/002/004)

1. Send `note-to-store` style interaction payload to `/api/memory/personal/interactions`.
2. Verify response:
   - `intent = note_to_store`
   - `status = accepted`
   - Includes `idempotency_key` and `canonical_chain_id`.
3. Resend same payload with same `source_identity`.
4. Verify response:
   - `status = duplicate_suppressed`
   - No additional canonical artifact created.

Expected outcome:
- Exactly one canonical artifact exists per `(tenant_id, source_channel, source_identity)`.

## Scenario B: Grounded confirmed recall (FR-005/006)

1. Store at least two corroborating artifacts for the same factual claim.
2. Submit recall query interaction.
3. Verify response:
   - `intent = recall_query`
   - `status = grounded_result`
   - Claims marked `confirmed` include at least two citations each.

Expected outcome:
- No confirmed claim appears with fewer than two corroborating citations.

## Scenario C: Single-artifact low-confidence recall (FR-008)

1. Ensure only one relevant artifact supports the queried claim.
2. Submit recall query.
3. Verify response:
   - `status = low_confidence`
   - Includes `clarification_question`
   - Claim is not presented as confirmed.

Expected outcome:
- System requests clarification instead of over-asserting.

## Scenario D: No-result and deletion gap behavior (FR-007/009)

1. Query topic with no relevant evidence.
2. Verify `status = no_result` with reason `no_evidence`.
3. Mark previously relevant artifact as deleted and query again.
4. Verify:
   - Deleted artifact is not cited.
   - `no_result_reason = deleted_evidence_gap` when applicable.

Expected outcome:
- No fabrication and explicit gap signaling.

## Scenario E: Scope denial and audit evidence (FR-003/014)

1. Submit request with mismatched principal/tenant/context.
2. Verify:
   - HTTP 403 with `scope_denied`.
   - Auditable denial record contains correlation + scope identifiers.

Expected outcome:
- No fallback to broader scope; denial is traceable.

## Scenario F: Topic grouping and continuity (FR-010/011)

1. Capture related notes through two already connected channels in same tenant/context.
2. Query topic-oriented recall.
3. Verify response:
   - Includes grouped `topic_groups`.
   - Includes citations from in-scope artifacts across channels.

Expected outcome:
- Coherent grouped output preserving continuity across connected channels.

## Operational checks (FR-013)

For each scenario, verify telemetry dimensions are present for memory skills:
- `request`
- `scope`
- `latency_ms`
- `result`
- `cost_estimate`
- `retry_count`

Expected outcome:
- Required observability dimensions emitted per significant execution.
