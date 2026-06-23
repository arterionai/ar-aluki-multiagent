# Feature Specification: Instagram Channel Capture

**Feature Branch**: `013-instagram-capture`

**Created**: 2026-06-23

**Status**: Draft

**Input**: Product decision to extend the capture platform with a second inbound channel (Instagram Direct Messages) using the same Meta infrastructure as SB-001 WhatsApp Capture.

## Clarifications

### Session 2026-06-23

- Q: Should Instagram capture reuse the existing capture tables (`unified_message_artifact`, `media_artifact`, `idempotency_record`, `capture_audit_event`) or introduce a separate schema? -> A: Reuse all existing capture tables with `source_channel = 'instagram'`; no new capture tables are required. The canonical idempotency key `(tenant_id, source_channel, provider_message_id)` already accommodates multiple channels.
- Q: How is an Instagram sender (IGSID) resolved to a tenant and user? -> A: A registered Instagram Business Account is associated with one tenant at configuration time. The sender's IGSID is matched to an existing user record via the `instagram_channel_registrations` table (new, this feature); unresolvable senders are denied and audited.
- Q: Which Instagram message types must be supported and which are unsupported? -> A: Supported: text DMs, image attachments, audio attachments. Unsupported (accepted-but-unsupported fallback): video, sticker, file, reaction events, story mentions, story replies, echo messages (sent by the page), referral events.
- Q: Does the existing `IMessageDispatcher` and `ConversationalResponseAgent` require changes to handle Instagram? -> A: `IMessageDispatcher` and all `IDomainAgent` implementations are channel-agnostic via `UnifiedMessage`. `ConversationalResponseAgent` must be extended to send outbound text replies over Instagram (via `IInstagramMessenger`) in addition to WhatsApp. The `ChannelType.Instagram` constant must be added.
- Q: How are outbound replies sent to Instagram users? -> A: Via a new `IInstagramMessenger` that calls `POST /{ig_business_account_id}/messages` on the Meta Graph API with the sender's IGSID as recipient; same access token mechanism as WhatsApp but different endpoint path. 401/403 errors surface as a structured `reconnect_required` outcome.
- Q: Is the webhook signature validation the same as WhatsApp? -> A: Yes, same HMAC-SHA256 scheme against `X-Hub-Signature-256`. The app secret may be the same Meta app as WhatsApp or a separate app configured via `Instagram:AppSecret`. If the same app is used, the same `Meta__AppSecret` applies.
- Q: What is the mark-seen and typing indicator behavior? -> A: On every valid inbound DM the system sends `sender_action: "mark_seen"` immediately, then `sender_action: "typing_on"` (typing bubble, auto-dismisses after ~20s). Both calls are best-effort and non-blocking, identical in principle to WhatsApp read receipt + typing indicator.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Capture Instagram DM text and media messages reliably (Priority: P1)

As an Instagram Direct Message user, I need my messages sent to the Aluki Business Account to be captured consistently so that my information is not lost and can be used by the same memory, calendar, and conversational features available on WhatsApp.

**Why this priority**: Instagram is the second inbound channel; without reliable capture, no downstream product feature works for Instagram users.

**Independent Test**: Can be fully tested by posting valid Instagram DM payloads (text, image, audio, unsupported) and verifying each is captured exactly once with required metadata.

**Acceptance Scenarios**:

1. **Given** a valid inbound Instagram DM text message from a registered sender, **When** the capture flow runs, **Then** exactly one normalized message record is persisted with `source_channel = 'instagram'`, sender IGSID, required provenance fields, and an acknowledgment is returned.
2. **Given** a valid inbound Instagram DM with an image or audio attachment, **When** the capture flow runs, **Then** exactly one message record and one associated media artifact are persisted with content type and provenance.
3. **Given** the same inbound DM delivery is retried by Meta, **When** capture executes again, **Then** no duplicate persisted message is created and processing is marked as duplicate-safe.
4. **Given** an inbound message of unsupported type (video, sticker, reaction, story mention), **When** the capture flow runs, **Then** a minimal artifact with `message_kind = 'unsupported'` is persisted without breaking the session and without triggering a domain agent response that implies the content was understood.

---

### User Story 2 - Enforce sender identity and tenant isolation for Instagram messages (Priority: P1)

As a tenant owner, I need Instagram messages to be associated with the correct tenant and user account so that data remains isolated and no unauthorized reads or writes occur.

**Why this priority**: Multi-tenant security is non-negotiable; Instagram introduces a new sender identity type (IGSID) that must be resolved before any side effect.

