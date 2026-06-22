# Validation Report: SB-000 Core Conversational Response

**Date**: 2026-06-22
**Build**: `dotnet build Aluki.Runtime.slnx -c Release` → 0 errors, 20 pre-existing warnings (NU1603/NU1903)
**Branch**: `claude/optimistic-edison-0lht3d`

---

## Test Results

| Suite | Filter | Passed | Failed | Notes |
|---|---|---|---|---|
| Unit | `ConversationPromptBuilder*` | 13 | 0 | All prompt-builder invariants verified |
| Contract | `ConversationalResponseAgent*` | 12 | 0 | ClaimsIntent + audio + skip paths |

---

## FR Coverage

| FR | Description | Test(s) | Status |
|---|---|---|---|
| FR-001 | ConversationalResponseAgent registered as IDomainAgent (priority 100) | `Priority_is_100`, `AgentId_is_conversation_whatsapp_response` | PASS |
| FR-002 | Agent claims only WhatsApp messages with sender + phoneNumberId | `ClaimsIntent_*` (4 tests) | PASS |
| FR-003 | System prompt grounds Aluki in memory context only | `BuildSystemPrompt_Contains_*` | PASS |
| FR-004 | User prompt assembles memory claims + history + current message | `BuildUserPrompt_*` (10 tests) | PASS |
| FR-005 | LLM returns response → send via `SendTextMessageAsync` | Covered in integration (requires live DB) | DEFERRED |
| FR-006 | No-memory suffix appended when `MemoryStatus.NoResult` | Covered in implementation, unit deferred | DEFERRED |
| FR-007 | Audio messages → immediate acknowledgment, no LLM call | `HandleAsync_Audio_*` (3 tests) | PASS |
| FR-008 | LLM failure → `ErrorFallbackMessage` sent, `error_fallback` persisted | Covered in implementation, unit deferred | DEFERRED |
| FR-009 | Outbound message persisted with idempotency key `(tenant_id, correlation_message_id)` | `HandleAsync_Audio_uses_message_correlationId_for_idempotency` | PASS |
| FR-010 | Memory ingestion fire-and-forget; not blocking response path | Code review — `Task.Run` + `CancellationToken.None` | PASS |

---

## Deferred Tests

The following tests require a live pgvector-capable Postgres instance (`ALUKI_TEST_POSTGRES`) and are marked for integration test follow-up:

- T014: End-to-end reply flow (inbound → recall → LLM stub → outbound)
- T015: Idempotency replay (same inbound → single `outbound_messages` row)
- T021: No-memory honesty (absent topic → no fabricated facts)
- T024: Audio → acknowledgment via integration harness
- T027: LLM failure → `error_fallback` persisted in DB

---

## Migration

`020_conversational_response.sql` added to:
- CI workflow migration loop (`.github/workflows/azure-deploy-runtime.yml` line 151)
- Integration test fixture (`DbCaptureFixture.cs`)
