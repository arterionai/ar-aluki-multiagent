-- 015_feedback_suggestions.sql
-- SB-007 Feedback Suggestions Capture: per-user suggestion windows with
-- attachment tracking, state lifecycle, and append-only transition audit.
-- Builds on tenancy/RLS from 001/003. pgcrypto is NOT allow-listed on Azure;
-- use core gen_random_uuid() (PG16).

-- ── Suggestion windows ───────────────────────────────────────────────────────

create table if not exists suggestions (
    id                              uuid        primary key default gen_random_uuid(),
    tenant_id                       uuid        not null,
    user_id                         uuid        not null,
    state                           text        not null default 'captured'
        check (state in ('captured', 'enriched', 'sent_user', 'archived')),
    state_transitioned_at_utc       timestamptz not null default now(),
    archived_at_utc                 timestamptz,
    text_content                    text,
    text_blob_uri                   text,
    captured_at_utc                 timestamptz not null default now(),
    context_window_expires_at_utc   timestamptz not null,
    attachment_count                int         not null default 0
        check (attachment_count <= 10),
    inbound_message_id              text,
    inbound_payload_hash            text,
    created_at_utc                  timestamptz not null default now(),
    updated_at_utc                  timestamptz not null default now()
);

-- Idempotency: prevent duplicate captures for same message within active states
create unique index if not exists ux_suggestions_inbound_active
    on suggestions (tenant_id, inbound_message_id, inbound_payload_hash)
    where state in ('captured', 'enriched');

create index if not exists ix_suggestions_tenant_user_state
    on suggestions (tenant_id, user_id, state);

create index if not exists ix_suggestions_tenant_user_window
    on suggestions (tenant_id, user_id, context_window_expires_at_utc)
    where state != 'archived';

-- ── Suggestion attachments ───────────────────────────────────────────────────

create table if not exists suggestion_attachments (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    suggestion_id       uuid        not null references suggestions(id) on delete cascade,
    attachment_type     text        not null
        check (attachment_type in ('text', 'audio', 'photo')),
    blob_uri            text        not null,
    mime_type           text        not null,
    file_size_bytes     bigint      not null,
    content_hash        text        not null,
    linked_at_utc       timestamptz not null default now(),
    expires_at_utc      timestamptz not null,
    retained_until_utc  timestamptz,
    created_at_utc      timestamptz not null default now()
);

create index if not exists ix_suggestion_attachments_suggestion
    on suggestion_attachments (suggestion_id);

create index if not exists ix_suggestion_attachments_expiry
    on suggestion_attachments (expires_at_utc)
    where retained_until_utc is null;

-- ── State transition audit (append-only) ────────────────────────────────────

create table if not exists suggestion_state_transitions (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    suggestion_id       uuid        not null references suggestions(id) on delete cascade,
    prior_state         text        not null
        check (prior_state in ('captured', 'enriched', 'sent_user')),
    new_state           text        not null
        check (new_state in ('enriched', 'sent_user', 'archived')),
    actor               text        not null,
    reason              text        not null,
    duration_seconds    int         not null,
    transitioned_at_utc timestamptz not null default now()
);

create index if not exists ix_suggestion_state_transitions_suggestion
    on suggestion_state_transitions (suggestion_id);

create index if not exists ix_suggestion_state_transitions_tenant_time
    on suggestion_state_transitions (tenant_id, transitioned_at_utc desc);

-- ── Row-level security ───────────────────────────────────────────────────────

alter table suggestions                  enable row level security;
alter table suggestion_attachments       enable row level security;
alter table suggestion_state_transitions enable row level security;

-- suggestions: read + write scoped by tenant and user membership
drop policy if exists p_suggestions_tenant on suggestions;
create policy p_suggestions_tenant on suggestions
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- suggestion_attachments: read + write scoped by tenant and user membership
drop policy if exists p_suggestion_attachments_tenant on suggestion_attachments;
create policy p_suggestion_attachments_tenant on suggestion_attachments
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- suggestion_state_transitions: append-only; SELECT by tenant scope
drop policy if exists p_suggestion_state_transitions_read on suggestion_state_transitions;
create policy p_suggestion_state_transitions_read on suggestion_state_transitions
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_suggestion_state_transitions_write on suggestion_state_transitions;
create policy p_suggestion_state_transitions_write on suggestion_state_transitions
    for insert
    with check (tenant_id = app.current_tenant_id());
