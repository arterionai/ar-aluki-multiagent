# Implementation Plan: Instagram Channel Capture

**Branch**: `013-instagram-capture` | **Date**: 2026-06-23 | **Spec**: `specs/013-instagram-capture/spec.md`

**Input**: Feature specification from `specs/013-instagram-capture/spec.md`

## Summary

Extend the Aluki runtime with a second inbound channel — Instagram Direct Messages — by adding a parallel capture stack that reuses all existing SB-001 infrastructure (capture tables, idempotency, audit, retry, scope guard, `UnifiedMessage`, `IMessageDispatcher`) and the SB-000 `ConversationalResponseAgent`. New components are limited to: the Instagram webhook function, payload mapper, normalization skill, capture coordinator, sender identity resolver, and outbound messenger. No domain agent logic changes are needed beyond extending `ConversationalResponseAgent` for Instagram outbound dispatch.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**: All existing project references (`Aluki.Runtime.Abstractions`, `Aluki.Runtime.Capture`, `Aluki.Runtime.Conversation`); `Npgsql` (already in use); `System.Text.Json` (already in use).

**Storage**: PostgreSQL. One new migration (`022_instagram_channel_registrations.sql`). All capture artifact tables reused from SB-001.

**Testing**: Unit tests for mapper, normalization skill, signature validation, and IGSID resolver. Integration tests for full capture pipeline, RLS isolation, and duplicate suppression on Instagram channel. Contract tests for webhook payload shapes.

**Target Platform**: Azure Functions isolated worker (same `Aluki.Runtime.Functions` project as all other deployed functions).

**Performance Goals**:
- Acknowledgment path (mark-seen + typing-on + HTTP 200) P95 ≤ 2s, P99 ≤ 3s.
- Capture success ≥ 99.5% valid DMs within 60s.
- 0% duplicate artifacts in redelivery scenarios.

**Constraints**:
- No side effects without resolved `PrincipalContext` from `instagram_channel_registrations`.
- Canonical idempotency key: `(tenant_id, 'instagram', mid)`.
- Retry limit: maximum 5 attempts with bounded exponential backoff.
- Always return HTTP 200 to Meta; never surface internal errors to the webhook caller.
- Outbound reply 401/403 must not fail the capture pipeline.

**Scale/Scope**:
- One Instagram Business Account per tenant (configuration constraint).
- Supported inbound: text DMs, image attachments, audio attachments.
- Unsupported (fallback only): video, sticker, file, reaction events, story mentions, story replies, echo messages.

## Architecture Decisions

1. **Parallel capture stack, identical structure to SB-001**
   - Decision: Mirror `MetaWhatsAppWebhookFunction` / `MetaWebhookMapper` / `NormalizeWhatsAppInboundSkill` / `WhatsAppCaptureCoordinator` pattern.
   - Why: Keeps each channel independently testable and deployable; changes to WhatsApp cannot regress Instagram.

2. **Single new DB table; all capture artifacts reused**
   - Decision: `instagram_channel_registrations` for IGSID mapping only; no new capture artifact tables.
   - Why: Idempotency key design already supports multi-channel; separate tables add schema without isolation benefit.

3. **`IInstagramMessenger` separate from `IWhatsAppMessenger`**
   - Decision: New interface and implementation for Instagram outbound; injected into `ConversationalResponseAgent`.
   - Why: Different Graph API endpoint path and different sender identity model; sharing the WhatsApp messenger adds conditional branching to a security-critical outbound path.

4. **`ConversationalResponseAgent` extended, not replaced**
   - Decision: Check `message.ChannelType` inside the agent and route outbound to the correct messenger.
   - Why: All LLM, history, memory-recall, and response logic is shared; only the outbound dispatch differs.

5. **`AddInstagramCapture()` DI extension in `Aluki.Runtime.Functions`**
   - Decision: Single registration method wires all new Instagram components.
   - Why: Mirrors `AddCalendarIntegration()` and `AddGovernance()` pattern; keeps `Program.cs` / function host startup readable.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Skill-First Execution**: PASS. Instagram capture uses the same skill pipeline (scope guard, idempotency, normalize, persist, audit) as WhatsApp.
- **Tenant-Scoped Security by Default**: PASS. IGSID resolution is a hard pre-side-effect gate; unregistered IGSIDs are denied and audited.
- **Grounded Memory and Provenance**: PASS. Capture artifacts include `source_channel`, IGSID provenance, and `mid` identity.
- **Durable Session and Workflow Separation**: PASS. Fast ack path (mark-seen + 200) is separated from durable capture pipeline.
- **Cost-Aware and Observable Intelligence**: PASS. Telemetry and audit events are mandatory; LLM calls only triggered for supported text/audio messages via the existing `ConversationalResponseAgent`.

No constitution violations require exceptions.

## Implementation Phases

### Phase A - Contracts, Envelope Types, and Migration

- Define `InstagramInboundEnvelope` in `Aluki.Runtime.Abstractions/Channels/Instagram/`.
- Add `ChannelType.Instagram = "instagram"` constant to `UnifiedMessage.cs`.
- Add `IInstagramMessenger` interface to `Aluki.Runtime.Abstractions/Channels/Instagram/`.
- Write migration `022_instagram_channel_registrations.sql` and append to the deployment loop and fixture list.
- Output: contract types, migration, updated ChannelType constant.

