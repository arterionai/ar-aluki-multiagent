# Feature Specification: YouTube Link Save and Classification

Feature ID: SB-008B
Status: Draft
Date: 2026-06-21

## 1. Objective

Detect YouTube links in user messages, enrich them with best-effort metadata, classify content into structured fields, and persist link artifacts for reliable recall.

## Clarifications

### Session 2026-06-21

- Q: What is the provider fallback order for metadata enrichment? -> A: The system uses a deterministic order: configured primary provider first, configured secondary fallback provider second, then degraded save when both fail.
- Q: How should unsupported YouTube URLs be handled? -> A: Unsupported or malformed YouTube URLs are not persisted, produce an explicit invalid-link user response, and emit an audit event.
- Q: How are duplicate links handled within and across submissions? -> A: Each unique canonical video identity is processed once per message; repeated submissions in the same tenant perform idempotent refresh instead of creating new active records.
- Q: How visible is classification confidence to users? -> A: User confirmations include a confidence label (high, medium, or low) for classification output, and low-confidence fields are explicitly marked uncertain.

## 2. Architecture Adaptation

- KnowledgeAgent detects YouTube URL patterns through deterministic pre-LLM guards.
- LinkCaptureSkill performs canonicalization, dedupe checks, and persistence of video identity artifacts.
- Enrichment runs as a best-effort step with fallback behavior and no hard failure on provider unavailability.
- Classification outputs are stored as structured fields suitable for filtering and downstream retrieval.
- TenantScopeSkill, IdempotencyGuardSkill, and AuditLogSkill enforce tenant boundaries, dedupe behavior, and traceability.

## 3. In Scope

- Detection of common YouTube URL forms in inbound text.
- Canonicalization to a stable video identity.
- Metadata enrichment with fallback behavior.
- Category, tags, and short summary generation.
- Idempotent upsert by tenant and video identity.
- Transparent user confirmation for enriched or degraded outcomes.

## 4. Out of Scope

- Downloading or storing video media payload.
- Personalized recommendation generation.
- Channel-specific rich playback UX.
- Playlist/channel analytics beyond captured link metadata.

## 5. User Scenarios

### US1 - Save enriched YouTube link (P1)
Given a tenant-scoped user sends a valid YouTube URL,
when enrichment is available,
then the system saves one canonical link artifact and returns a confirmation containing title, source URL, and structured classification.

### US2 - Save with fallback metadata (P1)
Given the primary metadata provider is unavailable or unauthorized,
when fallback path is executed,
then the system still saves the canonical link identity and returns a confirmation that marks metadata as partial.

### US3 - Save in degraded mode (P2)
Given both primary and fallback enrichment paths cannot return metadata,
when processing completes,
then the system persists tenant-scoped canonical identity and returns a transparent confirmation with limited details.

## 6. Functional Requirements

### FR1. Link Detection and Normalization
1. The system MUST detect YouTube links from common URL forms (including short and standard host patterns).
2. The system MUST normalize detected links to a canonical video identity before persistence.
3. The system MUST ignore non-YouTube URLs for this feature and leave them to other capture flows.

### FR2. Tenant Scope and Authorization
1. The system MUST require principal context, tenant context, and conversation context before any read or write.
2. The system MUST persist every created or updated link artifact with tenant and context ownership fields.
3. The system MUST reject processing when tenant scope cannot be resolved and emit an auditable denial event.

### FR3. Idempotent Persistence
1. The system MUST upsert by tenant identity plus canonical video identity to prevent duplicates.
2. Re-submitting the same video within the same tenant MUST update freshness metadata without creating a second active record.
3. Submitting the same video across different tenants MUST result in isolated records.

### FR4. Enrichment and Fallback
1. The system MUST attempt enrichment through the configured primary metadata path.
2. If primary enrichment fails, the system MUST execute fallback enrichment through a configured secondary provider before marking enrichment unavailable.
3. The system MUST apply a deterministic enrichment order: primary provider, then secondary provider, then degraded mode if both fail.
4. Enrichment failure MUST NOT block canonical link persistence.

