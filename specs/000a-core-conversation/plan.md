# Implementation Plan: Core Conversational Response (SB-000)

## Strategy

Build the minimum surface that closes the conversational loop: receive a WhatsApp
message, generate a grounded reply using existing memory infrastructure, and send
it back. No new AI models, no new pipelines — wire what already exists.

## Phase sequence

### Phase 1 — Contracts and interfaces (blocking prerequisite)

Before any implementation, the shared contracts must exist so all downstream layers
can compile independently.

1. Extend `UnifiedMessage` with two optional fields: `SenderExternalId` and
   `PhoneNumberId`. These are already available in the inbound webhook but not
   threaded through the normalization skill. Without them the agent cannot route
   the outbound reply.
2. Extend `WhatsAppInboundEnvelope` with `PhoneNumberId` and update
   `MetaWebhookMapper.Map()` to populate it from `value.metadata.phone_number_id`.
3. Update `NormalizeWhatsAppInboundSkill.BuildUnifiedMessage()` to propagate both
   new fields.
4. Extend `IWhatsAppMessenger` with `SendTextMessageAsync`. Implement in
   `MetaWhatsAppMessenger` (real) and `NullWhatsAppMessenger` (no-op).
5. Add abstraction interfaces: `IConversationHistoryStore`, `IOutboundMessageStore`,
   and shared models in `Aluki.Runtime.Abstractions/Conversation/`.

Rationale for ordering: `UnifiedMessage` changes ripple into the capture pipeline
and the agent interface, so they must land first.

### Phase 2 — Migration and persistence (blocking prerequisite)

1. Create `db/migrations/020_conversational_response.sql`:
   `outbound_messages` table with unique `(tenant_id, correlation_message_id)` for
   idempotency + RLS policy.
2. Add `020_conversational_response` to the CI deploy loop and to
   `DbCaptureFixture.cs` for integration tests.
3. Implement `ConversationHistoryStore` — queries `unified_message_artifact` UNION
   `outbound_messages`, ordered by `created_at DESC LIMIT N`.
4. Implement `OutboundMessageStore` — idempotent upsert using
   `INSERT … ON CONFLICT (tenant_id, correlation_message_id) DO NOTHING`.

### Phase 3-6 — User stories (can run in parallel after Phase 2)

Each story has its tests first, then its implementation:

- **US1** (text response): `ConversationalResponseAgent` assembles context, calls
  `MemoryRecallService` + conversation history in parallel, builds prompt via
  `ConversationPromptBuilder`, calls `IChatModelRouter` with 8s timeout, sends
  reply via `IWhatsAppMessenger.SendTextMessageAsync`, persists to `outbound_messages`.
  Also calls `IMemoryIngestionSink` since `MemoryDomainAgent` (int.MaxValue) won't
  fire when this agent (priority 100) claims the message.
- **US2** (no-memory): `ConversationPromptBuilder` detects empty/low-confidence
  recall and injects a grounding instruction that prevents fabrication.
- **US3** (audio ack): agent detects `MediaRefs` with `MediaKind = "audio"` and
  sends the configured `AudioAcknowledgmentMessage` immediately, skipping LLM call.
- **US4** (error fallback): LLM timeout or exception → send `ErrorFallbackMessage`,
  persist with `status = error_fallback`.

### Phase 7 — Polish

Update `tasks.md` checkboxes, `quickstart.md` validation notes, `CLAUDE.md`
delivery state, and produce `validation-report.md`.

## Key architectural constraints

| Constraint | Decision |
|-----------|---------|
| Dispatcher picks ONE agent | `ConversationalResponseAgent` must also call `IMemoryIngestionSink` for text |
| `PhoneNumberId` not in `UnifiedMessage` | Add as optional field; propagate from envelope |
| `MemoryRecallService` takes `PrincipalScope` | Construct from `PrincipalContext`: `new PrincipalScope(principal.TenantId, principal.ContextId, principal.UserId, principal.Roles)` |
| `IChatModelRouter` already registered by `AddPersonalMemory` | Inject directly; don't re-register |
| `NpgsqlConnectionFactory` already registered by `AddWhatsAppCapture` | Use `TryAddSingleton` in `AddConversationalResponse` |
| Migration numbering | SB-010 billing shifts from 020–026 to 027–033 |

## New project: `Aluki.Runtime.Conversation`

Mirrors the structure of `Aluki.Runtime.Reminders`. References:
- `Aluki.Runtime.Abstractions`
- `Aluki.Runtime.Capture` (for `NpgsqlConnectionFactory`, `IWhatsAppMessenger`)
- `Aluki.Runtime.Memory` (for `MemoryRecallService`, `IChatModelRouter`, `IMemoryIngestionSink`)

Registered via `AddConversationalResponse(IConfiguration)` extension method,
called in both `Host/Program.cs` and `Functions/Program.cs`.

## Risk: LLM latency

The 8-second timeout is aggressive for some queries. Recall + LLM can together
approach that budget. Mitigation: run history fetch and recall in parallel
(`Task.WhenAll`), minimizing the sequential portion to the LLM call only.

## Risk: `MemoryDomainAgent` displacement

By claiming priority 100, this agent displaces `MemoryDomainAgent` for all
messages. If `ConversationalResponseAgent` is not registered (e.g., in older
Host deployments without the new config), `MemoryDomainAgent` resumes naturally
as the catch-all. This preserves backward compatibility.
