# Data Model: WhatsApp Capture Foundation

## Entity: inbound_message_event
Purpose: Raw provider envelope metadata for traceable capture intake.

Fields:
- event_id (uuid, PK)
- tenant_id (uuid, required)
- context_id (uuid, required)
- source_channel (text, required, expected `whatsapp`)
- provider_message_id (text, required)
- provider_account_id (text, optional)
- sender_external_id (text, required)
- received_at_utc (timestamptz, required)
- payload_type (text, required: text|image|audio|forwarded|unsupported)
- raw_envelope_ref (text, required)
- correlation_id (text, required)
- created_at_utc (timestamptz, required)

Validation rules:
- `(tenant_id, source_channel, provider_message_id)` must be non-null and valid for idempotency checks.
- `payload_type` must map to supported enum or `unsupported`.

## Entity: unified_message_artifact
Purpose: Canonical normalized message persisted once per unique inbound message.

Fields:
- message_id (uuid, PK)
- tenant_id (uuid, required)
- context_id (uuid, required)
- created_by_user_id (uuid, required)
- source_channel (text, required)
- provider_message_id (text, required)
- message_kind (text, required: text|image|audio|forwarded|unsupported)
- message_text (text, optional)
- forwarded_from_ref (text, optional)
- provenance_event_id (uuid, required, FK -> inbound_message_event.event_id)
- acknowledged_at_utc (timestamptz, optional)
- capture_status (text, required)
- created_at_utc (timestamptz, required)

Validation rules:
- Required scope fields (`tenant_id`, `context_id`, `created_by_user_id`, `source_channel`) must always be populated.
- `capture_status` must be one of `accepted|duplicate_suppressed|unsupported|failed_terminal`.

## Entity: media_artifact
Purpose: Metadata record for image/audio attachments linked to canonical message.

Fields:
- media_id (uuid, PK)
- tenant_id (uuid, required)
- context_id (uuid, required)
- message_id (uuid, required, FK -> unified_message_artifact.message_id)
- media_type (text, required: image|audio)
- content_type (text, required)
- provider_media_id (text, optional)
- media_ref_uri (text, optional)
- byte_length (bigint, optional)
- provenance_event_id (uuid, required)
- created_at_utc (timestamptz, required)

Validation rules:
- Media rows are only valid for `message_kind` in image/audio categories.
- No media artifact should be created for duplicate-suppressed deliveries.

## Entity: idempotency_record
Purpose: Durable dedupe marker for canonical capture outcomes.

Fields:
- idempotency_id (uuid, PK)
- tenant_id (uuid, required)
- source_channel (text, required)
- provider_message_id (text, required)
- canonical_message_id (uuid, optional FK -> unified_message_artifact.message_id)
- first_seen_at_utc (timestamptz, required)
- last_seen_at_utc (timestamptz, required)
- duplicate_count (int, required default 0)

Constraints:
- Unique index on `(tenant_id, source_channel, provider_message_id)`.

## Entity: capture_audit_event
Purpose: Immutable compliance and operations record for each lifecycle side effect.

Fields:
- audit_id (uuid, PK)
- tenant_id (uuid, required)
- context_id (uuid, required)
- user_id (uuid, optional)
- source_channel (text, required)
- event_name (text, required)
- event_status (text, required)
- correlation_id (text, required)
- provider_message_id (text, optional)
- attempt_number (int, optional)
- failure_category (text, optional)
- payload_ref (text, optional)
- occurred_at_utc (timestamptz, required)

Allowed event_name values:
- capture.accepted
- capture.duplicate_suppressed
- capture.scope_denied
- capture.unsupported_payload
- capture.retry_scheduled
- capture.failed_terminal

## Relationships
- inbound_message_event 1:1 unified_message_artifact (for accepted canonical outcomes)
- unified_message_artifact 1:N media_artifact
- unified_message_artifact 1:1 idempotency_record (logical via canonical key)
- inbound_message_event 1:N capture_audit_event

## State Transitions
1. received -> principal_resolved
2. principal_resolved -> duplicate_suppressed
3. principal_resolved -> normalized
4. normalized -> persisted
5. normalized -> unsupported_persisted
6. persisted -> acknowledged
7. persisted -> retry_scheduled (transient failure branch)
8. retry_scheduled -> persisted (success before attempt 5)
9. retry_scheduled -> failed_terminal (after attempt 5)

## RLS and Scope Expectations
- Every table above must include tenant scope columns and enforce RLS policies.
- Session-level tenant/user scope must be set prior to read/write operations.
- Queries without scope context are denied and audited.
