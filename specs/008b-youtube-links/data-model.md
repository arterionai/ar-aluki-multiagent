# Data Model: YouTube Link Save and Classification (008)

**Version**: 1.0 | **Date**: 2026-06-21 | **Persistence**: PostgreSQL 15+ with RLS

## Entity Relationships

```text
InboundMessageEvidence (existing)
  -> SavedLinkArtifact (tenant-scoped canonical video record)
      -> LinkEnrichment (provider metadata snapshot)
      -> LinkClassification (category/tags/summary with confidence)
      -> LinkCaptureAuditEvent (append-only lifecycle/audit events)
```

Each relationship is tenant-scoped and keyed to canonical video identity.

## Core Entities

### 1. SavedLinkArtifact

Purpose: Tenant-scoped canonical record for a captured YouTube video.

Key fields:
- `id` (uuid, pk)
- `tenant_id` (uuid, required)
- `context_id` (uuid, required)
- `principal_id` (text, required)
- `canonical_video_id` (text, required)
- `canonical_url` (text, required)
- `original_source_url` (text, required)
- `status` (enum: `active`, `inactive`)
- `first_captured_at` (timestamptz, required)
- `last_refreshed_at` (timestamptz, required)
- `created_at` (timestamptz, required)
- `updated_at` (timestamptz, required)

Constraints:
- Unique upsert key: `(tenant_id, canonical_video_id)`
- `canonical_url` must match canonical format for persisted records
- Tenant ownership fields non-null for RLS enforcement

Notes:
- Re-submitting same canonical identity in same tenant updates `last_refreshed_at` and related metadata.
- Same canonical identity across different tenants creates isolated rows.

### 2. LinkEnrichment

Purpose: Best-effort metadata from primary/secondary provider or degraded fallback.

Key fields:
- `id` (uuid, pk)
- `saved_link_id` (uuid, fk -> SavedLinkArtifact.id)
- `tenant_id` (uuid, required)
- `enrichment_state` (enum: `enriched`, `partial`, `degraded`)
- `provider_used` (enum: `primary`, `secondary`, `none`)
- `title` (text, nullable)
- `description_snippet` (text, nullable)
- `channel_name` (text, nullable)
- `published_at` (timestamptz, nullable)
- `provider_error_code` (text, nullable)
- `provider_latency_ms` (int, nullable)
- `captured_at` (timestamptz, required)

Constraints:
- If `provider_used = none`, then `enrichment_state = degraded`
- If `enrichment_state = degraded`, metadata fields may be null
- Foreign key tenant consistency with parent artifact

### 3. LinkClassification

Purpose: Structured interpretation for filtering and recall with confidence visibility.

Key fields:
- `id` (uuid, pk)
- `saved_link_id` (uuid, fk -> SavedLinkArtifact.id)
- `tenant_id` (uuid, required)
- `category` (text, nullable)
- `tags` (jsonb array of text, required default `[]`)
- `summary` (text, nullable)
- `confidence_label` (enum: `high`, `medium`, `low`, required)
- `category_uncertain` (bool, required default false)
- `tags_uncertain` (bool, required default false)
- `summary_uncertain` (bool, required default false)
- `confidence_score` (numeric(4,3), nullable)
- `classified_at` (timestamptz, required)

Constraints:
- `confidence_label` must be one of high/medium/low
- Low-confidence outputs must mark at least one uncertainty flag true when applicable
- Structured fields must remain queryable (no opaque blob-only storage)

### 4. LinkCaptureAuditEvent

Purpose: Immutable operational trail for compliance and debugging.

Key fields:
- `id` (uuid, pk)
- `tenant_id` (uuid, required)
- `context_id` (uuid, required)
- `principal_id` (text, required)
- `message_id` (text, nullable)
- `canonical_video_id` (text, nullable)
- `event_type` (enum)
- `outcome_code` (text, required)
- `details` (jsonb, nullable)
- `created_at` (timestamptz, required)

