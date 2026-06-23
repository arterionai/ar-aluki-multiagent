# Tasks: Instagram Channel Capture

**Input**: Design documents from `/specs/013-instagram-capture/`

**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`

**Tests**: Included. The specification explicitly requires scenario-based and measurable validation for reliability, isolation, retries, SLA outcomes, and signature security.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing in the current .NET solution. All tasks target the `013-instagram-capture` feature branch.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency conflicts)
- **[Story]**: User story label (`[US1]`..`[US5]`)
- Every task includes: dependencies, file touch points, Definition of Done, and requirement traceability.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish scaffolding for Instagram channel components without touching any deployed WhatsApp path.

- [ ] T001 Create feature scaffolding folders for Instagram capture in `src/Aluki.Runtime.Abstractions/Channels/Instagram/`, `src/Aluki.Runtime.Capture/Channels/Instagram/`, and `src/Aluki.Runtime.Functions/Functions/`
  Depends on: None
  Touch points: `src/Aluki.Runtime.Abstractions/Channels/Instagram/.gitkeep`; `src/Aluki.Runtime.Capture/Channels/Instagram/.gitkeep`
  Definition of Done: Folders exist in source control and match plan structure.
  Traceability: FR-001, FR-012

- [ ] T002 [P] Create test scaffolding folders for Instagram unit, integration, and contract tests
  Depends on: None
  Touch points: `tests/unit/Aluki.Runtime.UnitTests/Instagram/.gitkeep`; `tests/integration/Aluki.Runtime.IntegrationTests/Instagram/.gitkeep`; `tests/contract/Aluki.Runtime.ContractTests/Instagram/.gitkeep`
  Definition of Done: Test folders exist and the three test projects build.
  Traceability: FR-001..FR-022, SC-001..SC-011

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Migration, new contracts, and DI wiring that all user story tasks depend on.

**Critical**: No user story implementation starts before this phase is complete.

- [ ] T003 Write migration `022_instagram_channel_registrations.sql` with table, unique constraint on `(igsid, ig_account_id)`, check constraint on `status`, and RLS policies
  Depends on: T001
  Touch points: `db/migrations/022_instagram_channel_registrations.sql`
  Definition of Done: Migration creates `instagram_channel_registrations` table with all fields, unique index, status check, RLS `SELECT/INSERT` policies on `tenant_id = app.current_tenant()`.
  Traceability: FR-014, FR-022, SC-003, SC-004

- [ ] T004 Append `022_instagram_channel_registrations.sql` to the explicit migration loop in `.github/workflows/azure-deploy-runtime.yml` and to the fixture list in `tests/integration/.../DbCaptureFixture.cs`
  Depends on: T003
  Touch points: `.github/workflows/azure-deploy-runtime.yml`; `tests/integration/Aluki.Runtime.IntegrationTests/DbCaptureFixture.cs`
  Definition of Done: Migration appears in both the deploy workflow loop and the fixture setup; integration tests apply it before running.
  Traceability: FR-014, FR-022

- [ ] T005 [P] Define `InstagramInboundEnvelope` record and `InstagramAttachmentType` enum in `src/Aluki.Runtime.Abstractions/Channels/Instagram/InstagramInboundEnvelope.cs`
  Depends on: T001
  Touch points: `src/Aluki.Runtime.Abstractions/Channels/Instagram/InstagramInboundEnvelope.cs`
  Definition of Done: Record contains `Mid`, `SenderIgsid`, `RecipientAccountId`, `Timestamp`, `Text`, `Attachments` (list of type+URL), `IsEcho`, `IsStoryMention`, `IsReaction`, `RawEnvelopeRef`. Enum covers `Text|Image|Audio|Video|Sticker|File|Fallback|Reaction|StoryMention|StoryReply|Echo|Unknown`.
  Traceability: FR-001, FR-010, FR-015

- [ ] T006 [P] Add `ChannelType.Instagram = "instagram"` constant to `src/Aluki.Runtime.Abstractions/Orchestration/Dispatch/UnifiedMessage.cs`
  Depends on: T001
  Touch points: `src/Aluki.Runtime.Abstractions/Orchestration/Dispatch/UnifiedMessage.cs`
  Definition of Done: `ChannelType.Instagram` constant is available; existing `ChannelType.WhatsApp` value is unchanged; project compiles.
  Traceability: FR-001, FR-020

- [ ] T007 [P] Define `IInstagramMessenger` interface in `src/Aluki.Runtime.Abstractions/Channels/Instagram/IInstagramMessenger.cs`
  Depends on: T001
  Touch points: `src/Aluki.Runtime.Abstractions/Channels/Instagram/IInstagramMessenger.cs`
  Definition of Done: Interface exposes `SendTextMessageAsync(string recipientIgsid, string igAccountId, string text, CancellationToken ct)` returning `InstagramSendResult` (Sent | ReconnectRequired | Failed) and `SendSeenAndTypingAsync(string recipientIgsid, string igAccountId, CancellationToken ct)` returning `Task`.
  Traceability: FR-019, FR-021

- [ ] T008 [P] Define `IInstagramChannelRegistrationRepository` interface in `src/Aluki.Runtime.Abstractions/Channels/Instagram/`
  Depends on: T001
  Touch points: `src/Aluki.Runtime.Abstractions/Channels/Instagram/IInstagramChannelRegistrationRepository.cs`
  Definition of Done: Interface exposes `ResolveAsync(string igsid, string igAccountId, CancellationToken ct)` returning `InstagramRegistration?` and `RegisterAsync(InstagramRegistration registration, CancellationToken ct)` returning `RegistrationResult` (Registered | DuplicateRejected).
  Traceability: FR-014, FR-022

- [ ] T009 Create `AddInstagramCapture()` DI extension stub in `src/Aluki.Runtime.Functions/` and register it in `Program.cs` / function host startup
  Depends on: T005, T006, T007, T008
  Touch points: `src/Aluki.Runtime.Functions/InstagramCaptureServiceExtensions.cs`; `src/Aluki.Runtime.Functions/Program.cs`
  Definition of Done: Extension method compiles and is called during startup; no services fail registration even if implementations are stubs.
  Traceability: FR-001, FR-007, FR-012

**Checkpoint**: Foundation complete. User stories can proceed.

---

## Phase 3: User Story 1 — Capture Instagram DMs reliably (Priority: P1) 🎯 MVP

**Goal**: Normalize and persist valid Instagram DM text/image/audio events exactly once with canonical deduplication.

### Tests for User Story 1

- [ ] T010 [P] [US1] Implement unit tests for `MetaInstagramWebhookMapper` covering text DM, image attachment, audio attachment, unsupported types, echo messages, and empty messaging array in `tests/unit/.../Instagram/MetaInstagramWebhookMapperTests.cs`
  Depends on: T002, T005
  Touch points: `tests/unit/Aluki.Runtime.UnitTests/Instagram/MetaInstagramWebhookMapperTests.cs`
  Definition of Done: Tests exercise all message type branches; assertions on `SenderIgsid`, `RecipientAccountId`, `Mid`, `Text`, `AttachmentType`, `IsEcho` fields.
  Traceability: FR-001, FR-010, FR-015

- [ ] T011 [P] [US1] Implement unit tests for `NormalizeInstagramInboundSkill` covering supported types (text→`UnifiedMessage.Text`, image→`MediaRefs`, audio→`MediaRefs`) and unsupported fallback in `tests/unit/.../Instagram/NormalizeInstagramInboundSkillTests.cs`
  Depends on: T002, T005, T006
  Touch points: `tests/unit/Aluki.Runtime.UnitTests/Instagram/NormalizeInstagramInboundSkillTests.cs`
  Definition of Done: Tests assert `ChannelType.Instagram`, correct `SenderExternalId` (IGSID), `PhoneNumberId` (ig_account_id), `message_kind`, and `MediaRefs` population.
  Traceability: FR-001, FR-010, FR-015

- [ ] T012 [P] [US1] Implement integration tests for Instagram capture pipeline persistence and duplicate suppression in `tests/integration/.../Instagram/InstagramCapturePipelineTests.cs`
  Depends on: T002, T004
  Touch points: `tests/integration/Aluki.Runtime.IntegrationTests/Instagram/InstagramCapturePipelineTests.cs`
  Definition of Done: Tests prove single canonical artifact per unique `mid`; duplicate `mid` redelivery produces no extra artifact and emits `capture.duplicate_suppressed`; `source_channel = 'instagram'` is persisted.
  Traceability: FR-002, FR-003, FR-004, FR-013, SC-002, SC-003, SC-008

### Implementation for User Story 1

- [ ] T013 [P] [US1] Implement `MetaInstagramWebhookMapper` in `src/Aluki.Runtime.Capture/Channels/Instagram/MetaInstagramWebhookMapper.cs`
  Depends on: T005, T009
  Touch points: `src/Aluki.Runtime.Capture/Channels/Instagram/MetaInstagramWebhookMapper.cs`
  Definition of Done: Parses `object: "instagram"` webhook JSON; iterates `entry[].messaging[]`; classifies `message.text`, `message.attachments[].type` into `InstagramAttachmentType`; extracts `sender.id` as `SenderIgsid`, `recipient.id` as `RecipientAccountId`, `message.mid` as provider message ID; marks `is_echo`, reactions, and story events as unsupported; returns `IReadOnlyList<InstagramInboundEnvelope>`.
  Traceability: FR-001, FR-010, FR-015

- [ ] T014 [US1] Implement `NormalizeInstagramInboundSkill` in `src/Aluki.Runtime.Capture/Channels/Instagram/NormalizeInstagramInboundSkill.cs`
  Depends on: T005, T006, T013
  Touch points: `src/Aluki.Runtime.Capture/Channels/Instagram/NormalizeInstagramInboundSkill.cs`
  Definition of Done: Maps `InstagramInboundEnvelope` to `NormalizedCaptureMessage` (text/image/audio/unsupported) and `UnifiedMessage` with `ChannelType.Instagram`, `SenderExternalId = SenderIgsid`, `PhoneNumberId = RecipientAccountId`, `MessageId = Mid`. Unsupported attachments produce `message_kind = unsupported` without error.
  Traceability: FR-001, FR-010, FR-015

- [ ] T015 [US1] Implement `InstagramCaptureCoordinator` in `src/Aluki.Runtime.Capture/Channels/Instagram/InstagramCaptureCoordinator.cs`
  Depends on: T014, T009
  Touch points: `src/Aluki.Runtime.Capture/Channels/Instagram/InstagramCaptureCoordinator.cs`
  Definition of Done: Ordered pipeline: `InstagramPrincipalResolver` → `ScopeGuardSkill` → `IdempotencyGuardSkill` → `NormalizeInstagramInboundSkill` → `PersistCaptureSkill` → `WriteCaptureAuditSkill` → `IMessageDispatcher`. Returns `CaptureResult` (accepted, duplicate_suppressed, scope_denied, unsupported, failed_terminal). No exception propagated to caller.
  Traceability: FR-001, FR-007, FR-010, FR-013, FR-020

**Checkpoint**: US1 capture pipeline is independently functional and testable.

---

## Phase 4: User Story 2 — Enforce sender identity and tenant isolation (Priority: P1)

**Goal**: Resolve IGSID to tenant/user before any side effect; deny and audit unregistered senders.

### Tests for User Story 2

- [ ] T016 [P] [US2] Implement unit tests for `InstagramPrincipalResolver` covering registered IGSID (returns principal), unregistered IGSID (returns scope_denied), and revoked registration (returns scope_denied) in `tests/unit/.../Instagram/InstagramPrincipalResolverTests.cs`
  Depends on: T002, T008
  Touch points: `tests/unit/Aluki.Runtime.UnitTests/Instagram/InstagramPrincipalResolverTests.cs`
  Definition of Done: Tests mock `IInstagramChannelRegistrationRepository`; assertions on `PrincipalContext` fields and denial reason.
  Traceability: FR-014, SC-004

- [ ] T017 [P] [US2] Implement integration tests for cross-tenant isolation and unregistered-IGSID denial in `tests/integration/.../Instagram/InstagramScopeIsolationTests.cs`
  Depends on: T002, T004
  Touch points: `tests/integration/Aluki.Runtime.IntegrationTests/Instagram/InstagramScopeIsolationTests.cs`
  Definition of Done: Tests prove tenant A cannot read tenant B artifacts; unregistered IGSID produces `capture.scope_denied` audit event and no capture artifact.
  Traceability: FR-005, FR-006, FR-014, SC-004

### Implementation for User Story 2

- [ ] T018 [US2] Implement `InstagramChannelRegistrationRepository` backed by `instagram_channel_registrations` table in `src/Aluki.Runtime.Capture/Channels/Instagram/InstagramChannelRegistrationRepository.cs`
  Depends on: T003, T008
  Touch points: `src/Aluki.Runtime.Capture/Channels/Instagram/InstagramChannelRegistrationRepository.cs`
  Definition of Done: `ResolveAsync` returns active registration for `(igsid, ig_account_id)` or `null` if absent/revoked; `RegisterAsync` enforces unique constraint and returns `DuplicateRejected` on conflict. Sets session GUC before every query.
  Traceability: FR-014, FR-022, SC-004

- [ ] T019 [US2] Implement `InstagramPrincipalResolver` in `src/Aluki.Runtime.Capture/Channels/Instagram/InstagramPrincipalResolver.cs`
  Depends on: T018
  Touch points: `src/Aluki.Runtime.Capture/Channels/Instagram/InstagramPrincipalResolver.cs`
  Definition of Done: Resolves `InstagramInboundEnvelope.(SenderIgsid, RecipientAccountId)` → `PrincipalContext`; returns structured `scope_denied` reason for unregistered or revoked IGSID.
  Traceability: FR-014, SC-004

- [ ] T020 [US2] Register `InstagramChannelRegistrationRepository` and `InstagramPrincipalResolver` in `AddInstagramCapture()` extension
  Depends on: T009, T018, T019
  Touch points: `src/Aluki.Runtime.Functions/InstagramCaptureServiceExtensions.cs`
  Definition of Done: DI registration compiles; integration tests resolve both services from the container.
  Traceability: FR-014

**Checkpoint**: US2 is independently functional and testable.

---

## Phase 5: User Story 3 — Deliver conversational AI responses over Instagram DMs (Priority: P1)

**Goal**: Send LLM-generated text replies to Instagram senders; audio acknowledgment without LLM.

### Tests for User Story 3

- [ ] T021 [P] [US3] Implement unit tests for `MetaInstagramMessenger` covering successful send, 401/403 → `reconnect_required`, and non-blocking failure in `tests/unit/.../Instagram/MetaInstagramMessengerTests.cs`
  Depends on: T002, T007
  Touch points: `tests/unit/Aluki.Runtime.UnitTests/Instagram/MetaInstagramMessengerTests.cs`
  Definition of Done: Tests mock `HttpClient`; assert correct `POST /{igAccountId}/messages` body shape; assert `ReconnectRequired` result on 401/403; assert no exception propagation.
  Traceability: FR-021

- [ ] T022 [P] [US3] Implement integration tests for `ConversationalResponseAgent` Instagram outbound path in `tests/integration/.../Instagram/InstagramConversationalResponseTests.cs`
  Depends on: T002
  Touch points: `tests/integration/Aluki.Runtime.IntegrationTests/Instagram/InstagramConversationalResponseTests.cs`
  Definition of Done: Tests verify that a captured Instagram text message triggers `IInstagramMessenger.SendTextMessageAsync`; audio message triggers acknowledgment without LLM call; `reconnect_required` does not throw.
  Traceability: FR-020, FR-021, SC-001

### Implementation for User Story 3

- [ ] T023 [US3] Implement `MetaInstagramMessenger` in `src/Aluki.Runtime.Capture/Channels/Instagram/MetaInstagramMessenger.cs`
  Depends on: T007, T009
  Touch points: `src/Aluki.Runtime.Capture/Channels/Instagram/MetaInstagramMessenger.cs`
  Definition of Done: `SendTextMessageAsync` posts `{"messaging_type":"RESPONSE","recipient":{"id":"<igsid>"},"message":{"text":"<text>"}}` to `GET /{igAccountId}/messages`; returns `Sent` on 200, `ReconnectRequired` on 401/403, `Failed` otherwise. `SendSeenAndTypingAsync` sends `sender_action: "mark_seen"` then `"typing_on"`; swallows all errors. Token from `Instagram:AccessToken` config.
  Traceability: FR-019, FR-021

- [ ] T024 [US3] Extend `ConversationalResponseAgent.HandleAsync` in `src/Aluki.Runtime.Conversation/Agents/ConversationalResponseAgent.cs` to handle `ChannelType.Instagram`
  Depends on: T006, T007, T023
  Touch points: `src/Aluki.Runtime.Conversation/Agents/ConversationalResponseAgent.cs`
  Definition of Done: Existing `ChannelType.WhatsApp` branch is unchanged. New `else if (message.ChannelType == ChannelType.Instagram)` branch resolves `IInstagramMessenger` via `IServiceScopeFactory`, calls `SendTextMessageAsync`, logs `reconnect_required` without throwing. Audio acknowledgment path also dispatched via `IInstagramMessenger`.
  Traceability: FR-020, FR-021

- [ ] T025 [US3] Register `MetaInstagramMessenger` via `AddHttpClient<IInstagramMessenger, MetaInstagramMessenger>` in `AddInstagramCapture()`
  Depends on: T009, T023
  Touch points: `src/Aluki.Runtime.Functions/InstagramCaptureServiceExtensions.cs`
  Definition of Done: Transient `HttpClient` is configured with Graph API base URL; base address resolves from `Instagram:GraphBaseUrl` (default `https://graph.facebook.com/v21.0`).
  Traceability: FR-021

