# Data Model: Calendar Integration

## Entity: CalendarConnection
Represents an active provider authorization binding for a scoped user.

Fields:
- `calendar_connection_id` (UUID, PK)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `provider` (string, required, enum: `outlook`, `google`)
- `connection_status` (string, required, enum: `connected`, `disconnected`, `revoked`, `failed`)
- `connected_at_utc` (timestamp with timezone, nullable)
- `disconnected_at_utc` (timestamp with timezone, nullable)
- `provider_account_ref` (string, nullable)
- `default_for_user` (bool, required)
- `correlation_id` (string, required)

Validation rules:
- At most one active (`connected`) record per `(tenant_id, context_id, user_id, provider)`.
- `default_for_user` can be true for at most one active provider per `(tenant_id, context_id, user_id)`.

State transitions:
- `failed` -> `connected`
- `connected` -> `disconnected|revoked`
- `disconnected|revoked` -> `connected`

## Entity: OAuthCallbackState
Single-use authorization callback state used for anti-forgery and replay protection.

Fields:
- `oauth_callback_state_id` (UUID, PK)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `provider` (string, required)
- `state_nonce` (string, required, unique)
- `issued_at_utc` (timestamp with timezone, required)
- `expires_at_utc` (timestamp with timezone, required)
- `used_at_utc` (timestamp with timezone, nullable)
- `status` (string, required, enum: `issued`, `consumed`, `expired`, `rejected`)
- `correlation_id` (string, required)

Validation rules:
- `state_nonce` is single-use.
- Callback is invalid when `used_at_utc` is populated, `expires_at_utc` elapsed, or scope/provider mismatch occurs.

## Entity: EventCreationRequest
Normalized create-event request derived from natural language.

Fields:
- `event_creation_request_id` (UUID, PK)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `provider_hint` (string, nullable)
- `title` (string, required)
- `start_local` (string, required)
- `end_local` (string, nullable)
- `canonical_timezone` (string, required)
- `timezone_resolution_source` (string, required, enum: `request`, `profile`, `clarified`)
- `normalized_payload_hash` (string, required)
- `requested_at_utc` (timestamp with timezone, required)
- `correlation_id` (string, required)

Validation rules:
- `title`, start time, and canonical timezone are required before side effects.
- Ambiguous/nonexistent DST local times cannot transition to provider create until clarified.

## Entity: ClarificationTurn
Captures missing/ambiguous required details prior to event creation.

Fields:
- `clarification_turn_id` (UUID, PK)
- `event_creation_request_id` (UUID, FK -> EventCreationRequest)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `question_text` (string, required)
- `requested_field` (string, required)
- `answer_text` (string, nullable)
- `status` (string, required, enum: `pending`, `answered`, `expired`)
- `created_at_utc` (timestamp with timezone, required)
- `answered_at_utc` (timestamp with timezone, nullable)

Validation rules:
- Create side effect is blocked while any required-field clarification is `pending`.

## Entity: ProviderSelectionDecision
Durable decision record for deterministic provider selection.

Fields:
- `provider_selection_decision_id` (UUID, PK)
- `event_creation_request_id` (UUID, FK -> EventCreationRequest)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `selected_provider` (string, required)
- `selection_reason` (string, required, enum: `explicit_request`, `user_default`, `deterministic_tiebreak`)
- `available_providers` (jsonb, required)
- `created_at_utc` (timestamp with timezone, required)

Validation rules:
- Selection must be deterministic from the same inputs.
- Decision record is required before provider-side create call.

## Entity: DeduplicationRecord
Tracks logical create request identity for retry suppression and stable outcomes.

Fields:
- `deduplication_record_id` (UUID, PK)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `provider` (string, required)
- `idempotency_key` (string, required)
- `window_started_at_utc` (timestamp with timezone, required)
- `window_expires_at_utc` (timestamp with timezone, required)
- `first_outcome_ref` (string, required)
- `first_provider_event_ref` (string, nullable)
- `status` (string, required, enum: `in_progress`, `created`, `failed`)

Validation rules:
- Idempotency key is unique while `window_expires_at_utc` is active.
- Duplicate in-window requests must reuse `first_outcome_ref` and not create extra provider events.

## Entity: CalendarEventOutcome
User-visible and auditable result of create-event processing.

Fields:
- `calendar_event_outcome_id` (UUID, PK)
- `event_creation_request_id` (UUID, FK -> EventCreationRequest)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `provider` (string, required)
- `outcome_type` (string, required, enum: `created`, `previously_created`, `clarification_required`, `reconnect_required`, `denied`, `failed`)
- `outcome_reference` (string, required)
- `provider_event_reference` (string, nullable)
- `final_title` (string, nullable)
- `final_start_utc` (timestamp with timezone, nullable)
- `final_end_utc` (timestamp with timezone, nullable)
- `final_timezone` (string, nullable)
- `created_at_utc` (timestamp with timezone, required)
- `correlation_id` (string, required)

Validation rules:
- `outcome_type = created` requires non-null `provider_event_reference`.
- `outcome_type = previously_created` requires same `outcome_reference` as the first in-window create.

## Entity: AuthorizationFailureOutcome
Durable reconnect-required outcome when provider authorization is invalid/expired.

Fields:
- `authorization_failure_outcome_id` (UUID, PK)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `provider` (string, required)
- `failure_reason` (string, required, enum: `expired_token`, `invalid_grant`, `refresh_denied`, `scope_denied`)
- `reconnect_required` (bool, required)
- `outcome_reference` (string, required)
- `created_at_utc` (timestamp with timezone, required)
- `correlation_id` (string, required)

Validation rules:
- Must not expose raw token values.
- Must not persist partial create result when reconnect is required.

## Entity: CalendarAuditEvent
Compliance evidence for connection lifecycle and create outcomes.

Fields:
- `calendar_audit_event_id` (UUID, PK)
- `event_name` (string, required)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, nullable)
- `provider` (string, nullable)
- `skill_name` (string, required)
- `result` (string, required)
- `outcome_reference` (string, nullable)
- `correlation_id` (string, required)
- `occurred_at_utc` (timestamp with timezone, required)
- `payload_json` (jsonb, required)

Validation rules:
- Events required for connect, disconnect, create-success, create-denied, and auth-failure outcomes.
- `payload_json` must be token-redacted.

## Relationships
- `CalendarConnection` 1..* `OAuthCallbackState`
- `EventCreationRequest` 1..* `ClarificationTurn`
- `EventCreationRequest` 1..1 `ProviderSelectionDecision`
- `EventCreationRequest` 1..* `CalendarEventOutcome`
- `DeduplicationRecord` 1..* `CalendarEventOutcome` (logical create lineage)
- `AuthorizationFailureOutcome` may map to one `CalendarEventOutcome` with `reconnect_required`
- `CalendarAuditEvent` references lifecycle and create operations across all entities