**Independent Test**: Can be fully tested by sending messages from registered and unregistered IGSIDs across two tenants and verifying isolation and denial behavior.

**Acceptance Scenarios**:

1. **Given** an Instagram DM from a sender whose IGSID is registered in tenant A, **When** capture runs, **Then** the artifact is scoped to tenant A only and is inaccessible from tenant B.
2. **Given** an Instagram DM from a sender whose IGSID is not registered in any tenant, **When** capture runs, **Then** no artifact is persisted, a `capture.scope_denied` audit event is emitted, and the webhook is acknowledged 200 (Meta must not retry).
3. **Given** an inbound POST with an invalid `X-Hub-Signature-256` header, **When** the webhook function processes it, **Then** the request is rejected without any capture side effect and no audit event is created for the message itself.

---

### User Story 3 - Deliver conversational AI responses over Instagram DMs (Priority: P1)

As an Instagram DM user, I need to receive AI-generated text replies directly in my Instagram DM thread so that the conversational experience matches what WhatsApp users receive.

**Why this priority**: Without outbound replies, Instagram capture is a one-way sink and delivers no user value.

**Independent Test**: Can be tested end-to-end by sending a DM and verifying a text reply appears in the Instagram thread within the response window.

**Acceptance Scenarios**:

1. **Given** a captured Instagram DM text message that is dispatched to `ConversationalResponseAgent`, **When** the agent generates a reply, **Then** the reply is delivered to the sender's IGSID via the Instagram messages API and the outbound record is persisted.
2. **Given** a captured Instagram DM audio message, **When** the capture flow completes, **Then** an audio acknowledgment message is sent immediately over Instagram without invoking the LLM, matching the WhatsApp audio acknowledgment pattern.
3. **Given** an Instagram Graph API call that returns 401 or 403, **When** the messenger attempts to send, **Then** a structured `reconnect_required` outcome is recorded and the failure does not cause a capture pipeline failure.

---

### User Story 4 - Acknowledge Instagram messages with read receipt and typing indicator (Priority: P2)

As an Instagram DM user, I need to see that my message was read and that a reply is being typed so that I know the system received my message.

**Why this priority**: UX parity with WhatsApp; lack of read confirmation degrades trust in the channel.

**Independent Test**: Can be tested by sending a DM and verifying the Instagram app shows the "Seen" state and a typing indicator.

**Acceptance Scenarios**:

1. **Given** any valid inbound Instagram DM, **When** the webhook function processes it, **Then** a `sender_action: "mark_seen"` call is made to the Meta Graph API before any capture pipeline logic, and a `sender_action: "typing_on"` call is made immediately after.
2. **Given** the mark-seen or typing-on API call fails transiently, **When** the call is attempted, **Then** the failure is logged but does not block or fail the capture pipeline.

---

### User Story 5 - Preserve operational reliability and traceability for Instagram messages (Priority: P2)

As an operations owner, I need failures and retries in Instagram capture to be visible and traceable so that no message is silently dropped.

**Why this priority**: Identical to WhatsApp reliability requirement; operational visibility is required before production deployment.

**Independent Test**: Can be tested by injecting transient and permanent failures and verifying retry behavior, failure outcomes, and audit visibility.

**Acceptance Scenarios**:

1. **Given** a transient failure during capture persistence, **When** retryable processing runs, **Then** the message is eventually persisted once and telemetry includes retry attempts.
2. **Given** a permanent processing failure, **When** retries are exhausted, **Then** failure is recorded with reason and correlation identifiers, and no silent success is reported.

---

### Edge Cases

- Duplicate DM deliveries from Meta (same `mid` retried after network timeout).
- Messages from an IGSID not yet registered in any tenant.
- `X-Hub-Signature-256` header absent or malformed.
- Inbound payload with `object: "instagram"` but empty or missing `messaging` array.
- Unsupported attachment types: video, sticker, file, fallback, reaction events, story mentions, story replies.
- Echo messages (sent by the page itself) that appear in the webhook stream.
- Mark-seen or typing-on call fails while capture pipeline succeeds.
- Instagram Graph API 401/403 on outbound reply.
- Very large image attachments where CDN URL download is deferred.
- Consent-stop state active for the resolved user.
- Multiple IGSIDs registered to the same user in the same tenant.
- Instagram Business Account registered with more than one tenant (configuration error).

## Requirements *(mandatory)*

### Scope Boundaries

