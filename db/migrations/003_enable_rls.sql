-- 003_enable_rls.sql
-- Baseline RLS policies for tenant and membership scoped access.
-- Required session variables before any query:
--   set app.current_tenant = '<tenant-uuid>';
--   set app.current_user_id = '<user-uuid>';

create schema if not exists app;

create or replace function app.current_tenant_id()
returns uuid
language sql
stable
as $$
    select nullif(current_setting('app.current_tenant', true), '')::uuid
$$;

create or replace function app.current_user_id()
returns uuid
language sql
stable
as $$
    select nullif(current_setting('app.current_user_id', true), '')::uuid
$$;

create or replace function app.user_in_tenant(target_tenant uuid)
returns boolean
language sql
stable
as $$
    select exists (
        select 1
        from memberships m
        where m.tenant_id = target_tenant
          and m.user_id = app.current_user_id()
          and m.status = 'ACTIVE'
    )
$$;

-- Enable RLS
alter table messages enable row level security;
alter table entities enable row level security;
alter table entity_mentions enable row level security;
alter table relationships enable row level security;
alter table context_clusters enable row level security;
alter table vector_embeddings enable row level security;
alter table audit_log enable row level security;
alter table contexts enable row level security;

-- Tenant and membership policies
drop policy if exists p_messages_tenant on messages;
create policy p_messages_tenant on messages
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_entities_tenant on entities;
create policy p_entities_tenant on entities
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_mentions_tenant on entity_mentions;
create policy p_mentions_tenant on entity_mentions
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_relationships_tenant on relationships;
create policy p_relationships_tenant on relationships
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_clusters_tenant on context_clusters;
create policy p_clusters_tenant on context_clusters
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_embeddings_tenant on vector_embeddings;
create policy p_embeddings_tenant on vector_embeddings
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_audit_tenant on audit_log;
create policy p_audit_tenant on audit_log
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));

drop policy if exists p_contexts_tenant on contexts;
create policy p_contexts_tenant on contexts
    using (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id))
    with check (tenant_id = app.current_tenant_id() and app.user_in_tenant(tenant_id));
