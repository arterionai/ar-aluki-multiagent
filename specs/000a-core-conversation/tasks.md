# Tasks: Core Conversational Response (SB-000)

**Input**: Design documents from /specs/000a-core-conversation/

**Prerequisites**: spec.md (required), data-model.md (required), research.md, contracts/

**Tests**: Included. Grounding correctness and idempotency are hard requirements.

**Organization**: Tasks are grouped by phase. Phases 1 and 2 must complete before
user story work begins.

## Format: [ID] [P?] [Story] Description
- [P]: Can run in parallel with other tasks in the same phase
- [Story]: User story label (US1–US4)

---

## Phase 1: Contracts and Interfaces

- [x] T001 Add `IConversationHistoryStore` in `src/Aluki.Runtime.Abstractions/Conversation/IConversationHistoryStore.cs`
- [x] T002 [P] Add `IOutboundMessageStore` in `src/Aluki.Runtime.Abstractions/Conversation/IOutboundMessageStore.cs`
- [x] T003 [P] Add `ConversationTurn` and `OutboundMessage` models in `src/Aluki.Runtime.Abstractions/Conversation/ConversationModels.cs`
- [x] T004 [P] Extend `IWhatsAppMessenger` with `SendTextMessageAsync(phoneNumberId, recipientWaId, text, CancellationToken)` in `src/Aluki.Runtime.Capture/Channels/WhatsApp/IWhatsAppMessenger.cs`
- [x] T005 [P] Add `NullWhatsAppMessenger.SendTextMessageAsync` no-op implementation in `src/Aluki.Runtime.Capture/Channels/WhatsApp/NullWhatsAppMessenger.cs`

---

## Phase 2: Migration and Persistence

- [x] T006 Create migration `db/migrations/020_conversational_response.sql` — `outbound_messages` table, RLS policy, and `ix_outbound_tenant_user_created` index
- [x] T007 Implement `MetaWhatsAppMessenger.SendTextMessageAsync` using Meta Graph API `/{phone_number_id}/messages` POST in `src/Aluki.Runtime.Capture/Channels/WhatsApp/WhatsAppMessenger.cs`
- [x] T008 [P] Implement `OutboundMessageStore` with idempotent upsert (INSERT … ON CONFLICT DO NOTHING RETURNING) in `src/Aluki.Runtime.Conversation/OutboundMessageStore.cs`
- [x] T009 [P] Implement `ConversationHistoryStore` querying `unified_message_artifact` UNION `outbound_messages` ordered by `created_at_utc DESC LIMIT N` in `src/Aluki.Runtime.Conversation/ConversationHistoryStore.cs`
- [x] T010 [P] Add migration filename to the explicit list in `.github/workflows/azure-deploy-runtime.yml` and to `tests/integration/.../DbCaptureFixture.cs`

**Checkpoint**: Persistence layer ready. Agent work can begin.

---

## Phase 3: US1 — Text message grounded reply

### Tests

- [ ] T011 [P] [US1] Add unit tests for context assembly (history + recall output → LLM prompt) in `tests/Aluki.Runtime.Tests/Conversation/ContextAssemblyTests.cs`
- [ ] T012 [P] [US1] Add unit tests for system prompt grounding instructions in `tests/Aluki.Runtime.Tests/Conversation/SystemPromptTests.cs`
- [ ] T013 [P] [US1] Add contract tests for outbound message schema in `tests/Aluki.Runtime.Contracts.Tests/ConversationContractTests.cs`
- [ ] T014 [P] [US1] Add integration tests for end-to-end reply flow (inbound → recall → LLM stub → outbound) in `tests/Aluki.Runtime.IntegrationTests/Conversation/ConversationalResponseIntegrationTests.cs`
- [ ] T015 [P] [US1] Add integration tests for idempotency (replay same inbound → single outbound_messages row) in `tests/Aluki.Runtime.IntegrationTests/Conversation/OutboundIdempotencyTests.cs`

### Implementation

