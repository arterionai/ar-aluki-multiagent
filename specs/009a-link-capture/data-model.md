# Data Model: Link Capture and Secure Enrichment

## Overview

This feature introduces tenant-scoped entities for canonical link persistence, one-time confirmation lifecycle, enrichment outcomes, and recall projection.

## Entities

### 1) LinkArtifact

Purpose: Represents one active canonical link artifact per tenant for canonical-equivalent submissions.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `context_scope_id` (text, required, indexed)
- `created_by_principal_id` (text, required)
- `source_channel` (text, required)
- `canonical_url` (text, required)
- `url_hash` (text, required, indexed)
- `context_label` (text, nullable)
- `enrichment_status` (enum: `enriched`, `policy_blocked`, `timeout`, `failed`, default `failed` until first attempt)
- `enrichment_reason_code` (text, nullable)
- `description_text` (text, required)
- `site_name` (text, nullable)
- `title_text` (text, nullable)
- `first_captured_at_utc` (timestamp with time zone, required)
- `last_upserted_at_utc` (timestamp with time zone, required)
- `is_active` (bool, required, default true)

Constraints:
- Unique active artifact per canonical URL and tenant:
  - unique index on (`tenant_id`, `url_hash`, `is_active`) where `is_active=true`
- `canonical_url` must be absolute and normalized.
- `description_text` must be non-empty; fallback text required when enrichment limited.

Validation rules:
- Reject malformed or unsupported URLs before insert.
- Enforce tenant/context/principal non-null at write.

### 2) LinkProvenanceRef

Purpose: Captures many-to-one provenance/context references merged into a single `LinkArtifact`.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `link_artifact_id` (uuid, fk -> LinkArtifact.id, required, indexed)
- `source_message_id` (text, required)
- `source_channel` (text, required)
- `source_timestamp_utc` (timestamp with time zone, required)
- `captured_by_principal_id` (text, required)
- `context_label_snapshot` (text, nullable)
- `created_at_utc` (timestamp with time zone, required)

Constraints:
- Deduped provenance merge per source message:
  - unique (`tenant_id`, `link_artifact_id`, `source_message_id`)

### 3) LinkPendingConfirmation

Purpose: Represents a single unresolved yes/no decision window in session-conversation scope.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `context_scope_id` (text, required, indexed)
- `session_id` (text, required, indexed)
- `conversation_id` (text, required, indexed)
- `subject_link_artifact_id` (uuid, fk nullable)
- `state` (enum: `pending`, `resolved_yes`, `resolved_no`, `expired`)
- `expires_at_utc` (timestamp with time zone, required)
- `resolved_at_utc` (timestamp with time zone, nullable)
- `resolved_by_principal_id` (text, nullable)
- `resolve_message_id` (text, nullable)
- `resolve_cause` (text, nullable, e.g. `user_yes`, `user_no`, `timeout`)
- `created_at_utc` (timestamp with time zone, required)

Constraints:
- At most one active pending confirmation per session-conversation:
  - unique partial index on (`tenant_id`, `session_id`, `conversation_id`) where `state='pending'`
- Terminal states are immutable.

State transitions:
- `pending` -> `resolved_yes` on first explicit yes
- `pending` -> `resolved_no` on first explicit no
- `pending` -> `expired` when timeout reached
- Any transition attempt from terminal states is side-effect free and returns `already_resolved`

### 4) LinkEnrichmentAttempt

Purpose: Tracks each enrichment execution attempt including timeout fallback details.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `link_artifact_id` (uuid, fk -> LinkArtifact.id, required, indexed)
- `attempt_no` (int, required)
- `started_at_utc` (timestamp with time zone, required)
- `completed_at_utc` (timestamp with time zone, nullable)
- `duration_ms` (int, nullable)
- `outcome` (enum: `enriched`, `policy_blocked`, `timeout`, `failed`)
- `reason_code` (text, nullable)
- `provider_trace_id` (text, nullable)

Constraints:
- `duration_ms <= 4000` for timeout-bounded attempt completion classification.

### 5) EnrichmentPolicyDecision

Purpose: Auditable record of policy evaluation before outbound fetch.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `link_artifact_id` (uuid, fk -> LinkArtifact.id, required, indexed)
- `decision` (enum: `allow`, `block`)
- `reason_code` (text, required)
- `destination_host` (text, required)
- `evaluated_at_utc` (timestamp with time zone, required)
- `evaluator_version` (text, nullable)

Constraints:
- Must exist for every enrichment attempt.

### 6) LinkAuditEvent

Purpose: Immutable timeline of critical transitions and side-effect decisions.

Fields:
- `id` (uuid, pk)
- `tenant_id` (text, required, indexed)
- `entity_type` (text, required)
- `entity_id` (uuid, required, indexed)
- `event_type` (text, required)
- `event_time_utc` (timestamp with time zone, required)
- `actor_type` (text, required, e.g. `user`, `system`)
- `actor_id` (text, nullable)
- `payload_json` (jsonb, required)

Required audit events:
- confirmation resolved (`resolved_yes`/`resolved_no`/`expired`)
- enrichment policy decision
- enrichment timeout/failure fallback
- duplicate upsert merge
- idempotent replay no-op

## Projection

### LinkRecallView

Purpose: Deterministic user-facing recall projection.

Fields:
- `canonical_url` (text, required)
- `description` (text, required)
- `enrichment_status` (enum, required)
- `enrichment_reason` (text, nullable)
- `provenance_reference` (object, required):
  - `source_message_id`
  - `source_channel`
  - `captured_at_utc`

Rules:
- Always include full canonical URL.
- Always include non-empty description (enriched or fallback).
- Include explicit status/reason when limited metadata is returned.

## Idempotency Keys

- Capture idempotency key: `tenant_id + source_channel + source_message_id + canonical_url`
- Confirmation consume key: `pending_confirmation_id + resolve_message_id`

## RLS and Security Notes

- All tables include `tenant_id` and must enforce tenant policy filters.
- Reads/writes require resolved principal context and context scope.
- No secret-bearing values stored in artifacts; provider credentials remain externalized.
