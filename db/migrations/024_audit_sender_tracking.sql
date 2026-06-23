-- Migration 024: Add sender_external_id to capture_audit_event for scope-denial traceability.
-- Enables forensic backfill of historically rejected senders (e.g., MembershipNotFound
-- before auto-provisioning was introduced). Also adds an unscoped denial log table so
-- denials where even the tenant is unresolved can be persisted (capture_audit_event
-- requires tenant_id NOT NULL due to RLS).

alter table capture_audit_event
    add column if not exists sender_external_id text;

create index if not exists ix_audit_event_sender
    on capture_audit_event (sender_external_id)
    where sender_external_id is not null;

-- Unscoped denial log: persists scope denials where the tenant could not be resolved.
-- No RLS — rows are written by the webhook pipeline before any principal is established.
-- Used for forensic backfill and abuse detection only; never served via tenant API.
create table if not exists capture_unresolved_denial (
    id              bigserial primary key,
    sender_external_id text not null,
    source_channel  text not null,
    correlation_id  text not null,
    failure_reason  text not null,
    occurred_at_utc timestamptz not null default now()
);

create index if not exists ix_capture_unresolved_denial_sender
    on capture_unresolved_denial (sender_external_id, occurred_at_utc desc);
