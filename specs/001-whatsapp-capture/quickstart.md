# Quickstart: Validate WhatsApp Capture Foundation

This guide defines end-to-end validation steps for the WhatsApp capture foundation implementation.

## Prerequisites

- .NET 10 SDK installed
- PostgreSQL reachable with RLS-capable schema
- Runtime configuration available (local or Key Vault-backed)
- Test tenant(s), users, and context fixtures prepared
- WhatsApp webhook simulation payloads for text/image/audio/forwarded/unsupported

## Setup

1. Apply baseline DB migrations in order:
   - `db/migrations/001_init_tenancy.sql`
   - `db/migrations/002_init_artifacts.sql`
   - `db/migrations/003_enable_rls.sql`
2. Build runtime:
   - `dotnet build Aluki.Runtime.slnx`
3. Start host:
   - `dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj`

## Validation Scenarios

### 1. Supported inbound capture path
- Send valid text/image/audio/forwarded payloads to webhook contract endpoint.
- Verify one canonical message artifact per unique provider message ID.
- For image/audio, verify linked media artifact is created.
- Confirm acknowledgment status is `accepted`.

Expected outcomes:
- FR-001, FR-002, FR-003 satisfied.
- SC-003 satisfied for required scope/provenance fields.

### 2. Duplicate delivery suppression
- Replay identical payload with same `(tenant_id, source_channel, provider_message_id)`.
- Verify no new canonical message/media artifacts are created.
- Confirm acknowledgment status is `duplicate_suppressed`.

Expected outcomes:
- FR-004, FR-013 satisfied.
- SC-002 and SC-008 satisfied.

### 3. Scope denial behavior
- Submit payload missing valid tenant/context or with unauthorized context.
- Verify operation is denied before side effects.
- Verify `capture.scope_denied` audit event exists with correlation/scope identifiers.

Expected outcomes:
- FR-005, FR-006, FR-014 satisfied.
- SC-004 satisfied.

### 4. Unsupported payload continuity
- Submit payload shape marked unsupported.
- Verify minimal unsupported artifact persisted with raw envelope reference.
- Confirm status `accepted_unsupported` and event `capture.unsupported_payload`.

Expected outcomes:
- FR-010, FR-015 satisfied.

### 5. Retry and terminal failure path
- Inject transient persistence failure and verify retries up to success or max 5 attempts.
- Inject permanent failure and verify `capture.failed_terminal` after retry budget exhaustion.

Expected outcomes:
- FR-009, FR-017 satisfied.
- SC-005 and SC-009 satisfied.

### 6. Latency SLO checks
- Run baseline load test for valid non-blocking inbound events.
- Collect telemetry for ack latency and outcome status.

Expected outcomes:
- SC-006 (P95 <= 2s) and SC-007 (P99 <= 3s) satisfied.
- SC-001 capture success target validated.

## Test Commands (expected task phase)

- Unit tests: `dotnet test --filter Category=Unit`
- Integration tests: `dotnet test --filter Category=Integration`
- Contract tests: `dotnet test --filter Category=Contract`

## Evidence to Attach to Review

- Test run summary mapped to FR-001..FR-017
- Metric report for SC-001..SC-009
- Audit event samples for mandatory lifecycle events
- Duplicate suppression and scope denial query evidence

## Implementation Verification Notes

The capture foundation is implemented in `src/Aluki.Runtime.Host` (webhook ingress
+ skill pipeline) over the abstractions in `src/Aluki.Runtime.Abstractions`.

### Build

```bash
dotnet restore Aluki.Runtime.slnx
dotnet build Aluki.Runtime.slnx
```

### Run the host (webhook on /api/channels/whatsapp/inbound)

```bash
dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj
# Health probe:
curl -s http://localhost:5000/health
```

### Tests

```bash
# Unit + contract (no database required)
dotnet test --filter Category=Unit
dotnet test --filter Category=Contract

# Integration: coordinator reliability/SLA run without a DB; the DB-backed
# persistence/isolation tests activate when a database is provided.
export ALUKI_TEST_POSTGRES="Host=localhost;Database=aluki_test;Username=postgres;Password=postgres"
dotnet test --filter Category=Integration
```

The integration fixture auto-applies migrations `001`–`005` to the target
database (the `vector` extension from migration 002 must be installed).

### Scenario-to-test mapping

| Quickstart scenario | Automated coverage |
|---------------------|--------------------|
| 1 Supported capture path | `WhatsAppCapturePersistenceTests`, `NormalizeWhatsAppInboundSkillTests` |
| 2 Duplicate suppression | `WhatsAppCapturePersistenceTests` |
| 3 Scope denial | `CaptureScopeIsolationTests` |
| 4 Unsupported continuity | `NormalizeWhatsAppInboundSkillTests` (+ persistence path) |
| 5 Retry / terminal failure | `CaptureRetryReliabilityTests` |
| 6 Latency SLO | `CaptureSlaTelemetryTests` + load baseline |

See `checklists/implementation-evidence.md` for the full FR/SC traceability and
`docs/CAPTURE_OPERATIONS.md` for the operational runbook.

## References

- Spec: `specs/001-whatsapp-capture/spec.md`
- Plan: `specs/001-whatsapp-capture/plan.md`
- Data model: `specs/001-whatsapp-capture/data-model.md`
- Contract: `specs/001-whatsapp-capture/contracts/whatsapp-inbound-contract.yaml`
