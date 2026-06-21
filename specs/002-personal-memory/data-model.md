# Data Model: Personal Memory and Grounded Recall

## Entity: MemoryArtifact
Represents canonical tenant/context-scoped memory persisted from `note-to-store` interactions.

Fields:
- `memory_artifact_id` (UUID, PK)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `source_channel` (string, required)
- `source_identity` (string, required)
- `canonical_chain_id` (UUID, required)
- `chain_version` (int, required, >= 1)
- `content_text` (text, nullable for non-text payloads)
- `content_locale` (string, nullable)
- `captured_at_utc` (timestamp with timezone, required)
- `updated_at_utc` (timestamp with timezone, required)
- `deleted_at_utc` (timestamp with timezone, nullable)
- `deletion_reason` (string, nullable)
- `provenance_ref` (string, required)
- `correlation_id` (string, required)

Validation rules:
- Unique canonical source identity per scope: `(tenant_id, source_channel, source_identity)`.
- `chain_version` increments by exactly 1 on update.
- Deleted artifacts (`deleted_at_utc` not null) are excluded from retrieval and citations.

State transitions:
- `captured` -> `updated` (same `canonical_chain_id`, increment version)
- `captured|updated` -> `deleted`

## Entity: RecallQuery
Represents a scoped user request for grounded memory retrieval.

Fields:
- `recall_query_id` (UUID, PK)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, required)
- `query_text` (text, required)
- `requested_at_utc` (timestamp with timezone, required)
- `correlation_id` (string, required)
- `source_channel` (string, required)

Validation rules:
- Scope fields must match resolved `PrincipalContext`.
- Empty/blank query text is invalid.

## Entity: EvidenceCitation
Maps recall claims to supporting artifacts.

Fields:
- `citation_id` (UUID, PK)
- `recall_query_id` (UUID, FK -> RecallQuery)
- `claim_id` (string, required)
- `memory_artifact_id` (UUID, FK -> MemoryArtifact)
- `citation_rank` (int, required, >= 1)
- `is_corroborating` (bool, required)

Validation rules:
- Confirmed claim requires at least two distinct corroborating artifacts.
- Cited artifacts must be non-deleted and in authorized scope.

## Entity: TopicCluster
Logical grouping for coherent recall outputs.

Fields:
- `topic_cluster_id` (UUID, PK)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `topic_label` (string, required)
- `cluster_score` (decimal, required)
- `generated_at_utc` (timestamp with timezone, required)

Validation rules:
- Clusters are scoped by `(tenant_id, context_id)`.
- Returned grouped results include at least one citation-backed item.

## Entity: DeletionMarker
Durable record for memory deletion eligibility enforcement.

Fields:
- `deletion_marker_id` (UUID, PK)
- `memory_artifact_id` (UUID, FK -> MemoryArtifact)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `deleted_by_user_id` (UUID, required)
- `deleted_at_utc` (timestamp with timezone, required)
- `reason` (string, nullable)

Validation rules:
- Every deleted artifact must have one deletion marker.
- Retrieval engine must ignore marked artifacts.

## Entity: MemoryAuditEvent
Compliance and operational evidence for side effects and denials.

Fields:
- `audit_event_id` (UUID, PK)
- `event_name` (string, required)
- `tenant_id` (UUID, required)
- `context_id` (UUID, required)
- `user_id` (UUID, nullable for unresolved principal)
- `skill_name` (string, required)
- `result` (string, required)
- `correlation_id` (string, required)
- `occurred_at_utc` (timestamp with timezone, required)
- `payload_json` (jsonb, required)

Validation rules:
- Denied operations must include scope identifiers and denial reason.
- Side-effecting skills must emit at least one audit event.

## Relationships
- `RecallQuery` 1..* `EvidenceCitation`
- `EvidenceCitation` *..1 `MemoryArtifact`
- `TopicCluster` groups many `MemoryArtifact` items by `(tenant_id, context_id)`
- `MemoryArtifact` 1..0..1 `DeletionMarker`
- `MemoryAuditEvent` references skill executions touching any of the above entities
