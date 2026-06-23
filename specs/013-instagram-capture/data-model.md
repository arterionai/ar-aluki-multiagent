# Data Model: Instagram Channel Capture

## Reused Entities (no schema changes)

The following tables from SB-001 are reused without modification. All queries
filter by `source_channel = 'instagram'` where applicable.

- `unified_message_artifact` — canonical message record; `source_channel = 'instagram'`, `message_kind` values: `text|image|audio|unsupported`.
- `media_artifact` — image/audio metadata linked to a unified message artifact.
- `idempotency_record` — dedup marker keyed on `(tenant_id, 'instagram', mid)`.
- `capture_audit_event` — immutable lifecycle audit; `source_channel = 'instagram'`.
- `outbound_messages` (SB-000) — outbound reply record; `channel_type = 'instagram'`.

---

## New Entity: instagram_channel_registrations

**Purpose**: Maps an Instagram sender (IGSID) to a registered tenant user so that inbound DMs can be scope-resolved before any capture side effect.

**Migration file**: `022_instagram_channel_registrations.sql`

**Fields**:
- `registration_id` (uuid, PK, default `gen_random_uuid()`)
- `tenant_id` (uuid, required, FK → tenants)
- `context_id` (uuid, required)
- `user_id` (uuid, required, FK → users)
- `igsid` (text, required) — Instagram-Scoped User ID of the sender
- `ig_account_id` (text, required) — Instagram Business Account ID that received the DM
- `status` (text, required, default `'active'`) — `active` | `revoked`
- `registered_at_utc` (timestamptz, required, default `now()`)
- `revoked_at_utc` (timestamptz, optional)
- `registered_by_user_id` (uuid, optional) — tenant admin who registered the sender

**Constraints**:
- Unique index on `(igsid, ig_account_id)` — one IGSID per Instagram Business Account maps to at most one registration row; a second active registration for the same pair is rejected at configuration time (FR-022).
- Check constraint: `status IN ('active', 'revoked')`.

**RLS**:
- SELECT: `tenant_id = app.current_tenant()` — tenants can only see their own registrations.
- INSERT/UPDATE: same `tenant_id` restriction.
- No cross-tenant access to `igsid` values.

---

## New Entity: instagram_inbound_event (transient envelope)

**Purpose**: Raw provider envelope metadata for the Instagram DM for traceable intake; mirrors `inbound_message_event` from SB-001 but with Instagram-specific field names. Stored as a capture pipeline artifact, not a long-lived table (same table as `inbound_message_event` with `source_channel = 'instagram'`).

No new table is required. The existing `inbound_message_event` table accommodates Instagram by setting `source_channel = 'instagram'` and `provider_account_id = ig_account_id`.

---

## State Transitions

The capture state machine is identical to SB-001. Key transitions for Instagram:

1. `received` → signature valid? → `signature_validated`
2. `signature_validated` → IGSID registered? → `principal_resolved` | `scope_denied`
3. `principal_resolved` → duplicate `mid`? → `duplicate_suppressed`
4. `principal_resolved` → supported type? → `normalized` | `unsupported_normalized`
5. `normalized` → `persisted` → `dispatched` (IMessageDispatcher)
6. `dispatched` → reply generated? → `outbound_sent` | `outbound_failed`
7. `persisted` → transient error? → `retry_scheduled` → max 5 → `failed_terminal`

---

## Migration Notes

- Migration `022_instagram_channel_registrations.sql` must be appended to the explicit migration list in `.github/workflows/azure-deploy-runtime.yml` (the `for f in ...` loop).
- Migration must also be added to the fixture list in `tests/integration/.../DbCaptureFixture.cs`.
- `pgcrypto` is not used; `gen_random_uuid()` is the PG16 built-in used throughout.

---

## Relationships

- `instagram_channel_registrations` N:1 `unified_message_artifact` (one registration → many messages over time)
- `unified_message_artifact` 1:N `media_artifact` (unchanged from SB-001)
- `unified_message_artifact` 1:1 `idempotency_record` (logical, via `(tenant_id, 'instagram', mid)`)
- `inbound_message_event` 1:N `capture_audit_event` (unchanged)
- `unified_message_artifact` 1:1 `outbound_messages` via `correlation_message_id` (SB-000)

---

## RLS and Scope Expectations

- All tables enforce `tenant_id = app.current_tenant()` via existing RLS policies (capture tables) or new RLS policy (`instagram_channel_registrations`).
- Session GUCs `app.current_tenant` and `app.current_user_id` must be set before any read or write.
- The `igsid` value is tenant-private; no cross-tenant query should expose it.
