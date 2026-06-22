# Feature Specification: Core Conversational Response

Feature ID: SB-000
Status: Draft
Date: 2026-06-22

## 1. Objective

Make Aluki respond to every inbound WhatsApp message with a contextually grounded reply,
using the user's personal memory and recent conversation history as the basis for
every answer. This is the feature that closes the loop: without it, Aluki captures
information but never talks back.

## Clarifications

### Session 2026-06-22

- Q: Should Aluki respond to every message or only to explicit questions? → A: Aluki responds to every inbound message without exception.
- Q: What context should the LLM use? → A: Recent conversation history (last ~10 messages from the same user) plus semantic memory recall (SB-002). Semantic graph enrichment (SB-011) is a follow-up.
- Q: What happens when no relevant memory is found? → A: Aluki responds honestly that no saved information was found for that topic, and invites the user to share it.
- Q: How are inbound audio messages handled? → A: Aluki sends a friendly acknowledgment ("Ahora escucho tu audio y te respondo") and queues the audio for transcription (SB-004 pipeline). The text response follows once transcription is complete. For this first iteration the acknowledgment is sent immediately; the post-transcription reply is a follow-up.
- Q: What happens if the LLM call fails? → A: Aluki sends a friendly error message to the user. The inbound capture (MemoryDomainAgent) already completed independently.
- Q: What number and placement for this spec? → A: SB-000 (foundational). Migration numbering starts at 020; SB-010 billing migrations shift to 027–033.

## 2. Architecture Adaptation

- Introduce a new project `Aluki.Runtime.Conversation` mirroring the pattern of
  `Aluki.Runtime.Memory`, `Aluki.Runtime.Extraction`, etc.
- Implement `ConversationalResponseAgent : IDomainAgent` (priority = 100, before
  `MemoryDomainAgent` which is `int.MaxValue`) that intercepts every inbound message,
  assembles context, calls the LLM, and sends the reply.
- `MemoryDomainAgent` remains the catch-all for memory ingestion and still runs
  independently (both agents fire; the dispatcher does not short-circuit after the first).
- Extend `IWhatsAppMessenger` with `SendTextMessageAsync(phoneNumberId, recipientWaId, text)`
  and implement it in `MetaWhatsAppMessenger` via the existing Meta Graph API client.
- Introduce `IConversationHistoryStore` to retrieve the last N captured messages for a
  given tenant+user, backed by the existing `captured_messages` table (no new data required
  for history reads).
- Introduce `outbound_messages` table (migration 020) to persist every Aluki-originated
  reply for deduplication, audit, and future retry logic.
- LLM calls go through the existing `IChatModelRouter` (Azure AI Foundry, config
  `Foundry:Endpoint/ApiKey/ChatDeployment`).

## 3. In Scope

- Text message response using memory recall + conversation history context.
- Honest no-memory response when recall returns no grounded evidence.
- Audio message acknowledgment (immediate friendly reply, transcription follow-up deferred).
- Friendly error message when LLM call fails.
- `SendTextMessageAsync` on `IWhatsAppMessenger` + `MetaWhatsAppMessenger`.
- `outbound_messages` persistence with idempotency key and delivery status.
- `ConversationalResponseAgent` registration as a domain agent at priority 100.
- System prompt design: Aluki's persona, tone, grounding rules, and citation style.

## 4. Out of Scope

- Post-transcription response for audio (follow-up after SB-004 async pipeline).
- Semantic graph enrichment in context assembly (follow-up after SB-011 stabilizes).
- Rich media replies (images, documents, buttons, lists).
- Multi-turn intent disambiguation flows (follow-up).
- Push-style proactive messages not triggered by an inbound message.
- Rate limiting or per-user response throttling (follow-up with SB-010 billing).

## 5. User Stories

### US1 — Text message receives a grounded reply (Priority: P0)

As a WhatsApp user, when I send any text message to Aluki, I receive a reply that
is grounded in my personal memory and recent conversation history.

