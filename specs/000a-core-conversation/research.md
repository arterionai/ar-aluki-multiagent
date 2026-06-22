# Research: Core Conversational Response (SB-000)

## Existing infrastructure inventory

### LLM — IChatModelRouter
- Interface: `Aluki.Runtime.Abstractions` → `IChatModelRouter`
- Implementation: `Aluki.Runtime.Memory/Chat/ChatModelRouter.cs`
- Config: `Foundry:Endpoint / ApiKey / ChatDeployment`
- Already used by: `MemoryRecallService`, `EntityResolutionService`

### Memory recall — IMemoryRecallService
- Interface + implementation in `Aluki.Runtime.Memory/Recall/MemoryRecallService.cs`
- Returns grounded `RecallResult` with citations and corroboration score ≥2
- Input: `RecallQuery { TenantId, UserId, Query }`
- Already callable without HTTP roundtrip

### Inbound message store — captured_messages
- Table created by migration `001_whatsapp_capture.sql`
- Contains: `tenant_id`, `user_id`, `body`, `created_at`, `message_id`, `message_type`
- Used for conversation history assembly (no schema change needed)

### Domain agent dispatch — MessageDispatcher
- `Aluki.Runtime.Capture/Dispatch/MessageDispatcher.cs`
- Evaluates ALL registered `IDomainAgent` implementations in priority order
- Does NOT short-circuit after first claiming agent — both `ConversationalResponseAgent`
  and `MemoryDomainAgent` will run

### WhatsApp outbound — IWhatsAppMessenger
- Interface: `Aluki.Runtime.Capture/Channels/WhatsApp/IWhatsAppMessenger.cs`
- Current single method: `MarkReadAndShowTypingAsync(phoneNumberId, messageId)`
- `MetaWhatsAppMessenger` uses `/{phone_number_id}/messages` POST on Meta Graph API
- Same endpoint and auth token used for sending text messages (just different payload)

### Meta Graph API — send message payload
```json
{
  "messaging_product": "whatsapp",
  "to": "<recipient_wa_id>",
  "type": "text",
  "text": { "body": "<message text>" }
}
```
Endpoint: `POST /{phone_number_id}/messages`
Auth: `Authorization: Bearer <Meta:AccessToken>`

## Key design decisions

### Why not a new HTTP endpoint?
The response generation is triggered by the inbound webhook, which already exists at
`api/whatsapp POST`. Adding a new endpoint would require a separate caller; the domain
agent model handles this entirely within the inbound pipeline.

### Why priority 100 for ConversationalResponseAgent?
`MemoryDomainAgent` sits at `int.MaxValue` (lowest priority = last resort fallback).
Priority 100 ensures the response agent runs before the fallback without blocking other
domain agents that may claim higher-priority intents in the future (e.g., a billing
agent at priority 50 could preempt the conversational one).

### Why UNION of captured_messages + outbound_messages for history?
Conversation history must include both sides of the dialogue (user messages and Aluki
replies) to maintain coherent context. The UNION approach reuses existing tables without
duplicating data.

### LLM grounding contract
The system prompt MUST include an explicit instruction such as:
> "Answer only using information provided in the context below. If the context does not
> contain sufficient information to answer, say so explicitly. Do not invent facts."

This is enforceable at the prompt level independent of model behavior.
