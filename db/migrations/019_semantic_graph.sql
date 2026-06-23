-- 019_semantic_graph.sql
-- SB-011 Semantic Graph: entity resolution, relationship extraction, and graph traversal.
-- All tables tenant-scoped with RLS. Relationships are immutable (archived, not deleted).

-- ── Entities ──────────────────────────────────────────────────────────────────

create table if not exists semantic_entities (
    id              uuid        primary key default gen_random_uuid(),
    tenant_id       uuid        not null,
    entity_type     text        not null
        check (entity_type in ('person', 'organization', 'location', 'concept')),
    canonical_name  text        not null,
    description     text,
    active          boolean     not null default true,
    created_at_utc  timestamptz not null default now(),
    updated_at_utc  timestamptz not null default now()
);

create index if not exists ix_entities_tenant_type
    on semantic_entities (tenant_id, entity_type, active);

create index if not exists ix_entities_tenant_canonical
    on semantic_entities (tenant_id, lower(canonical_name));

-- ── Entity aliases ────────────────────────────────────────────────────────────

create table if not exists semantic_entity_aliases (
    id              uuid        primary key default gen_random_uuid(),
    entity_id       uuid        not null references semantic_entities(id),
    tenant_id       uuid        not null,
    alias           text        not null,
    confidence      numeric(4,3) not null check (confidence >= 0 and confidence <= 1),
    created_at_utc  timestamptz not null default now()
);

create unique index if not exists ux_entity_alias
    on semantic_entity_aliases (entity_id, lower(alias));

create index if not exists ix_alias_lookup
    on semantic_entity_aliases (tenant_id, lower(alias));

-- ── Relationships ─────────────────────────────────────────────────────────────

create table if not exists semantic_relationships (
    id                  uuid        primary key default gen_random_uuid(),
    tenant_id           uuid        not null,
    source_entity_id    uuid        not null references semantic_entities(id),
    target_entity_id    uuid        not null references semantic_entities(id),
    relationship_type   text        not null
        check (relationship_type in ('worksAt', 'owns', 'mentions', 'collaboratesWith', 'manages', 'generic')),
    confidence          numeric(4,3) not null check (confidence >= 0 and confidence <= 1),
    explanation         text,
    source_fact_ids     jsonb       not null default '[]'::jsonb,
    active              boolean     not null default true,
    created_at_utc      timestamptz not null default now(),
    archived_at_utc     timestamptz
);

create index if not exists ix_rel_source
    on semantic_relationships (tenant_id, source_entity_id, active);

create index if not exists ix_rel_target
    on semantic_relationships (tenant_id, target_entity_id, active);

-- ── Entity-fact provenance links ──────────────────────────────────────────────

create table if not exists semantic_entity_facts (
    id              uuid        primary key default gen_random_uuid(),
    entity_id       uuid        not null references semantic_entities(id),
    fact_id         uuid        not null,
    tenant_id       uuid        not null,
    created_at_utc  timestamptz not null default now()
);

create unique index if not exists ux_entity_fact
    on semantic_entity_facts (entity_id, fact_id);

create index if not exists ix_entity_facts_fact
    on semantic_entity_facts (tenant_id, fact_id);

-- ── Row-level security ────────────────────────────────────────────────────────

alter table semantic_entities       enable row level security;
alter table semantic_entity_aliases enable row level security;
alter table semantic_relationships  enable row level security;
alter table semantic_entity_facts   enable row level security;

-- semantic_entities
drop policy if exists p_entities_select on semantic_entities;
create policy p_entities_select on semantic_entities
    for select using (tenant_id = app.current_tenant_id());

drop policy if exists p_entities_insert on semantic_entities;
create policy p_entities_insert on semantic_entities
    for insert with check (tenant_id = app.current_tenant_id());

drop policy if exists p_entities_update on semantic_entities;
create policy p_entities_update on semantic_entities
    for update using  (tenant_id = app.current_tenant_id())
    with check        (tenant_id = app.current_tenant_id());

-- semantic_entity_aliases
drop policy if exists p_aliases_select on semantic_entity_aliases;
create policy p_aliases_select on semantic_entity_aliases
    for select using (tenant_id = app.current_tenant_id());

drop policy if exists p_aliases_insert on semantic_entity_aliases;
create policy p_aliases_insert on semantic_entity_aliases
    for insert with check (tenant_id = app.current_tenant_id());

-- semantic_relationships (archive via UPDATE, no hard delete)
drop policy if exists p_rel_select on semantic_relationships;
create policy p_rel_select on semantic_relationships
    for select using (tenant_id = app.current_tenant_id());

drop policy if exists p_rel_insert on semantic_relationships;
create policy p_rel_insert on semantic_relationships
    for insert with check (tenant_id = app.current_tenant_id());

drop policy if exists p_rel_update on semantic_relationships;
create policy p_rel_update on semantic_relationships
    for update using  (tenant_id = app.current_tenant_id())
    with check        (tenant_id = app.current_tenant_id());

-- semantic_entity_facts (provenance; INSERT+SELECT only)
drop policy if exists p_ef_select on semantic_entity_facts;
create policy p_ef_select on semantic_entity_facts
    for select using (tenant_id = app.current_tenant_id());

drop policy if exists p_ef_insert on semantic_entity_facts;
create policy p_ef_insert on semantic_entity_facts
    for insert with check (tenant_id = app.current_tenant_id());