### Phase B - Sender Identity Resolution and Scope Gate

- Implement `IInstagramChannelRegistrationRepository` and `InstagramChannelRegistrationRepository` backed by the new table.
- Implement `InstagramPrincipalResolver`: looks up `(igsid, ig_account_id)` → `PrincipalContext`; returns `scope_denied` for unregistered senders.
- Wire RLS enforcement: same `ScopedSessionContextSetter` pattern from SB-001.
- Output: IGSID → principal resolution with denial path.

### Phase C - Webhook Function, Mapper, and Signature Validation

- Implement `MetaInstagramWebhookMapper`: parses `object: "instagram"` payload, extracts `entry[].messaging[]`, classifies `message.attachments` into supported/unsupported types, produces `InstagramInboundEnvelope` list.
- Implement `MetaInstagramWebhookFunction` (Azure HTTP trigger `GET api/instagram` for verification, `POST api/instagram` for inbound): HMAC-SHA256 validation using `Instagram:AppSecret` (fallback `Meta__AppSecret`), calls `IInstagramMessenger.SendSeenAndTypingAsync` best-effort, dispatches to `InstagramCaptureCoordinator`.
- Output: deployed webhook endpoint with signature validation and immediate acknowledgment behavior.

### Phase D - Normalization Skill and Capture Coordinator

- Implement `NormalizeInstagramInboundSkill`: maps `InstagramInboundEnvelope` to `NormalizedCaptureMessage` and `UnifiedMessage`; classifies video/sticker/reaction/story/echo as `unsupported`; carries IGSID as `SenderExternalId` and Instagram Business Account ID as `PhoneNumberId`.
- Implement `InstagramCaptureCoordinator`: ordered skill sequence — `InstagramPrincipalResolver` → `ScopeGuardSkill` → `IdempotencyGuardSkill` → `NormalizeInstagramInboundSkill` → `PersistCaptureSkill` → `WriteCaptureAuditSkill` → `IMessageDispatcher`; mirrors `WhatsAppCaptureCoordinator`.
- Output: end-to-end capture pipeline for Instagram DMs.

### Phase E - Outbound Messenger and Conversational Response Extension

- Implement `MetaInstagramMessenger`: `SendTextMessageAsync(igsid, igAccountId, text, ct)` calling `POST /{igAccountId}/messages` on Graph API `v21.0`; `SendSeenAndTypingAsync(igsid, igAccountId, ct)` with `sender_action: "mark_seen"` then `"typing_on"`; handles 401/403 as `reconnect_required`.
- Extend `ConversationalResponseAgent.HandleAsync`: add `else if (message.ChannelType == ChannelType.Instagram)` branch that resolves `IInstagramMessenger` and calls `SendTextMessageAsync`; `reconnect_required` outcome is logged but does not bubble as pipeline failure.
- Register `IInstagramMessenger` → `MetaInstagramMessenger` via `AddHttpClient` in `AddInstagramCapture()`.
- Output: full round-trip — inbound DM captured → LLM reply generated → reply sent over Instagram.

### Phase F - Validation Against Acceptance and Outcomes

- Execute contract tests for webhook request/response shapes (text, image, audio, unsupported, duplicate, denied, invalid-signature).
- Execute integration tests against real Postgres with RLS for scope isolation and duplicate suppression.
- Execute fault-injection tests for transient persistence failures and retry exhaustion.
- Verify P95/P99 ack latency under baseline load.
- Produce evidence bundle mapping FR-001..FR-022 and SC-001..SC-011 to test artifacts.
- Output: go/no-go evidence for feature merge.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| IGSID not stable across Meta App reinstalls | Stale registrations causing scope denials for real users | Scope denial is audited; tenant admins can re-register; revoked status allows cleanup without deleting history |
| Instagram Business Account registered to multiple tenants | Cross-tenant data leakage | Unique index on `(igsid, ig_account_id)` at DB level; FR-022 configuration-time check rejects duplicates |
| WhatsApp capture regression from `ChannelType` or `ConversationalResponseAgent` changes | WhatsApp production breakage | All WhatsApp paths covered by existing test suite; changes to `ConversationalResponseAgent` are additive (`else if`) not modifying existing `if` branch |
| Instagram Graph API 401 on outbound reply | Silent message loss without user feedback | `reconnect_required` outcome logged and surfaced to tenant ops; does not fail the capture pipeline |
| Mark-seen / typing-on adding latency to ack path | Miss SC-006/SC-007 | Both calls are fire-and-forget (`Task.Run` / best-effort); same pattern as WhatsApp read receipt |
| Unsupported message types triggering confusing LLM responses | User confusion | `NormalizeInstagramInboundSkill` sets `message_kind = unsupported`; `IMessageDispatcher` will not route unsupported kinds to `ConversationalResponseAgent` (matches WhatsApp behavior) |

## Dependencies