- [x] T016 [US1] Implement `ConversationalResponseAgent.HandleAsync` — history fetch, recall, prompt assembly, LLM call, outbound send, persist in `src/Aluki.Runtime.Conversation/ConversationalResponseAgent.cs`
- [x] T017 [US1] Implement system prompt builder with grounding rules and citation style in `src/Aluki.Runtime.Conversation/ConversationPromptBuilder.cs`
- [x] T018 [US1] Register `ConversationalResponseAgent` as `IDomainAgent` (priority = 100) in `src/Aluki.Runtime.Host/Program.cs` and `src/Aluki.Runtime.Functions/Program.cs`
- [x] T019 [US1] Add `Conversation` options binding and validation (HistoryWindowSize, LlmTimeoutSeconds, ErrorFallbackMessage) in `src/Aluki.Runtime.Host/appsettings.json` and `Program.cs`

**Checkpoint**: US1 functional. Users receive grounded text replies.

---

## Phase 4: US2 — No-memory honest response

### Tests

- [ ] T020 [P] [US2] Add unit tests for no-results path in prompt builder (honest acknowledgment, no fabrication) in `tests/Aluki.Runtime.Tests/Conversation/NoMemoryResponseTests.cs`
- [ ] T021 [P] [US2] Add integration test: query a topic absent from memory → reply contains no fabricated facts in `tests/Aluki.Runtime.IntegrationTests/Conversation/NoMemoryHonestyTests.cs`

### Implementation

- [x] T022 [US2] Handle empty recall result in `ConversationalResponseAgent.HandleAsync` — append no-memory suffix when `MemoryStatus.NoResult` in `src/Aluki.Runtime.Conversation/ConversationalResponseAgent.cs`

**Checkpoint**: US2 functional. Aluki never fabricates.

---

## Phase 5: US3 — Audio acknowledgment

### Tests

- [ ] T023 [P] [US3] Add unit tests for audio message type detection and acknowledgment path in `tests/Aluki.Runtime.Tests/Conversation/AudioAcknowledgmentTests.cs`
- [ ] T024 [P] [US3] Add integration test: inbound audio → acknowledgment reply sent, MemoryDomainAgent also runs in `tests/Aluki.Runtime.IntegrationTests/Conversation/AudioAcknowledgmentIntegrationTests.cs`

### Implementation

- [x] T025 [US3] Add audio branch in `ConversationalResponseAgent.HandleAsync` — detect `MediaRefs.Any(r => r.MediaKind == "audio")`, send acknowledgment, skip LLM call in `src/Aluki.Runtime.Conversation/ConversationalResponseAgent.cs`

**Checkpoint**: US3 functional. Audio messages get immediate feedback.

---

## Phase 6: US4 — Graceful error handling

### Tests

- [ ] T026 [P] [US4] Add unit tests for LLM timeout → error fallback message path in `tests/Aluki.Runtime.Tests/Conversation/ErrorFallbackTests.cs`
- [ ] T027 [P] [US4] Add integration test: simulate LLM failure → friendly message sent, `outbound_messages.status = error_fallback` in `tests/Aluki.Runtime.IntegrationTests/Conversation/ErrorFallbackIntegrationTests.cs`

### Implementation

- [x] T028 [US4] Wrap LLM call in try/catch with timeout in `ConversationalResponseAgent.HandleAsync`; on failure send `ErrorFallbackMessage` and persist with `status = error_fallback` in `src/Aluki.Runtime.Conversation/ConversationalResponseAgent.cs`

**Checkpoint**: US4 functional. No silent failures.

---

## Phase 7: Polish

- [x] T029 [P] Update `CLAUDE.md` delivery state to reflect SB-000 done and migration numbering shift (SB-010 billing → 027–033)
- [ ] T030 [P] Update quickstart with smoke-test steps in `specs/000a-core-conversation/quickstart.md`
- [x] T031 Build and run all touched test suites; capture summary in `specs/000a-core-conversation/validation-report.md`
- [x] T032 Produce FR traceability table in `specs/000a-core-conversation/checklists/requirements.md`

---

## Dependencies and Execution Order

```
Phase 1 (T001–T005)
    ↓
Phase 2 (T006–T010)
    ↓
Phase 3–6 (can run in parallel per story after Phase 2)
    ↓
Phase 7
```

## Critical Path

T001 → T004 → T007 → T016 → T018 → T031
