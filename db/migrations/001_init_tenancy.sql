-- 001_init_tenancy.sql
-- Baseline identity, tenancy, membership, and context model.

-- Note: gen_random_uuid() is built into PostgreSQL core since v13, so no
-- extension is required (pgcrypto is not allow-listed on the Azure baseline).

create table if not exists tenants (
    id uuid primary key default gen_random_uuid(),
    tenant_type text not null check (tenant_type in ('INDIVIDUAL', 'ORGANIZATION')),
    display_name text not null,
    status text not null default 'ACTIVE',
    created_at timestamptz not null default now()
);

create table if not exists users_profile (
    id uuid primary key default gen_random_uuid(),
    external_auth_id text not null unique,
    email text,
    phone text,
    status text not null default 'ACTIVE',
    created_at timestamptz not null default now()
);

create table if not exists memberships (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    user_id uuid not null references users_profile(id),
    role text not null check (role in ('OWNER', 'ADMIN', 'MEMBER', 'GUEST')),
    status text not null default 'ACTIVE',
    created_at timestamptz not null default now(),
    unique (tenant_id, user_id)
);

create table if not exists contexts (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id),
    context_type text not null check (context_type in ('DM', 'GROUP', 'CHANNEL', 'EMAIL_THREAD')),
    external_context_id text not null,
    title text,
    created_at timestamptz not null default now(),
    unique (tenant_id, context_type, external_context_id)
);

create table if not exists context_access (
    id uuid primary key default gen_random_uuid(),
    context_id uuid not null references contexts(id),
    user_id uuid not null references users_profile(id),
    access_role text not null,
    status text not null default 'ACTIVE',
    created_at timestamptz not null default now(),
    unique (context_id, user_id)
);

create index if not exists ix_memberships_tenant_user on memberships (tenant_id, user_id);
create index if not exists ix_contexts_tenant on contexts (tenant_id);
create index if not exists ix_context_access_context_user on context_access (context_id, user_id);
