# Research: Instagram Channel Capture

## Decision 1: Reuse all SB-001 capture tables; no new migrations for capture artifacts
- Decision: `unified_message_artifact`, `media_artifact`, `idempotency_record`, and `capture_audit_event` are reused with `source_channel = 'instagram'`. One new table `instagram_channel_registrations` is required for sender identity resolution.
- Rationale: The canonical idempotency key `(tenant_id, source_channel, provider_message_id)` already supports multiple channels. Separate tables would duplicate schema, split audit queries, and provide no isolation benefit given RLS already enforces tenant scope.
- Alternatives considered:
  - Separate `instagram_message_artifact` table: rejected — duplicates schema, complicates cross-channel queries and audit tooling.
  - Shared `channel_registrations` table for all channels: deferred — WhatsApp uses phone number membership differently; a channel-specific table keeps constraints and indexes clean for this feature.

## Decision 2: New `MetaInstagramWebhookFunction` at route `api/instagram`; no changes to WhatsApp function
- Decision: Introduce a parallel Azure Function HTTP trigger for Instagram. The same `IWhatsAppCaptureCoordinator` pattern is mirrored as `IInstagramCaptureCoordinator`.
- Rationale: Instagram and WhatsApp share the Meta webhook structure at the transport level but differ in payload shape (`object: "instagram"` with `entry[].messaging[]` vs `object: "whatsapp_business_account"` with `entry[].changes[].value.messages[]`). A separate function keeps routing explicit and each channel independently deployable.
- Alternatives considered:
  - Single unified Meta webhook handler with object-type routing: rejected — mixes payload schemas in one function, increases risk of regressions in the deployed WhatsApp path.
  - Reuse `MetaWhatsAppWebhookFunction` with conditional branching: rejected — same risk plus naming confusion.

## Decision 3: `MetaInstagramWebhookMapper` produces `InstagramInboundEnvelope`; mirrors `WhatsAppInboundEnvelope`
- Decision: Define a strongly typed `InstagramInboundEnvelope` with IGSID-based sender identity and Instagram-specific attachment model. Map to this type in the mapper, then normalize to `UnifiedMessage` in `NormalizeInstagramInboundSkill`.
- Rationale: Keeps Instagram-specific field names (IGSID, `mid`, `ig_business_account_id`) explicit in the ingress layer and prevents leaking Instagram concepts into the channel-agnostic `UnifiedMessage`.
- Alternatives considered:
  - Map directly to `WhatsAppInboundEnvelope`: rejected — field semantics differ (IGSID vs phone number, account ID vs phone_number_id, `mid` vs `wamid`), causing confusion and potential misrouting.

## Decision 4: `ChannelType.Instagram = "instagram"` added to shared constants; no other `UnifiedMessage` contract changes
- Decision: Add one constant. `PhoneNumberId` field in `UnifiedMessage` is reused as the channel account identifier (Instagram Business Account ID for outbound routing); no rename to avoid breaking existing WhatsApp paths.
- Rationale: `ConversationalResponseAgent` uses `PhoneNumberId` to select the outbound messenger; for Instagram, this field carries the Instagram Business Account ID. Renaming to `ChannelAccountId` is a broader refactor out of scope.
- Alternatives considered:
  - Add a separate `ChannelAccountId` field: deferred — valid cleanup but is a cross-cutting refactor that risks WhatsApp regressions; defer to a future channel-normalisation pass.

## Decision 5: `IInstagramMessenger` for outbound; mirrors `IWhatsAppMessenger`
- Decision: Introduce `IInstagramMessenger` with `SendTextMessageAsync(recipientIgsid, igAccountId, text, ct)` and `SendSeenAndTypingAsync(recipientIgsid, igAccountId, ct)`. Implementation `MetaInstagramMessenger` calls `POST /{ig_account_id}/messages` on Graph API `v21.0`.
- Rationale: Same Graph API base URL and access token pattern but different endpoint path. Keeping a separate interface avoids branching inside `MetaWhatsAppMessenger` and allows independent stubbing/testing.
- Alternatives considered:
  - Extend `IWhatsAppMessenger` with Instagram overloads: rejected — violates interface segregation; Instagram and WhatsApp callers should not share a messenger contract.
  - Single `IMetaMessenger` with a channel discriminator: deferred — premature unification before a third channel exists.

## Decision 6: `instagram_channel_registrations` for IGSID → tenant/user mapping
- Decision: New table `instagram_channel_registrations(registration_id, tenant_id, context_id, user_id, igsid, ig_account_id, registered_at_utc, status)` with unique index on `(igsid, ig_account_id)` and RLS on `tenant_id`. Status values: `active`, `revoked`.
- Rationale: WhatsApp uses phone number membership (resolved by `wa_id`). Instagram introduces IGSID which is scoped to the Instagram Business Account (IGSIDs are not portable across apps). A dedicated table makes this relationship explicit and allows tenant-admin management.
- Alternatives considered:
  - Extend `principal_memberships` with an `instagram_igsid` column: rejected — `principal_memberships` is keyed on channel/phone, mixing identity models creates query complexity and RLS ambiguity.

## Decision 7: `ConversationalResponseAgent` extended to dispatch outbound replies via `IInstagramMessenger`
- Decision: Extend `ConversationalResponseAgent.HandleAsync` to check `message.ChannelType` and dispatch via `IInstagramMessenger` for `"instagram"`, continuing to use `IWhatsAppMessenger` for `"whatsapp"`. No new agent required.
- Rationale: The agent is channel-agnostic for input; only the outbound dispatch is channel-specific. Adding a separate `InstagramConversationalResponseAgent` would duplicate all LLM, history, and memory logic.
- Alternatives considered:
  - New `InstagramConversationalResponseAgent`: rejected — code duplication with identical business logic.
  - Abstract outbound messenger via `IChannelMessenger` with factory: deferred — valid abstraction for a third channel; two channels do not justify the added indirection yet.

## Decision 8: Webhook signature validation reuses same HMAC-SHA256 utility; configurable app secret
- Decision: Extract or reuse the HMAC-SHA256 validation utility from `MetaWhatsAppWebhookFunction`; configure via `Instagram:AppSecret` (falls back to `Meta__AppSecret` if the same Meta App is used for both channels).
- Rationale: The signature scheme is identical across Meta webhook products. A shared utility prevents two implementations diverging.
- Alternatives considered:
  - Inline signature check in the new function: rejected — duplicates security-critical code.

## Best-Practice Notes Applied
- Architecture mirrors SB-001: separate webhook function → mapper → normalize skill → coordinator → dispatcher.
- No domain agent changes needed beyond the outbound dispatch extension in `ConversationalResponseAgent`.
- Deployment follows the same Functions isolated worker pattern; `AddInstagramCapture()` DI extension wires all new components.
- Media download reuses `MetaMediaClient` since Instagram CDN URLs are also fetched via Graph API with the same access token.
- All new tables include `tenant_id` RLS with existing `app.current_tenant` GUC pattern.
