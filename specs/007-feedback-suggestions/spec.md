# Feature Specification: Feedback Suggestions Capture (Starter Baseline)

Feature ID: SB-007
Status: Draft
Date: 2026-06-21

## 1. Objective

Capture product suggestions as a dedicated artifact type, separate from normal memory recall.

## 2. Architecture Adaptation

- FeedbackAgent handles suggestion-intent detection and suggestion lifecycle.
- Suggestion records persist in isolated suggestion domain tables.
- AttachmentStoreSkill links text, audio, and image follow-up artifacts to active suggestion windows.
- AuditLogSkill records transitions without exposing full content in telemetry.

## 3. In Scope

- Suggestion intent capture from text.
- Follow-up attachment capture.
- Welcome guidance that explains suggestion usage.

## 4. Out of Scope

- Public voting by users.
- Automatic publication workflows.

## 5. User Stories

### US1 - Save suggestion separately (P1)
Given user sends clear suggestion message, when processed, then suggestion is stored in separate domain and user receives confirmation.

### US2 - Add context via audio/photo (P2)
Given active suggestion window, when user sends attachment, then it is linked to same suggestion.

### US3 - Discoverability in welcome (P3)
Given first interaction, when welcome is sent, then user is told how to submit suggestions including media context.

## 6. Acceptance Criteria

- Suggestions do not appear in normal recall result sets.
- Follow-up window expiration starts a new suggestion artifact.
- Duplicate webhook deliveries remain idempotent.

## 7. Payload & Context Handling

### 7.1 Text/Audio/Photo Payload Handling

**Storage Model**: Payloads are stored by reference (Azure Blob Storage) with inline metadata only.

- **Text payloads**: Stored inline in suggestion record (up to 5 KB); larger payloads referenced via blob URI.
- **Audio payloads**: Stored in Azure Blob Storage; max 50 MB per file, expires after 90 days unless explicitly retained.
- **Photo payloads**: JPEG/PNG only, up to 10 MB per file, expires after 90 days.
- **Attachment metadata**: SHA-256 hash, MIME type, upload timestamp, and content URI retained for lifecycle tracking.
- **Multi-attachment**: Single suggestion may reference up to 10 attachments total (text, audio, photo combined).

### 7.2 Context Linking Boundaries

**Active Suggestion Window**: 30 minutes from initial suggestion capture.

- Any attachment (audio, photo, or text clarification) sent within the 30-minute window is linked to the same suggestion artifact with `linked_at` timestamp.
- Only one suggestion window can be active per tenant-user pair at a time.
- If a new suggestion intent is detected while a window is active, the active window closes and a new one begins.
- Window expiration is automatic; explicit user actions (e.g., "done with this suggestion") do not reset the window.
- Context linking is tenant-scoped; cross-tenant context is not linked.

## 8. Duplicate Detection & Idempotency

### 8.1 Duplicate Follow-up Detection

Duplicates are detected by **message ID + media hash** composite key at inbound webhook layer.

- **Duplicate definition**: Same WhatsApp message ID arriving within 5 minutes with identical SHA-256 payload hash.
- **Duplicate behavior**: Logged as ignored event in audit trail; no new artifact created; existing suggestion remains unchanged.
- **Recovery**: System tracks last processed message ID per tenant-user; idempotency window is 5 minutes (after which re-delivery is treated as a new follow-up).
- **Cross-channel**: Audio/photo duplicates detected at content hash level; text duplicates detected at message ID level.

## 9. Suggestion Lifecycle & Visibility

### 9.1 Lifecycle States

Suggestions transition through exactly four observable states:

1. **`captured`** – Initial intent detected; payload(s) received but not yet validated or enriched.
2. **`enriched`** – All linked context (attachments, metadata) collected; waiting for delivery signal.
3. **`sent_user`** – Confirmation sent to user that suggestion was received; no further enrichment accepted.
4. **`archived`** – 90 days post-`sent_user` state; no further state transitions.

### 9.2 Lifecycle Visibility & Exposure Rules

- **To end user**: Only `sent_user` state is visible; user sees confirmation message. Earlier states hidden.
- **To FeedbackAgent**: All states visible; agent orchestrates transitions.
- **To audit log**: All state transitions logged with timestamp, actor (FeedbackAgent), prior state, new state, and reason.
- **To recall system**: Suggestions only excluded from recall after reaching `captured` state (i.e., they never appear in normal memory results).
- **To administrative dashboards**: All states visible with audit trail; state duration metrics tracked.

### 9.3 State Transition Rules

- `captured` → `enriched`: Triggered after 30-minute window closes or user explicitly closes context.
- `enriched` → `sent_user`: Triggered when confirmation is delivered to user.
- `sent_user` → `archived`: Automatic, 90 days post-transition.
- No reverse transitions allowed (one-way lifecycle).

## 10. Clarifications

### Session 2026-06-21

- Q: How should text/audio/photo payloads be stored and sized? → A: Reference-based storage (Azure Blob) with 5 KB inline text limit, 50 MB audio max, 10 MB photos (JPEG/PNG), 90-day expiry unless retained.
- Q: What defines context linking boundaries and window duration? → A: 30-minute active window per tenant-user pair, one active window at a time, automatic expiry with no reset on user action.
- Q: How are duplicate follow-ups detected and handled? → A: Composite key (message ID + SHA-256 hash) at inbound layer, 5-minute idempotency window, logged as ignored event on duplicate.
- Q: What is the suggestion lifecycle and who sees which states? → A: Four states (captured → enriched → sent_user → archived), end-users see only sent_user, all states visible to FeedbackAgent and audit logs, one-way transitions.
