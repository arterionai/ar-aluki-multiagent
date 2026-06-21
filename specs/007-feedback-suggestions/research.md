# Research Report: Feedback Suggestions Capture (007)

**Date**: 2026-06-21 | **Status**: Complete | **Spec Session**: 2026-06-21

## Context

This research phase validates technical decisions from the feature specification against the Aluki Runtime baseline and resolves implementation dependencies.

## Technology Stack

### Language & Runtime (Non-Negotiable)
- **Decision**: .NET 10 LTS
- **Rationale**: Aluki Runtime Constitution v1.1.0 mandates .NET 10 LTS for all services. All new feature services must align with this baseline.
- **Alternatives Considered**: Node.js (JavaScript), Python, Java were evaluated in baseline phase; .NET 10 LTS selected for unified runtime, Orleans integration, and Azure Functions compatibility.

### Persistence Layer (Mandatory)
- **Decision**: PostgreSQL + pgvector + Row-Level Security (RLS)
- **Rationale**: Constitution Principle II (Tenant-Scoped Security by Default) requires RLS enforcement at data layer. PostgreSQL pgvector supports semantic search for future suggestion enrichment.
- **Dependencies**:
  - Connection string: `PostgresConnectionString` from Key Vault
  - Schema: New tables `suggestions`, `suggestion_attachments`, `suggestion_state_transitions`
  - RLS policies: `tenant_id` + `user_id` composite scope

### Attachment Storage
- **Decision**: Azure Blob Storage (reference-based) with SHA-256 content hash tracking
- **Rationale**: Spec section 7.1 mandates blob reference storage with 90-day expiry lifecycle. Aluki baseline uses `BlobConnectionString` from Key Vault.
- **Implementation**:
  - Text payloads (≤5 KB): Inline in suggestion record
  - Audio (≤50 MB), Photos (≤10 MB): Blob references with metadata
  - Lifecycle: 90-day auto-expiry unless explicitly retained
  - Content tracking: SHA-256 hash for deduplication and lifecycle management

### Orchestration Pattern
- **Decision**: FeedbackAgent (Orleans grain) + SuggestionSkill (explicit skill) + AuditLogSkill
- **Rationale**: Constitution Principle I (Skill-First Execution) requires explicit skills for all side effects. FeedbackAgent orchestrates intent detection and state transitions; skills own payload storage, deduplication, and audit emission.
- **Lifecycle Context**: 30-minute active suggestion window per tenant-user pair (spec section 7.2)

### State Management & Idempotency
- **Decision**: Composite key (message_id + payload_hash) at inbound webhook layer; 5-minute idempotency window
- **Rationale**: Spec section 8.1 defines duplicate detection as message ID + SHA-256 hash composite. 5-minute window matches WhatsApp webhook retry SLA.
- **Audit Trail**: All ingestion attempts logged (accepted, ignored, error) with timestamp and reason

## Integration Points

### 1. WhatsApp Inbound Handler
- Spec 001-whatsapp-capture already defines webhook payload ingestion
- This feature extends existing handler: if suggestion intent detected, route to FeedbackAgent instead of MemoryAgent
- Contract: See `contracts/whatsapp-inbound-contract.yaml`

### 2. Memory Recall System
- Spec 002-personal-memory defines recall query execution
- Exclusion logic: Suggestions in `captured` state onward never appear in recall result sets (spec 9.2)
- Query filter: `WHERE artifact_type != 'suggestion'`

### 3. Audit & Observability
- AuditLogSkill (shared cross-feature) records all state transitions with actor, prior state, new state, timestamp, reason
- Telemetry events: suggestion_captured, suggestion_enriched, suggestion_sent_user, suggestion_archived (for cost and observability tracking)

## Remaining Dependencies

### Pre-Requisites (Blockers)
1. **Database migrations**: New schema tables required before deployment
   - Handled by: `db/migrations/[###]_init_suggestions.sql`
   - Target: Same PostgreSQL instance as 001-whatsapp-capture
2. **Blob Storage lifecycle policy**: 90-day auto-delete must be configured in Azure Storage Account
   - Target resource: `staralukidev6155` (from baseline)
3. **FeedbackAgent Orleans grain definition**: New grain interface + implementation
   - Grain type: Stateful with tenant+user composite key
   - Lifecycle: Activated on first suggestion, deactivated after 30-minute window expiry

### Design Dependencies (Resolved)
- ✅ Tenant-scoped security model (Constitution Principle II)
- ✅ Skill contract patterns (Constitution Principle I)
- ✅ State transition audit trail (Constitution Principle III: Grounded Memory)
- ✅ Idempotency strategy (spec section 8.1)
- ✅ Payload reference storage pattern (spec section 7.1)

## Decision Log

| Decision | Rationale | Impact |
|----------|-----------|--------|
| PostgreSQL + RLS for suggestions domain | Tenant-scoped security by default (Constitution II) | Requires migration scripts and RLS policy DDL |
| Azure Blob Storage for attachments (ref-based) | Scalability + lifecycle management (spec 7.1) | Requires storage lifecycle policy + reference tracking in suggestions table |
| Composite key deduplication (msg_id + hash) | WhatsApp delivery guarantees + idempotency (spec 8.1) | Requires tracking at inbound webhook layer; 5-min window |
| 30-minute active suggestion window per user | Simplifies state machine; prevents accidental context drift (spec 7.2) | Requires timer/lease mechanism in Orleans grain |
| Four observable states (captured → enriched → sent_user → archived) | Visibility model aligns with audit & recall exclusion (spec 9) | All state transitions must emit audit events |
| FeedbackAgent handles orchestration | Separation of concerns: Agent routes, Skills own side effects (Constitution I) | Requires explicit skill contracts for storage, dedup, audit |

## Next Phase

Design artifacts (data-model.md, contracts/, quickstart.md) will operationalize these decisions and establish concrete database schemas, API contracts, and validation scenarios.