**Checkpoint**: US3 is independently functional and testable.

---

## Phase 6: User Story 4 — Acknowledge Instagram messages with read receipt and typing indicator (Priority: P2)

**Goal**: Mark-seen + typing-on on every valid inbound DM, best-effort, non-blocking.

### Tests for User Story 4

- [ ] T026 [P] [US4] Implement unit tests verifying `MetaInstagramWebhookFunction` calls `SendSeenAndTypingAsync` before coordinator dispatch, and that a failure in that call does not prevent capture in `tests/unit/.../Instagram/MetaInstagramWebhookFunctionTests.cs`
  Depends on: T002, T007
  Touch points: `tests/unit/Aluki.Runtime.UnitTests/Instagram/MetaInstagramWebhookFunctionTests.cs`
  Definition of Done: Tests mock `IInstagramMessenger`; verify call sequence; assert HTTP 200 is still returned when `SendSeenAndTypingAsync` throws.
  Traceability: FR-019, SC-011

### Implementation for User Story 4

- [ ] T027 [US4] Implement `MetaInstagramWebhookFunction` in `src/Aluki.Runtime.Functions/Functions/MetaInstagramWebhookFunction.cs`
  Depends on: T009, T013, T015, T023
  Touch points: `src/Aluki.Runtime.Functions/Functions/MetaInstagramWebhookFunction.cs`
  Definition of Done: `GET api/instagram` handles Meta webhook verification (hub.challenge). `POST api/instagram`: (1) validates `X-Hub-Signature-256` using `Instagram:AppSecret` — rejects 403 on mismatch with no side effects; (2) parses payload via `MetaInstagramWebhookMapper`; (3) for each envelope, calls `IInstagramMessenger.SendSeenAndTypingAsync` best-effort in a `Task.Run`; (4) calls `InstagramCaptureCoordinator.CaptureAsync`; (5) always returns HTTP 200 so Meta does not retry. Errors are logged, never surfaced to Meta.
  Traceability: FR-007, FR-018, FR-019, SC-006, SC-007, SC-010, SC-011

