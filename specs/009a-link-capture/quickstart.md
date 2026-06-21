# Quickstart: Validate Link Capture and Secure Enrichment

## Purpose

Validate end-to-end behavior for SB-009A, including:
- one-time yes/no confirmation semantics,
- 4-second enrichment timeout fallback,
- duplicate canonical-link upsert with provenance/context merge,
- recall with full URL identity and provenance.

## Prerequisites

- .NET SDK 10 installed
- PostgreSQL reachable for local environment
- Runtime configuration populated (tenant-aware data access and channel ingestion)
- Migrations applied including link-capture tables

## Setup

1. Build solution:

```powershell
dotnet build Aluki.Runtime.slnx -v minimal
```

2. Run host runtime:

```powershell
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
```

3. If durable workflow path is exercised, run functions host as needed:

```powershell
dotnet run --project src/Aluki.Runtime.Functions/Aluki.Runtime.Functions.csproj
```

## Validation Scenarios

### Scenario A: Capture single URL with context

1. Send one inbound message containing a valid URL and context text.
2. Verify one `LinkArtifact` exists with canonical URL and tenant/context/principal fields.
3. Verify one provenance reference exists for source message.

Expected outcome:
- capture response outcome is `created`
- recall includes full URL and non-empty description.

### Scenario B: Duplicate delivery idempotent no-op

1. Replay the same inbound event (same source message identity and canonical URL).
2. Verify no new active `LinkArtifact` is created.

Expected outcome:
- capture response outcome is `idempotent_noop`
- audit event indicates replay no-op.

### Scenario C: Canonical-equivalent duplicate upsert merge

1. Send a canonical-equivalent URL variant with new conversational context.
2. Verify active artifact count remains 1 for the tenant/url hash.
3. Verify context/provenance references include both submissions without duplicates.

Expected outcome:
- capture response outcome is `upsert_merged`
- `last_upserted_at_utc` updates.

### Scenario D: One-time confirmation consume (YES)

1. Create active pending confirmation for a link action.
2. Send explicit `yes` once.
3. Repeat `yes` (or `no`) for the same pending item.

Expected outcome:
- first response: `resolved_yes`, `sideEffectsApplied=true`
- later responses: `already_resolved`, `sideEffectsApplied=false`
- no additional side effects or duplicate saves.

### Scenario E: One-time confirmation consume (NO)

1. Create active pending confirmation.
2. Send explicit `no` once.
3. Repeat `no`.

Expected outcome:
- first response: `resolved_no`, `sideEffectsApplied=true`
- later response: `already_resolved`, `sideEffectsApplied=false`.

### Scenario F: Expired confirmation late response

1. Create pending confirmation and wait until expiry.
2. Send delayed `yes` or `no`.

Expected outcome:
- outcome is `expired`
- no save side effects are executed.

### Scenario G: Enrichment policy blocked

1. Capture URL targeting disallowed destination.
2. Verify policy decision recorded before any outbound attempt.

Expected outcome:
- enrichment status `policy_blocked`
- recall still returns full URL, fallback description, and reason code.

### Scenario H: Enrichment timeout fallback (4 seconds)

1. Capture URL with induced slow enrichment target.
2. Verify attempt exits within 4-second cap.

Expected outcome:
- enrichment status `timeout`
- fallback metadata persisted
- recall returns item with timeout reason and provenance.

## Contract References

- API contract: `specs/009-link-capture/contracts/link-capture-contract.yaml`
- Data constraints and transitions: `specs/009-link-capture/data-model.md`

## Acceptance Checklist

- First yes/no consumes pending item atomically; later replies are side-effect free.
- Expired confirmations return explicit expired outcome.
- Enrichment never blocks beyond 4 seconds per link attempt.
- Duplicate canonical URLs do not create more than one active artifact.
- Recall always includes canonical full URL, description, enrichment status/reason, and provenance reference.