1. SB-001 capture infrastructure deployed and passing all integration tests.
2. SB-000 `ConversationalResponseAgent` and `outbound_messages` table available.
3. Meta Instagram Business Account API access, webhook subscription configured on `instagram` object with `messages` field.
4. `Instagram:AppSecret`, `Instagram:AccessToken`, `Instagram:GraphBaseUrl` config available (Key Vault refs in deployed env).
5. Migration `022_instagram_channel_registrations.sql` applied before any Instagram inbound traffic.

## Acceptance Criteria and Measurable Outcomes Alignment

| Spec Item | Plan Coverage | Validation Evidence |
|-----------|---------------|---------------------|
| FR-001, FR-002, FR-003 | Phases C + D (normalization and canonical persistence with `source_channel = 'instagram'`) | Contract + integration tests for text/image/audio/unsupported |
| FR-004, FR-013, SC-002, SC-008 | Phase D (idempotency guard keyed on `(tenant_id, 'instagram', mid)`) | Redelivery tests asserting zero duplicate artifacts |
| FR-005, FR-006, FR-014, SC-004 | Phase B (IGSID resolver + scope guard + deny-and-audit) | Unregistered-IGSID denial tests |
| FR-007 | Phase C (always HTTP 200 to Meta; internal outcome via audit/telemetry) | Contract tests verifying 200 response regardless of outcome |
| FR-008, FR-016, SC-005 | Phase D (mandatory audit event set via `WriteCaptureAuditSkill`) | Audit stream assertions and fault-injection checks |
| FR-009, FR-017, SC-001, SC-009 | Phase D (retry policy inherited from SB-001 `CaptureRetryPolicy`) | Fault-injection tests with attempt-count assertions |
| FR-010, FR-015 | Phase D (NormalizeInstagramInboundSkill unsupported fallback path) | Unsupported-type tests validating minimal artifact |
| FR-011 | Phase D (ScopeGuardSkill consent-stop check) | STOP policy tests inherited from SB-001 |
| FR-012, SC-006, SC-007 | Phases C + F (telemetry instrumentation + SLA verification) | Load baseline report with P95/P99 ack metrics |
| FR-018, SC-010 | Phase C (HMAC-SHA256 validation in `MetaInstagramWebhookFunction`) | Invalid-signature rejection tests |
| FR-019, SC-011 | Phase C (best-effort mark-seen + typing-on before pipeline) | Latency tests confirming non-blocking behavior |
| FR-020 | Phase D (coordinator calls `IMessageDispatcher`) | Dispatch audit assertions for Instagram messages |
| FR-021 | Phase E (ConversationalResponseAgent Instagram branch + MetaInstagramMessenger) | End-to-end reply delivery tests |
| FR-022, SC-003 | Phase A + B (unique constraint + configuration guard) | Duplicate-registration rejection test |

## Project Structure

### Documentation (this feature)

```text
specs/013-instagram-capture/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── tasks.md
├── checklists/
│   └── requirements.md
└── contracts/
    └── instagram-inbound-contract.yaml
```

### Source Code

```text
db/
└── migrations/
    └── 022_instagram_channel_registrations.sql

src/
├── Aluki.Runtime.Abstractions/
│   └── Channels/
│       └── Instagram/
│           ├── InstagramInboundEnvelope.cs
│           ├── IInstagramMessenger.cs
│           └── InstagramChannelTypes.cs
├── Aluki.Runtime.Capture/
│   └── Channels/
│       └── Instagram/
│           ├── MetaInstagramWebhookMapper.cs
│           ├── NormalizeInstagramInboundSkill.cs
│           ├── InstagramCaptureCoordinator.cs
│           ├── InstagramPrincipalResolver.cs
│           └── InstagramChannelRegistrationRepository.cs
├── Aluki.Runtime.Conversation/
│   └── Agents/
│       └── ConversationalResponseAgent.cs  (extended, not replaced)
└── Aluki.Runtime.Functions/
    └── Functions/
        └── MetaInstagramWebhookFunction.cs

tests/
├── contract/
│   └── Instagram/
│       └── InstagramInboundContractTests.cs
├── integration/
│   └── Instagram/
│       ├── InstagramCapturePipelineTests.cs
│       ├── InstagramScopeIsolationTests.cs
│       └── InstagramRetryReliabilityTests.cs
└── unit/
    └── Instagram/
        ├── MetaInstagramWebhookMapperTests.cs
        ├── NormalizeInstagramInboundSkillTests.cs
        └── InstagramPrincipalResolverTests.cs
```

## Post-Design Constitution Re-Check

- Skill contract boundaries remain explicit; all new components follow the existing skill pipeline model: PASS.
- Tenant/context enforcement before side effects: IGSID resolver is the first gate, identical to WhatsApp principal resolver: PASS.
- Provenance and audit: `source_channel = 'instagram'`, `mid` as provider message ID, full audit event set: PASS.
- Session vs long-running workflow: mark-seen / typing-on are fire-and-forget; capture pipeline is durable with retry: PASS.
- Observability: telemetry and audit event set mandatory per FR-012/FR-016: PASS.

## Complexity Tracking

No constitution exceptions or complexity waivers required.