**Checkpoint**: US4 is independently functional and testable.

---

## Phase 7: User Story 5 — Preserve operational reliability and traceability (Priority: P2)

**Goal**: Bounded retries, terminal failure visibility, measurable SLA telemetry for Instagram channel.

### Tests for User Story 5

- [ ] T028 [P] [US5] Implement resilience integration tests for retry success and exhaustion on Instagram channel in `tests/integration/.../Instagram/InstagramRetryReliabilityTests.cs`
  Depends on: T002, T015
  Touch points: `tests/integration/Aluki.Runtime.IntegrationTests/Instagram/InstagramRetryReliabilityTests.cs`
  Definition of Done: Fault-injection tests prove max 5 retry attempts; eventual persistence on transient fault; `capture.failed_terminal` on retry exhaustion with correlation fields.
  Traceability: FR-009, FR-017, SC-001, SC-009

- [ ] T029 [P] [US5] Implement performance / acknowledgment-latency test verifying P95 ≤ 2s and P99 ≤ 3s for Instagram inbound DMs in `tests/integration/.../Instagram/InstagramSlaTests.cs`
  Depends on: T002, T027
  Touch points: `tests/integration/Aluki.Runtime.IntegrationTests/Instagram/InstagramSlaTests.cs`
  Definition of Done: Benchmark report shows P95/P99 ack times within budget; non-blocking mark-seen/typing-on confirmed.
  Traceability: FR-012, SC-006, SC-007

