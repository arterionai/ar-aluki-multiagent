-- 005_whatsapp_capture_rls.sql
-- Row-level security for WhatsApp capture foundation tables.
-- Required session variables before any query (set per transaction):
--   select set_config('app.current_tenant', '<tenant-uuid>', true);
--   select set_config('app.current_user_id', '<user-uuid>', true);
--
-- Relies on helper functions defined in 003_enable_rls.sql:
--   app.current_tenant_id(), app.current_user_id(), app.user_in_tenant(uuid)

alter table inbound_message_event enable row level security;
alter table unified_message_artifact enable row level security;
alter table media_artifact enable row level security;
alter table idempotency_record enable row level security;
alter table capture_audit_event enable row level security;

-- Canonical capture tables: read/write require tenant scope and active membership.

drop policy if exists p_inbound_event_tenant on inbound_message_event;
create policy p_inbound_event_tenant on inbound_message_event
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_message_artifact_tenant on unified_message_artifact;
create policy p_message_artifact_tenant on unified_message_artifact
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_media_artifact_tenant on media_artifact;
create policy p_media_artifact_tenant on media_artifact
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_idempotency_record_tenant on idempotency_record;
create policy p_idempotency_record_tenant on idempotency_record
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

-- Audit table: read isolation requires membership, but denial audits MUST be
-- writable whenever a tenant scope is set even if membership resolution failed
-- (FR-006/SC-004). Reads remain strictly tenant + membership scoped.

drop policy if exists p_audit_event_read on capture_audit_event;
create policy p_audit_event_read on capture_audit_event
    for select
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_audit_event_write on capture_audit_event;
create policy p_audit_event_write on capture_audit_event
    for insert
    with check (tenant_id = app.current_tenant_id());
