# Implementation Evidence Checklist: WhatsApp Capture Foundation

**Purpose**: Map every functional requirement (FR) and success criterion (SC) to
implementation and test/evidence artifacts.

**Status legend**: ✅ implemented + covered · 🧪 covered by DB-gated integration
test (requires `ALUKI_TEST_POSTGRES`) · 📈 validated via load/telemetry evidence.

## Functional Requirements

| Req | Status | Implementation | Evidence |
|-----|--------|----------------|----------|
| FR-001 accept + normalize text/image/audio/forwarded | ✅ | `NormalizeWhatsAppInboundSkill`, `WhatsAppInboundEndpoint` | `NormalizeWhatsAppInboundSkillTests`, `WhatsAppInboundContractTests` |
| FR-002 one canonical record + provenance | ✅ | `PersistCaptureSkill`, migration 004 | `WhatsAppCapturePersistenceTests` 🧪 |
| FR-003 required identity/scope fields | ✅ | `PersistCaptureSkill`, `unified_message_artifact` schema | `WhatsAppCapturePersistenceTests` 🧪 |
| FR-004 idempotent processing | ✅ | `IdempotencyGuardSkill`, `IdempotencyRepository`, unique key | `WhatsAppCapturePersistenceTests` 🧪, `CaptureRetryReliabilityTests` |
| FR-005 scope check before side effect | ✅ | `PrincipalContextResolver`, `ScopeGuardSkill`, RLS 005 | `CaptureScopeIsolationTests` 🧪 |
| FR-006 reject + audit missing scope | ✅ | coordinator scope path, `WriteScopeDeniedAuditSkill` | `CaptureScopeIsolationTests` 🧪 |
| FR-007 ack / controlled failure outcome | ✅ | `CaptureFailureMapper`, endpoint | `CaptureFailureMapperTests`, contract tests |
| FR-008 audit each side effect/denial/failure | ✅ | `WriteCaptureAuditSkill`, `WriteScopeDeniedAuditSkill`, `WriteRetryAuditSkill` | reliability + isolation tests |
| FR-009 retry-safe transient handling | ✅ | `CaptureRetryPolicy`, coordinator retry loop | `CaptureRetryReliabilityTests` |
| FR-010 unsupported classification | ✅ | `NormalizeWhatsAppInboundSkill`, `PersistUnsupportedCaptureSkill` | `NormalizeWhatsAppInboundSkillTests` |
| FR-011 consent-stop enforcement | ✅ | `ConsentStopPolicyService`, `ScopeGuardSkill` | coordinator consent path |
| FR-012 critical-stage telemetry | ✅ | `CaptureTelemetry`, `CaptureObservability` | `CaptureSlaTelemetryTests` 📈 |
| FR-013 canonical idempotency key + duplicate-safe ack | ✅ | unique `(tenant_id, source_channel, provider_message_id)` | `WhatsAppCapturePersistenceTests` 🧪 |
| FR-014 derive + validate PrincipalContext | ✅ | `PrincipalContextResolver` | `PrincipalContextResolverTests`, `CaptureScopeIsolationTests` 🧪 |
| FR-015 unsupported fallback artifact | ✅ | `PersistUnsupportedCaptureSkill` | `NormalizeWhatsAppInboundSkillTests` |
| FR-016 mandatory audit event set | ✅ | `CaptureAuditEvent`, audit skills, migration 004 check constraint | reliability + isolation tests |
| FR-017 bounded retry (max 5) + terminal audit | ✅ | `CaptureRetryPolicy`, `WriteRetryAuditSkill` | `CaptureRetryReliabilityTests` |

## Success Criteria

| Crit | Status | Evidence |
|------|--------|----------|
| SC-001 ≥99.5% single canonical capture | 🧪📈 | persistence tests + load baseline (quickstart) |
| SC-002 0% duplicate artifacts | ✅🧪 | `WhatsAppCapturePersistenceTests` |
| SC-003 100% required scope/provenance fields | ✅🧪 | schema NOT NULL + persistence tests |
| SC-004 100% denial audited | ✅🧪 | `CaptureScopeIsolationTests` |
| SC-005 100% terminal failures observable | ✅ | `CaptureRetryReliabilityTests`, telemetry |
| SC-006 P95 ack ≤ 2s | 📈 | `CaptureSlaTelemetryTests` + load baseline |
| SC-007 P99 ack ≤ 3s | 📈 | load baseline (quickstart) |
| SC-008 duplicate key → 0 new artifacts + event | ✅🧪 | `WhatsAppCapturePersistenceTests` |
| SC-009 retry exhaustion ≤ 5 + failed_terminal | ✅ | `CaptureRetryReliabilityTests` |

## Notes

- DB-gated tests (🧪) run when `ALUKI_TEST_POSTGRES` points to a PostgreSQL with
  the `vector` extension; migrations `001`–`005` are applied automatically by the
  test fixture.
- Load/latency items (📈) require a baseline run per the quickstart; the
  coordinator-level SLA test provides a fast-path sanity bound.
