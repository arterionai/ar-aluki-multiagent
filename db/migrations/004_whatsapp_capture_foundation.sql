-- 004_whatsapp_capture_foundation.sql
-- WhatsApp capture foundation tables, constraints, and indexes.
-- Implements the data model in specs/001-whatsapp-capture/data-model.md.
--
-- Canonical idempotency key: (tenant_id, source_channel, provider_message_id).
-- All tables carry tenant/context scope columns; RLS is enabled in 005.
-- gen_random_uuid() is a PostgreSQL core function (v13+); no extension required.

-- Raw provider envelope metadata for traceable capture intake.
create table if not exists inbound_message_event (
    event_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    source_channel text not null,
    provider_message_id text not null,
    provider_account_id text,
    sender_external_id text not null,
    received_at_utc timestamptz not null,
    payload_type text not null check (payload_type in ('text', 'image', 'audio', 'forwarded', 'unsupported')),
    raw_envelope_ref text not null,
    correlation_id text not null,
    created_at_utc timestamptz not null default now()
);

-- Canonical normalized message persisted once per unique inbound message.
create table if not exists unified_message_artifact (
    message_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    created_by_user_id uuid not null references users_profile(id),
    source_channel text not null,
    provider_message_id text not null,
    message_kind text not null check (message_kind in ('text', 'image', 'audio', 'forwarded', 'unsupported')),
    message_text text,
    forwarded_from_ref text,
    provenance_event_id uuid not null references inbound_message_event(event_id),
    acknowledged_at_utc timestamptz,
    capture_status text not null check (capture_status in ('accepted', 'duplicate_suppressed', 'unsupported', 'failed_terminal')),
    created_at_utc timestamptz not null default now()
);

-- Metadata record for image/audio attachments linked to canonical message.
create table if not exists media_artifact (
    media_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    message_id uuid not null references unified_message_artifact(message_id),
    media_type text not null check (media_type in ('image', 'audio')),
    content_type text not null,
    provider_media_id text,
    media_ref_uri text,
    byte_length bigint check (byte_length is null or byte_length >= 0),
    provenance_event_id uuid not null references inbound_message_event(event_id),
    created_at_utc timestamptz not null default now()
);

-- Durable dedupe marker for canonical capture outcomes.
create table if not exists idempotency_record (
    idempotency_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    source_channel text not null,
    provider_message_id text not null,
    canonical_message_id uuid references unified_message_artifact(message_id),
    first_seen_at_utc timestamptz not null default now(),
    last_seen_at_utc timestamptz not null default now(),
    duplicate_count int not null default 0,
    -- Canonical idempotency uniqueness (FR-013, SC-002, SC-008).
    constraint ux_idempotency_record_canonical unique (tenant_id, source_channel, provider_message_id)
);

-- Immutable compliance and operations record for each lifecycle side effect.
create table if not exists capture_audit_event (
    audit_id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    -- Nullable: scope_denied audits may be written before a context is resolved.
    context_id uuid references contexts(id),
    user_id uuid references users_profile(id),
    source_channel text not null,
    event_name text not null check (event_name in (
        'capture.accepted',
        'capture.duplicate_suppressed',
        'capture.scope_denied',
        'capture.unsupported_payload',
        'capture.retry_scheduled',
        'capture.failed_terminal'
    )),
    event_status text not null,
    correlation_id text not null,
    provider_message_id text,
    attempt_number int,
    failure_category text,
    payload_ref text,
    occurred_at_utc timestamptz not null default now()
);

create index if not exists ix_inbound_event_tenant_provider
    on inbound_message_event (tenant_id, source_channel, provider_message_id);
create index if not exists ix_inbound_event_correlation
    on inbound_message_event (correlation_id);

create index if not exists ix_message_artifact_tenant_context_created
    on unified_message_artifact (tenant_id, context_id, created_at_utc desc);
create index if not exists ix_message_artifact_tenant_provider
    on unified_message_artifact (tenant_id, source_channel, provider_message_id);

create index if not exists ix_media_artifact_message
    on media_artifact (message_id);
create index if not exists ix_media_artifact_tenant
    on media_artifact (tenant_id);

create index if not exists ix_audit_event_tenant_created
    on capture_audit_event (tenant_id, occurred_at_utc desc);
create index if not exists ix_audit_event_correlation
    on capture_audit_event (correlation_id);
