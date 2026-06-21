# Data Model: Delegated Reminders

## Overview

This feature adds tenant-scoped delegated reminder entities that preserve recipient resolution lineage, explicit consent evidence, bounded retry traceability, and immutable lifecycle auditing.

## Entities

### 1) DelegatedReminder

Purpose: Canonical delegated reminder record for sender->recipient reminder workflows.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `sender_user_id` (text, required)
- `sender_identity` (text, required)
- `recipient_identity` (text, required)
- `recipient_display_name` (text, nullable)
- `routing_key` (text, required)  # `(tenant_id, recipient_identity, sender_identity)`
- `content` (text, required)
- `due_time_utc` (timestamptz, required)
- `status` (enum: `draft`, `awaiting_recipient_resolution`, `awaiting_consent`, `scheduled`, `delivery_in_progress`, `delivered`, `delivery_failed_terminal`, `cancelled`, required)
- `consent_acquired` (bool, required, default false)
- `cancel_deadline_utc` (timestamptz, required)
- `delivery_phase_started_at_utc` (timestamptz, nullable)
- `correlation_id` (text, required)
- `created_at_utc` (timestamptz, required)
- `updated_at_utc` (timestamptz, required)

Constraints:
- Unique (`tenant_id`, `id`)
- Unique (`tenant_id`, `routing_key`, `due_time_utc`, `content`) to reduce duplicate scheduling
- `cancel_deadline_utc = due_time_utc - interval '30 seconds'`

### 2) DelegatedRecipientContact

Purpose: Resolved recipient identity profile used by the three-tier resolution flow.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `sender_user_id` (text, required)
- `recipient_identity` (text, required)
- `recipient_name` (text, nullable)
- `phone_e164` (text, nullable)
- `whatsapp_handle` (text, nullable)
- `resolution_tier` (enum: `tier1_known_contact_confirmed`, `tier2_phone_only_needs_capture`, `tier3_unknown_needs_clarification`, required)
- `is_confirmed` (bool, required, default false)
- `last_confirmed_at_utc` (timestamptz, nullable)
- `created_at_utc` (timestamptz, required)
- `updated_at_utc` (timestamptz, required)

Constraints:
- Unique (`tenant_id`, `sender_user_id`, `recipient_identity`)
- At least one of `phone_e164` or `whatsapp_handle` present when `is_confirmed=true`

### 3) DelegatedConsentRegistry

Purpose: Persistent explicit opt-in state for delegated reminders.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `recipient_identity` (text, required)
- `consent_scope` (enum: `global`, `sender_scoped`, required)
- `sender_user_id` (text, nullable)  # required for sender_scoped
- `consent_status` (enum: `opted_in`, `opted_out`, `revoked`, required)
- `granted_at_utc` (timestamptz, nullable)
- `revoked_at_utc` (timestamptz, nullable)
- `policy_version` (text, required)
- `source_event_id` (text, required)
- `created_at_utc` (timestamptz, required)
- `updated_at_utc` (timestamptz, required)

Constraints:
- Unique (`tenant_id`, `recipient_identity`, `consent_scope`, `coalesce(sender_user_id,'*')`)
- `sender_user_id` required when `consent_scope='sender_scoped'`

### 4) DelegatedDeliveryAttempt

Purpose: Durable record for each delivery attempt and retry classification.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `delegated_reminder_id` (uuid, required, indexed)
- `attempt_index` (int, required)  # 1..5
- `scheduled_attempt_time_utc` (timestamptz, required)
- `started_at_utc` (timestamptz, required)
- `completed_at_utc` (timestamptz, nullable)
- `result` (enum: `delivered`, `transient_failure`, `permanent_invalid_recipient`, `permanent_permission`, `system_error`, required)
- `retry_delay_seconds` (int, nullable)
- `provider_reference` (text, nullable)
- `failure_detail` (text, nullable)
- `correlation_id` (text, required)
- `created_at_utc` (timestamptz, required)

Constraints:
- Unique (`tenant_id`, `delegated_reminder_id`, `attempt_index`)
- `attempt_index` between 1 and 5

### 5) DelegatedPolicyDecisionEvent

Purpose: Record anti-spam and consent policy decisions for delegated workflows.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `delegated_reminder_id` (uuid, nullable)
- `sender_user_id` (text, required)
- `decision_type` (enum: `consent_check`, `anti_spam_check`, required)
- `decision_outcome` (enum: `allow`, `deny`, `needs_consent`, required)
- `reason_code` (text, required)
- `rolling_window_count` (int, nullable)
- `rolling_window_limit` (int, nullable)
- `correlation_id` (text, required)
- `created_at_utc` (timestamptz, required)

### 6) DelegatedAuditEvent

Purpose: Immutable append-only lifecycle evidence.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `delegated_reminder_id` (uuid, required, indexed)
- `event_type` (text, required)
- `actor_type` (enum: `sender`, `recipient`, `system`, required)
- `actor_id` (text, nullable)
- `payload_json` (jsonb, required)
- `correlation_id` (text, required)
- `occurred_at_utc` (timestamptz, required)

Required event types:
- `delegated_reminder.created`
- `delegated_reminder.recipient_resolved`
- `delegated_reminder.consent_acquired`
- `delegated_reminder.delivery_started`
- `delegated_reminder.delivery_succeeded`
- `delegated_reminder.delivery_failed_terminal`
- `delegated_reminder.cancelled`

## State Model

State transitions for `DelegatedReminder.status`:
- `draft` -> `awaiting_recipient_resolution`
- `awaiting_recipient_resolution` -> `awaiting_consent` | `scheduled`
- `awaiting_consent` -> `scheduled`
- `scheduled` -> `delivery_in_progress` | `cancelled`
- `delivery_in_progress` -> `delivered` | `delivery_failed_terminal`

Rules:
- Cancellation is allowed only while `now_utc <= cancel_deadline_utc` and status is not `delivery_in_progress`.
- Once status is `delivery_in_progress`, recall is not allowed.
- Permanent failure classes terminate immediately; transient failures can retry to max five attempts.

## RLS and Security Notes

- All feature tables include `tenant_id` and are protected by tenant-scoped RLS.
- Reads and writes require principal context; unresolved context fails closed.
- Recipient consent and anti-spam decisions are persisted before scheduling.
- Audit entities are append-only; corrections are compensating events, not updates.

## Idempotency Keys

- Reminder creation: `tenant_id + sender_user_id + recipient_identity + due_time_utc + normalized_content_hash`
- Delivery attempt: `tenant_id + delegated_reminder_id + attempt_index`
- Policy decision replay: `tenant_id + decision_type + source_event_id`

## Validation Summary

The model supports all required capabilities: delegated routing isolation, tiered recipient resolution, explicit consent gating, bounded retry semantics, cancellation boundaries, sender failure visibility, and immutable lifecycle traceability.