- This feature covers inbound Instagram Direct Message capture as the second production channel.
- This feature includes webhook signature validation, sender identity resolution, message normalization (text, image, audio), media metadata capture, deduplication, outbound text reply delivery, mark-seen and typing indicator signals, and mandatory audit/telemetry.
- This feature does not include Instagram comment capture, Instagram Stories creation, Instagram Shopping, post analytics, or any channel beyond Instagram DMs.
- This feature does not include changes to domain agents other than extending `ConversationalResponseAgent` to dispatch outbound replies over Instagram.

### Actors

- Instagram DM User: Sends inbound DMs and receives text replies from the Aluki Business Account.
- Tenant Administrator: Configures the Instagram Business Account registration for their tenant.
- Operations Owner: Monitors reliability, retries, failures, and audit evidence for the Instagram channel.
- Compliance Auditor: Verifies security controls, sender identity provenance, and audit trail for all channel side effects.

### Functional Requirements

- **FR-001**: System MUST accept valid inbound Instagram DM text, image, and audio events and normalize them into a `UnifiedMessage` with `ChannelType = "instagram"`.
- **FR-002**: System MUST persist one canonical capture record per unique inbound DM and preserve immutable provenance to the source message identifier (`mid`).
- **FR-003**: System MUST persist required identity and scope attributes on each captured artifact: tenant identifier, context identifier, initiating user identifier, and `source_channel = 'instagram'`.
- **FR-004**: System MUST enforce idempotent processing so repeated Meta deliveries of the same DM (same `mid`) do not produce duplicate capture records.
- **FR-005**: System MUST enforce tenant and principal scope checks before any capture read/write side effect is executed.
- **FR-006**: System MUST reject capture operations that lack a valid resolved sender identity (unregistered IGSID) and MUST emit a `capture.scope_denied` audit event.
- **FR-007**: System MUST return HTTP 200 to Meta for all inbound POSTs regardless of downstream outcome so that Meta does not retry; internal outcomes are tracked via audit and telemetry.
- **FR-008**: System MUST record audit evidence for each capture side effect, denial, and terminal failure with correlation identifiers sufficient for traceability.
- **FR-009**: System MUST process transient persistence failures using retry-safe behavior and MUST ensure eventual single-record persistence or terminal audited failure.
- **FR-010**: System MUST classify unsupported inbound content (video, sticker, file, reaction, story mention, story reply, echo) into a controlled `accepted-but-unsupported` outcome without breaking session continuity.
- **FR-011**: System MUST respect active consent-stop states by enforcing policy-defined behavior before capture side effects proceed.
- **FR-012**: System MUST emit telemetry for each critical capture stage including latency, result status, and failure category.
- **FR-013**: System MUST treat `(tenant_id, 'instagram', mid)` as the canonical idempotency key and MUST suppress duplicate deliveries by returning duplicate-safe acknowledgment without creating or mutating canonical message or media artifacts.
- **FR-014**: System MUST derive and validate `PrincipalContext` before side effects by resolving the sending IGSID to a registered tenant user; unresolved or unauthorized IGSID MUST be denied and audited.
- **FR-015**: System MUST handle unsupported payloads with accepted-but-unsupported fallback behavior that persists a minimal artifact containing unsupported classification, raw envelope reference, tenant/context scope, and provenance metadata.
- **FR-016**: System MUST emit the mandatory audit event set: `capture.accepted`, `capture.duplicate_suppressed`, `capture.scope_denied`, `capture.unsupported_payload`, `capture.retry_scheduled`, and `capture.failed_terminal`.
- **FR-017**: System MUST bound transient retry behavior to a maximum of 5 attempts with bounded exponential backoff and MUST produce a terminal audited failure outcome when retry budget is exhausted.
- **FR-018**: System MUST validate the `X-Hub-Signature-256` HMAC-SHA256 webhook signature on every inbound POST and MUST reject invalid signatures without any capture side effect.
- **FR-019**: System MUST send `sender_action: "mark_seen"` and `sender_action: "typing_on"` to the Instagram sender immediately after a valid inbound DM is received, before the capture pipeline processes the message; both calls MUST be best-effort and non-blocking.
- **FR-020**: System MUST route the normalized `UnifiedMessage` through `IMessageDispatcher` so all domain agents (memory, calendar, conversational response) operate on Instagram messages without modification.
- **FR-021**: System MUST send outbound text replies to Instagram senders via `IInstagramMessenger` when `ConversationalResponseAgent` produces a response; 401/403 from the Graph API MUST surface as a structured `reconnect_required` outcome without failing the capture pipeline.
- **FR-022**: System MUST register the Instagram Business Account to exactly one tenant; an account registered to more than one tenant MUST be rejected at configuration time and audited.

### Non-Goals