Recommended `event_type` values:
- `detection_attempted`
- `normalization_succeeded`
- `normalization_failed`
- `enrichment_primary_attempted`
- `enrichment_secondary_attempted`
- `persistence_created`
- `persistence_refreshed`
- `persistence_skipped_invalid`
- `user_outcome_emitted`

Constraints:
- Append-only usage
- Tenant scope required on every row

## Behavioral Rules Encoded in Model

1. Provider fallback order
- Persist enrichment metadata so audit can prove deterministic sequence: primary -> secondary -> degraded.

2. Unsupported URL behavior
- `SavedLinkArtifact` is not created when normalization fails or URL is unsupported.
- Audit row with `event_type=normalization_failed` and `outcome_code=invalid_link` is required.

3. Duplicate handling
- Per-message dedupe occurs before persistence by canonical video identity set.
- Cross-message idempotency enforced by unique key `(tenant_id, canonical_video_id)` and refresh semantics.

4. Confidence visibility
- `LinkClassification.confidence_label` is mandatory for user confirmation payloads.
- Uncertainty booleans support explicit marking of low-confidence fields.

## Suggested SQL DDL Skeleton

```sql
CREATE TABLE saved_link_artifacts (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  context_id uuid NOT NULL,
  principal_id text NOT NULL,
  canonical_video_id text NOT NULL,
  canonical_url text NOT NULL,
  original_source_url text NOT NULL,
  status text NOT NULL CHECK (status IN ('active','inactive')),
  first_captured_at timestamptz NOT NULL,
  last_refreshed_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL,
  UNIQUE (tenant_id, canonical_video_id)
);

CREATE TABLE link_enrichments (
  id uuid PRIMARY KEY,
  saved_link_id uuid NOT NULL REFERENCES saved_link_artifacts(id) ON DELETE CASCADE,
  tenant_id uuid NOT NULL,
  enrichment_state text NOT NULL CHECK (enrichment_state IN ('enriched','partial','degraded')),
  provider_used text NOT NULL CHECK (provider_used IN ('primary','secondary','none')),
  title text NULL,
  description_snippet text NULL,
  channel_name text NULL,
  published_at timestamptz NULL,
  provider_error_code text NULL,
  provider_latency_ms int NULL,
  captured_at timestamptz NOT NULL
);

CREATE TABLE link_classifications (
  id uuid PRIMARY KEY,
  saved_link_id uuid NOT NULL REFERENCES saved_link_artifacts(id) ON DELETE CASCADE,
  tenant_id uuid NOT NULL,
  category text NULL,
  tags jsonb NOT NULL DEFAULT '[]'::jsonb,
  summary text NULL,
  confidence_label text NOT NULL CHECK (confidence_label IN ('high','medium','low')),
  category_uncertain boolean NOT NULL DEFAULT false,
  tags_uncertain boolean NOT NULL DEFAULT false,
  summary_uncertain boolean NOT NULL DEFAULT false,
  confidence_score numeric(4,3) NULL,
  classified_at timestamptz NOT NULL
);

CREATE TABLE link_capture_audit_events (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  context_id uuid NOT NULL,
  principal_id text NOT NULL,
  message_id text NULL,
  canonical_video_id text NULL,
  event_type text NOT NULL,
  outcome_code text NOT NULL,
  details jsonb NULL,
  created_at timestamptz NOT NULL
);
```

## RLS and Security Notes

- Every table above includes `tenant_id` for policy enforcement.
- Policies must deny reads/writes without resolved tenant scope.
- Denials should still create auditable denial events in existing secure audit path.

## Indexing Guidance

- `saved_link_artifacts (tenant_id, canonical_video_id)` unique index
- `saved_link_artifacts (tenant_id, updated_at DESC)` for tenant recall/order
- `link_classifications (tenant_id, confidence_label)` for confidence filtering
- `link_capture_audit_events (tenant_id, created_at DESC)` for investigations

## Data Lifecycle

- Active artifacts remain upserted and refreshable.
- Enrichment/classification snapshots update on refresh.
- Audit events are immutable and retained per compliance policy.