Why this priority: This is the core product loop. Nothing else matters without it.

Independent Test: Send a text message referencing a fact previously stored in memory.
Verify Aluki's reply contains that fact with a citation, and that `outbound_messages`
records the delivery.

Acceptance Scenarios:

1. Given a user sends a text message, when `ConversationalResponseAgent` processes it,
   then a reply is sent via WhatsApp within the same inbound cycle.
2. Given memory recall returns grounded facts, when the LLM synthesizes the reply,
   then the reply references those facts and their sources.
3. Given two consecutive messages from the same user, when the second is processed,
   then recent conversation history (including the first message and Aluki's reply)
   is included in the LLM context.
4. Given an inbound message is redelivered (idempotent replay), when the agent
   processes it again, then no duplicate outbound message is sent.

### US2 — Honest no-memory response (Priority: P0)

As a WhatsApp user, when I ask Aluki about a topic it has no saved information on,
I receive an honest reply that acknowledges the gap rather than a fabricated answer.

Why this priority: Preventing hallucination is a hard product constraint.

Independent Test: Ask a question about a topic never mentioned in the user's memory.
Verify the reply explicitly states no saved information was found, and that no
fabricated facts appear.

Acceptance Scenarios:

1. Given recall returns zero grounded results for the query, when the LLM generates
   the reply, then the reply states that no relevant information is saved and
   invites the user to share it.
2. Given partial recall results below the corroboration threshold, when the LLM
   generates the reply, then only the grounded portion is surfaced; ungrounded
   speculation is not included.

### US3 — Audio message receives acknowledgment (Priority: P1)

As a WhatsApp user, when I send a voice message, I receive an immediate friendly
acknowledgment that Aluki will process the audio.

Why this priority: Silence after sending a voice note feels broken. The acknowledgment
keeps the UX intact while async transcription runs.

Independent Test: Send a WhatsApp audio message. Verify an acknowledgment reply
is sent immediately, the audio is queued for transcription via SB-004, and
`outbound_messages` records the acknowledgment delivery.

Acceptance Scenarios:

1. Given an inbound audio message, when `ConversationalResponseAgent` detects
   message type = audio, then a friendly acknowledgment is sent immediately.
2. Given an inbound audio message, when the agent handles it, then
   `MemoryDomainAgent` still ingests the audio artifact independently.

### US4 — Graceful error handling (Priority: P1)

As a WhatsApp user, when an unexpected error occurs during response generation,
I receive a friendly error message rather than silence.

Why this priority: Silence on failure is worse than a transparent error from a
trust perspective.

Independent Test: Simulate an LLM call failure. Verify the user receives a friendly
error message and that `outbound_messages` records the error-reply delivery with
`status = error_fallback`.

Acceptance Scenarios:

1. Given the LLM call throws or times out, when the agent handles the exception,
   then a predefined friendly error message is sent to the user via WhatsApp.
2. Given an error reply is sent, when `outbound_messages` is queried, then the
   record has `status = error_fallback` and `error_reason` populated.
3. Given an error occurs, when the agent logs it, then a structured error event
   is emitted with correlation metadata (tenant, user, message_id, error type).

## 6. Non-Functional Requirements

- **Latency**: Reply must be sent within 10 seconds of inbound message receipt
  under normal load. LLM timeout is 8 seconds; error fallback message is sent
  if exceeded.
- **Grounding**: The LLM system prompt MUST instruct the model to answer only from
  provided context. The model MUST NOT fabricate facts not present in recall results
  or conversation history.
- **Idempotency**: Outbound messages are deduplicated by `(tenant_id, correlation_message_id)`
  — replayed inbound events never produce duplicate replies.
- **Failure isolation**: LLM failure must not affect `MemoryDomainAgent` ingestion.
  Both agents operate independently within the same dispatch cycle.
- **Tenant isolation**: Context assembly, recall, and outbound delivery are always
  scoped to the originating tenant and user. Cross-tenant data access is not possible.

## 7. Data Model

### outbound_messages (migration 020)

| Column | Type | Notes |
|--------|------|-------|
| id | uuid PK | `gen_random_uuid()` |
| tenant_id | uuid NOT NULL | FK → tenants, RLS anchor |
| user_id | uuid NOT NULL | FK → users |
| correlation_message_id | text NOT NULL | inbound message_id that triggered this reply |
| channel | text NOT NULL | `whatsapp` (extensible) |
| recipient_wa_id | text NOT NULL | destination phone number |
| body | text NOT NULL | sent message text |
| status | text NOT NULL | `delivered`, `error_fallback`, `pending` |
| error_reason | text | populated when status = error_fallback |
| created_at | timestamptz NOT NULL | `now()` |
| delivered_at | timestamptz | set on Graph API 200 |
| UNIQUE | (tenant_id, correlation_message_id) | idempotency constraint |

RLS: `tenant_id = app.current_tenant()`.

## 8. HTTP Endpoints

No new public HTTP endpoints. Response generation is triggered entirely by the
inbound WhatsApp webhook (`api/whatsapp` POST), which already exists.

## 9. Configuration

No new config section. Uses:
- `Foundry:Endpoint / ApiKey / ChatDeployment` — LLM calls (existing)
- `Meta:AccessToken / GraphBaseUrl` — outbound WhatsApp send (existing)
- `Postgres:AppConnection` — outbound_messages persistence (existing)

Optional tuning (with defaults, added to `appsettings.json`):

```json
"Conversation": {
  "HistoryWindowSize": 10,
  "LlmTimeoutSeconds": 8,
  "ErrorFallbackMessage": "Tuve un problema procesando tu mensaje, inténtalo de nuevo 🙏"
}
```

## 10. Implementation Split

| Layer | Location |
|-------|----------|
| Contracts / interfaces | `Aluki.Runtime.Abstractions/Conversation/` |
| `IConversationHistoryStore` | `Aluki.Runtime.Abstractions/Conversation/IConversationHistoryStore.cs` |
| `IOutboundMessageStore` | `Aluki.Runtime.Abstractions/Conversation/IOutboundMessageStore.cs` |
| `ConversationalResponseAgent` | `Aluki.Runtime.Conversation/ConversationalResponseAgent.cs` |
| `ConversationHistoryStore` | `Aluki.Runtime.Conversation/ConversationHistoryStore.cs` |
| `OutboundMessageStore` | `Aluki.Runtime.Conversation/OutboundMessageStore.cs` |
| `SendTextMessageAsync` extension | `Aluki.Runtime.Capture/Channels/WhatsApp/` (extends existing) |
| `MetaWhatsAppMessenger` update | `Aluki.Runtime.Functions/` (wiring) |
| Migration | `db/migrations/020_conversational_response.sql` |
| Unit tests | `tests/Aluki.Runtime.Tests/Conversation/` |
| Contract tests | `tests/Aluki.Runtime.Contracts.Tests/ConversationContractTests.cs` |
| Integration tests | `tests/Aluki.Runtime.IntegrationTests/Conversation/` |

## 11. Acceptance Criteria Summary

- FR-001: Every inbound text message produces exactly one outbound reply.
- FR-002: Replies are grounded in memory recall and/or conversation history; no fabrication.
- FR-003: When recall finds nothing, the reply explicitly states so — no hallucination.
- FR-004: Audio messages receive an immediate acknowledgment reply.
- FR-005: LLM failure produces a friendly error message, not silence.
- FR-006: Duplicate inbound delivery never produces a duplicate outbound message.
- FR-007: `outbound_messages` records every reply with status and correlation metadata.
- FR-008: LLM is invoked with a grounding system prompt that prohibits fabrication.
- FR-009: Reply latency is under 10 seconds at p95 under normal load.
- FR-010: `ConversationalResponseAgent` and `MemoryDomainAgent` are independent — a
  failure in one does not affect the other.