### Implementation for User Story 5

- [ ] T030 [US5] Verify that `CaptureRetryPolicy` (SB-001) is wired into `InstagramCaptureCoordinator` and confirm telemetry stage names include `instagram` channel discriminator
  Depends on: T015
  Touch points: `src/Aluki.Runtime.Capture/Channels/Instagram/InstagramCaptureCoordinator.cs`; `src/Aluki.Runtime.Host/Observability/CaptureTelemetry.cs`
  Definition of Done: Coordinator wraps `PersistCaptureSkill` with `CaptureRetryPolicy`; telemetry events include `channel = "instagram"` dimension.
  Traceability: FR-009, FR-012, FR-017, SC-001, SC-005, SC-009

**Checkpoint**: US5 is independently functional and testable.

---

## Phase 8: Contract Tests and Webhook Validation

**Goal**: Validate complete webhook request/response contract for Instagram channel.

- [ ] T031 [P] Implement contract tests for `MetaInstagramWebhookFunction` covering: valid text DM → 200, valid image DM → 200, valid audio DM → 200, unsupported type → 200 (accepted-but-unsupported), missing signature → 403, invalid signature → 403, duplicate mid → 200 (duplicate-safe), unregistered IGSID → 200 (scope-denied + audit) in `tests/contract/.../Instagram/InstagramInboundContractTests.cs`
  Depends on: T002, T027
  Touch points: `tests/contract/Aluki.Runtime.ContractTests/Instagram/InstagramInboundContractTests.cs`; `specs/013-instagram-capture/contracts/instagram-inbound-contract.yaml`
  Definition of Done: All contract scenarios produce the asserted HTTP status; audit events are verified for scope-denied and duplicate-suppressed outcomes; missing/invalid signature produces 403 with no capture side effect (SC-010).
  Traceability: FR-007, FR-018, SC-010