### FR5. Classification and Structured Output
1. The system MUST produce structured classification fields: category, tags, and summary.
2. If classification confidence is insufficient, the system MUST mark uncertain fields explicitly instead of inventing content.
3. Classification outputs MUST be queryable as structured data, not only free text.
4. User-facing confirmation for processed links MUST include a classification confidence label (high, medium, or low).

### FR6. User Confirmation and Transparency
1. The system MUST return an explicit confirmation outcome for each processed link: enriched, partial, or degraded.
2. Confirmation messages MUST indicate when metadata is partial or unavailable.
3. Confirmation MUST include enough information for the user to identify which link was saved.

### FR7. Provenance and Auditability
1. The system MUST retain provenance from saved link artifacts to inbound message evidence.
2. The system MUST emit auditable events for detection, enrichment attempts, fallback usage, persistence result, and final user outcome.
3. Recall and filtering views MUST be able to reference saved link provenance.

## 7. Edge Cases and Failure Handling

- Message contains multiple YouTube links: each unique canonical video identity is processed independently with idempotent upsert.
- Duplicate appearances of the same canonical video identity within one message: only one processing attempt is performed for that identity.
- Unsupported or malformed YouTube URL: no artifact is created; user receives a clear invalid-link response and an audit event is emitted.
- Link missing stable video identity after normalization: processing stops with transparent failure and audit event.
- Duplicate webhook delivery of the same inbound message: no duplicate artifact creation.
- Metadata provider latency/outage: processing continues through fallback or degraded path.

## 8. Key Entities

- SavedLinkArtifact
	- Purpose: tenant-scoped canonical record of a captured YouTube video.
	- Core fields: tenant/context ownership, canonical video identity, canonical URL, lifecycle status, created/updated timestamps.

- LinkEnrichment
	- Purpose: best-effort metadata payload associated with a saved link.
	- Core fields: title, description snippet, channel name, publish date, enrichment status, enrichment source.

- LinkClassification
	- Purpose: structured interpretation for filtering and recall.
	- Core fields: category, tags, summary, confidence marker (high/medium/low), uncertainty markers.

- LinkCaptureAuditEvent
	- Purpose: traceable operational record for compliance and debugging.
	- Core fields: event type, tenant/context scope, link identity reference, timestamp, outcome code.

## 9. Assumptions

- Input links arrive through already-authenticated channel adapters that provide principal context.
- Existing tenant RLS and artifact ownership patterns apply to this feature without introducing new tenancy models.
- Existing recall/filtering services can consume structured link classification fields once persisted.
- Enrichment providers may be intermittently unavailable; degraded-save behavior is accepted and expected.

## 10. Success Criteria

1. At least 99% of valid YouTube submissions create exactly one tenant-scoped canonical saved-link artifact.
2. 100% of duplicate submissions for the same tenant and canonical video identity avoid creating additional active saved-link records.
3. 100% of processed links return one of three explicit outcomes to users: enriched, partial, or degraded.
4. At least 95% of processed links complete and return user confirmation within 2 seconds for non-blocking flows.
5. 100% of saved-link artifacts are traceable to source inbound messages through provenance records.

## 11. Acceptance Criteria

- Duplicate link submissions do not create duplicate saved-link records within the same tenant.
- Duplicate appearances of the same canonical video identity in one inbound message produce one processing result for that identity.
- Classification output is structured and queryable for downstream filtering.
- User confirmation includes visible classification confidence label and uncertainty markers when confidence is low.
- Enrichment failures do not break capture flow; canonical identity is still saved.
- Tenant isolation is preserved for every saved-link, enrichment, and classification artifact.
- User confirmation transparently reflects enriched, partial, or degraded outcome.
