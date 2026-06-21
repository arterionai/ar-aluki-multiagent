-- 002_init_artifacts.sql
-- Baseline artifacts for messages, embeddings, entities, relationships.

create extension if not exists vector;

create table if not exists messages (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid not null references contexts(id),
    created_by_user_id uuid not null references users_profile(id),
    source_channel text not null,
    source_text text not null,
    provenance_message_id uuid,
    created_at timestamptz not null default now()
);

create table if not exists entities (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    entity_type text not null,
    canonical_name text not null,
    attributes jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now()
);

create table if not exists entity_aliases (
    id uuid primary key default gen_random_uuid(),
    entity_id uuid not null references entities(id),
    alias text not null,
    created_at timestamptz not null default now(),
    unique (entity_id, alias)
);

create table if not exists entity_mentions (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    entity_id uuid not null references entities(id),
    message_id uuid not null references messages(id),
    mention_text text not null,
    created_at timestamptz not null default now()
);

create table if not exists relationships (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    from_entity_id uuid not null references entities(id),
    to_entity_id uuid not null references entities(id),
    relationship_type text not null,
    confidence numeric(5,4),
    source_message_id uuid references messages(id),
    created_at timestamptz not null default now()
);

create table if not exists context_clusters (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    cluster_name text not null,
    cluster_type text,
    created_at timestamptz not null default now()
);

create table if not exists vector_embeddings (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    message_id uuid not null references messages(id),
    embedding vector(1536) not null,
    created_at timestamptz not null default now(),
    unique (message_id)
);

create table if not exists audit_log (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_id uuid references contexts(id),
    user_id uuid references users_profile(id),
    skill_name text not null,
    action_name text not null,
    success boolean not null,
    correlation_id text not null,
    details jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now()
);

create index if not exists ix_messages_tenant_context_created on messages (tenant_id, context_id, created_at desc);
create index if not exists ix_entities_tenant_name on entities (tenant_id, canonical_name);
create index if not exists ix_relationships_tenant on relationships (tenant_id);
create index if not exists ix_mentions_tenant on entity_mentions (tenant_id);
create index if not exists ix_embeddings_tenant on vector_embeddings (tenant_id);
create index if not exists ix_audit_tenant_created on audit_log (tenant_id, created_at desc);