---

## Phase 9: Polish and Evidence

- [ ] T032 [P] Create FR/SC evidence checklist in `specs/013-instagram-capture/checklists/requirements.md`
  Depends on: T027, T028, T029, T030, T031
  Touch points: `specs/013-instagram-capture/checklists/requirements.md`
  Definition of Done: Every FR-001..FR-022 and SC-001..SC-011 has test/evidence linkage and status.
  Traceability: FR-001..FR-022, SC-001..SC-011

- [ ] T033 Update `CLAUDE.md` delivery state section to record SB-013 as done when merged
  Depends on: T032
  Touch points: `CLAUDE.md`
  Definition of Done: SB-013 entry describes deployed components, config keys, HTTP routes, and migration number.
  Traceability: All

---

## Dependencies and Execution Order

### Phase Dependencies

- Setup (Phase 1): Starts immediately.
- Foundational (Phase 2): Depends on setup; blocks all user stories.
- User Stories (Phases 3–7): Depend on foundational completion.
- Contract Tests (Phase 8): Depend on Phase 3 and Phase 4 webhook function.
- Polish (Phase 9): Depends on all prior phases.

### Critical Task Chain

T001 → T003 → T004 → T005..T009 → T013 → T014 → T015 → T027 → T031 → T032 → T033

### Parallel Opportunities

