# Quickstart: Core Conversational Response (SB-000)

## Prerequisites

- SB-001 (WhatsApp Capture) deployed — inbound webhook live.
- SB-002 (Personal Memory) deployed — at least one `memory_artifact` for the test user.
- Migration 020 applied (`outbound_messages` table exists).
- `Foundry:*` and `Meta:*` config wired in Functions app settings.

## Smoke test — grounded reply

1. Send a WhatsApp text message from the test number (+14252307522) with content
   that references a fact previously stored in memory.
2. Expect an Aluki reply within 10 seconds containing that fact and its source.
3. Verify `outbound_messages` contains one row with `status = delivered`.

## Smoke test — no memory

1. Send a message asking about a topic never mentioned before.
2. Expect an Aluki reply that honestly states no information is saved on that topic.
3. Verify no fabricated facts appear in the reply.

## Smoke test — audio acknowledgment

1. Send a WhatsApp voice note.
2. Expect an immediate Aluki acknowledgment ("Ahora escucho tu audio y te respondo"
   or similar configured phrasing).
3. Verify `outbound_messages` contains one row with `status = delivered`.

## Smoke test — error fallback

1. Temporarily configure an invalid `Foundry:ApiKey`.
2. Send any text message.
3. Expect the configured `ErrorFallbackMessage` to be sent to the user.
4. Verify `outbound_messages.status = error_fallback`.
5. Restore correct config.

## Validation notes

(To be filled after implementation run.)