- Capturing Instagram comments, mentions, or posts.
- Ingesting Instagram Stories or Reels content beyond story-mention events (which are unsupported-fallback).
- Implementing Instagram-specific domain agents (calendar detection, reminders, etc.) beyond what the existing channel-agnostic agents already provide.
- Providing a user-facing consent flow for end users to connect their own Instagram accounts (as opposed to a business account).
- Outbound media (image/audio) sending; text-only replies are in scope.
- Deep media understanding or transcription for Instagram audio messages; these are captured as media artifacts and handled by downstream extraction features.

### Dependencies

- SB-001 WhatsApp Capture infrastructure (capture tables, idempotency, audit, telemetry, retry, scope guard, `UnifiedMessage`, `IMessageDispatcher`) is deployed and operational.
- SB-000 Core Conversational Response (`ConversationalResponseAgent`, `IWhatsAppMessenger`/`IInstagramMessenger`) provides outbound reply delivery.
- Meta Graph API access with a verified Instagram Business Account and a published Facebook Page.
- `instagram_channel_registrations` table (new, this feature) maps IGSID to tenant/user.
- Observability pipeline (App Insights / OpenTelemetry) is available for capture telemetry and failure investigation.

### Key Entities *(include if feature involves data)*

- **Instagram Inbound Envelope**: Provider-originated DM event containing sender IGSID, recipient account ID, message identifier (`mid`), timestamp, content type, and attachment metadata.
- **Instagram Channel Registration**: Tenant-scoped record mapping an Instagram sender IGSID to a registered user and context; controls whether inbound messages from that IGSID are accepted.
- **Unified Message Artifact**: Reused from SB-001; normalized, tenant-scoped record with `source_channel = 'instagram'`, message kind, text, and media provenance.
- **Media Artifact**: Reused from SB-001; image/audio metadata linked to a unified message artifact; media download URL from Instagram CDN.
- **Idempotency Record**: Reused from SB-001; keyed on `(tenant_id, 'instagram', mid)`.
- **Audit Event**: Reused from SB-001; immutable record for every capture side effect, denial, retry, or failure on the Instagram channel.
- **Outbound Message Record**: Reused from SB-000 `outbound_messages` table; persists Instagram replies with `channel_type = 'instagram'` and correlation to the inbound `mid`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 99.5% of valid inbound Instagram DMs from registered senders are captured as a single canonical record within 60 seconds of receipt.
- **SC-002**: Duplicate Meta redeliveries produce zero additional canonical message artifacts in 100% of tested retry scenarios.
- **SC-003**: 100% of persisted capture artifacts include required scope, provenance, and `source_channel = 'instagram'` fields.
- **SC-004**: 100% of denied capture attempts (unregistered IGSID, invalid scope) produce an auditable `capture.scope_denied` record.
- **SC-005**: 100% of terminal capture failures are observable with failure reason and correlation identifiers; silent-loss incidents remain at 0.
- **SC-006**: P95 end-user acknowledgment time (mark-seen + typing-on + HTTP 200 to Meta) for valid inbound DMs is 2 seconds or less under baseline load.
- **SC-007**: P99 end-user acknowledgment time for valid inbound DMs is 3 seconds or less under baseline load.
- **SC-008**: 100% of duplicate-delivery tests using identical `(tenant_id, 'instagram', mid)` produce zero additional canonical artifacts and emit `capture.duplicate_suppressed`.
- **SC-009**: 100% of retry-exhausted transient-failure test cases stop after at most 5 attempts and emit `capture.failed_terminal` with correlation and scope identifiers.
- **SC-010**: 100% of inbound POSTs with absent, malformed, or mismatched `X-Hub-Signature-256` are rejected without any capture artifact being created.
- **SC-011**: Mark-seen and typing-on API calls complete (success or non-blocking failure) within the acknowledgment window without delaying the HTTP 200 response to Meta by more than 200ms.

## Assumptions

- Meta Instagram webhooks use the same HMAC-SHA256 signature scheme as WhatsApp; the same or a separate app secret is configurable.
- The Instagram Business Account is a single account per tenant; users interacting via Instagram DMs are resolved by IGSID.
- Domain agents (memory ingestion, calendar detection, conversational response) operate correctly on `UnifiedMessage` with `ChannelType = "instagram"` without modification beyond the outbound reply extension in `ConversationalResponseAgent`.
- The Instagram Graph API messages endpoint is available and stable for the `v21.0` API version already in use.
- Audit and telemetry sinks are channel-agnostic and already operational from SB-001 deployment.
- End-to-end testing requires a live Instagram Business Account connected to a Meta App with the `instagram` webhook topic subscribed.