- T001 and T002 in parallel (different directories).
- T005, T006, T007, T008 in parallel after T001.
- T010, T011, T012 in parallel after T002, T005.
- T016, T017 in parallel after T002, T008.
- T021, T022 in parallel after T002, T007.
- T026, T028, T029 in parallel after T002.

---

## Requirement Traceability Matrix

- FR-001: T005, T006, T010, T011, T012, T013, T014, T015
- FR-002: T003, T012, T015
- FR-003: T003, T012, T015
- FR-004: T003, T012, T015
- FR-005: T003, T017, T018, T019
- FR-006: T003, T017, T018, T019
- FR-007: T027, T031
- FR-008: T015, T030
- FR-009: T028, T030
- FR-010: T005, T010, T011, T013, T014, T015
- FR-011: T015 (via inherited ScopeGuardSkill)
- FR-012: T029, T030
- FR-013: T003, T012, T015
- FR-014: T003, T016, T017, T018, T019, T020
- FR-015: T005, T010, T011, T013, T014, T015
- FR-016: T015, T030
- FR-017: T028, T030
- FR-018: T026, T027, T031
- FR-019: T021, T023, T026, T027
- FR-020: T006, T015
- FR-021: T021, T022, T023, T024, T025
- FR-022: T003, T008, T018

- SC-001: T028, T030
- SC-002: T012
- SC-003: T003, T012
- SC-004: T003, T016, T017
- SC-005: T030
- SC-006: T029, T030
- SC-007: T029, T030
- SC-008: T012
- SC-009: T028, T030
- SC-010: T026, T027, T031
- SC-011: T026, T027, T029

---

## Implementation Strategy

### MVP First (US1 + US2 + US3 text-only)

1. Complete Phases 1–2.
2. Complete US1 tasks T013–T015.
3. Complete US2 tasks T018–T020.
4. Complete US3 tasks T023–T025.
5. Deploy webhook function T027 and validate end-to-end with a live Instagram DM.

### Incremental Delivery

1. Add US4 typing indicator (T026–T027 mark-seen/typing-on — already in webhook function).
2. Add US5 reliability and SLA validation (T028–T030).
3. Add contract tests (T031) and close evidence (T032–T033).
