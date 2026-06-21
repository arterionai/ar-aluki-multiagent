# Feature Specification: Link Capture and Secure Enrichment

Feature ID: SB-009A
Status: Draft
Date: 2026-06-21

## 1. Objective

Capture user-shared links with their conversational context, resolve yes/no confirmations exactly once, and enrich link metadata safely so recalled results remain useful, grounded, and tenant-safe.

## 2. Architecture Adaptation

- KnowledgeAgent orchestrates link intent handling and confirmation routing, while side effects are executed through skill contracts.
- Link capture state is isolated in a dedicated link state domain to prevent cross-feature state contamination.
- Durable workflow handles wait-based confirmation windows, expiry, and resume behavior.
- Security and governance policies evaluate enrichment targets before outbound fetches.
- All persisted artifacts and transitions remain tenant-scoped and auditable.

## Clarifications

### Session 2026-06-21

- Q: How is one-time yes/no confirmation enforced? → A: First explicit yes/no consumes the active pending confirmation atomically; later yes/no replies for the same pending item return already-resolved outcome with no side effects.
- Q: What is the enrichment timeout and fallback behavior? → A: Enrichment has a 4-second limit per link; on timeout, capture completes with fallback metadata and a timeout status, and recall remains available with explicit limited-metadata reason.
- Q: How are duplicate links handled? → A: Canonical URL duplicates are upserted per tenant instead of creating new active artifacts; provenance and context references are merged and deduplicated.
- Q: What fields are mandatory in recall output? → A: Each recalled link must include canonical full URL, human-readable description (enriched or fallback), enrichment status/reason, and provenance reference.

## 3. In Scope

- URL detection from inbound messages and canonical normalization for storage.
- Explicit pending confirmation flow with one-time yes/no resolution.
- Secure metadata enrichment (title, site, description) for allowed destinations.
- Recall responses that include full URL identity plus meaningful description and provenance.

## 4. Out of Scope

- Deep crawling beyond the directly shared URL.
- Authenticated scraping of user-protected external systems.

## 5. User Scenarios & Testing

### User Story 1 - Save Link with User Context (Priority: P1)

When a user shares a URL with conversational context, the system captures one normalized link artifact with the user's context, preserving tenant scope and provenance.

**Why this priority**: This is the core value path. Without reliable capture, enrichment and recall are irrelevant.

**Independent Test**: Can be fully tested by sending a URL with descriptive text and verifying one persisted link artifact includes normalized URL, context label, tenant scope, and source message provenance.

**Acceptance Scenarios**:

1. **Given** a tenant-scoped user sends a message containing one valid URL and context text, **When** capture is processed, **Then** one link artifact is stored with canonical URL, context label, and source provenance.
2. **Given** a user sends a URL that is already normalized-equivalent to an existing link for the same tenant and context window, **When** capture is processed, **Then** the operation remains idempotent and does not create duplicate active artifacts.
3. **Given** a user shares a canonical-equivalent URL again with new conversational context, **When** capture is processed, **Then** the existing link artifact is upserted and context/provenance references are merged without creating a second active link artifact.

---

### User Story 2 - Resolve Confirmation Once (Priority: P1)

When a pending link confirmation exists, a yes/no reply resolves the pending request exactly once and does not re-open or loop.

**Why this priority**: Confirmation loops degrade trust and can cause repeated side effects.

**Independent Test**: Can be tested by creating one pending confirmation, replying "yes" or "no", and verifying the pending state closes once with no repeated prompt loop.

**Acceptance Scenarios**:

1. **Given** one active pending confirmation for a user session, **When** the user replies "yes", **Then** the system resolves the pending item once, applies the intended action, and marks the pending state closed.
2. **Given** one active pending confirmation for a user session, **When** the user replies "no", **Then** the system resolves the pending item once, does not perform the save action, and marks the pending state closed.
3. **Given** a pending confirmation has expired, **When** a delayed yes/no message arrives, **Then** the system does not execute a blind save and instead responds with an explicit expired-state outcome.
4. **Given** a pending confirmation has already reached a terminal state, **When** additional yes/no replies arrive, **Then** the system returns an already-resolved response and performs no additional side effects.

---

### User Story 3 - Recall Full Link Identity (Priority: P2)

When users recall links by topic, results return full URL identity and useful description so users can safely distinguish similar links.

**Why this priority**: High-quality recall improves practical usability of captured knowledge.

**Independent Test**: Can be tested by capturing multiple links on related topics and requesting recall to verify each result includes full URL and meaningful description.

**Acceptance Scenarios**:

1. **Given** multiple captured links exist for a topic, **When** a user requests recall, **Then** each returned result includes full normalized URL and non-empty description.
2. **Given** enrichment was blocked by policy for a link, **When** recall is requested, **Then** the link can still be returned with explicit limited-metadata indication and provenance.
3. **Given** enrichment timed out for a link, **When** recall is requested, **Then** the link is returned with fallback description plus explicit timeout status and provenance.

## 6. Edge Cases

- Message contains malformed or unsupported URL formats.
- Message contains multiple URLs in one payload.
- Same URL arrives repeatedly via webhook retries.
- User responds with non-yes/no text while a confirmation is pending.
- Confirmation response arrives after timeout.
- URL resolves to private, local, loopback, or otherwise disallowed destination.
- Enrichment target is unavailable or times out.
- Recall query matches links where metadata is partially missing.

## 7. Requirements

### 7.1 Functional Requirements

- **FR-001**: The system MUST detect URLs from inbound tenant-scoped messages and normalize them into a canonical form before persistence.
- **FR-002**: The system MUST persist each captured link artifact with tenant scope, context scope, creator identity, source channel, and provenance message reference.
- **FR-003**: The system MUST enforce idempotent link capture for duplicate deliveries of the same inbound event and canonical URL combination.
- **FR-004**: The system MUST create at most one active pending confirmation per session-conversation scope for link confirmation handling.
- **FR-005**: The system MUST resolve a pending confirmation exactly once when receiving an explicit yes/no response and MUST transition the pending item to a terminal resolved state.
- **FR-005a**: The first explicit yes/no response mapped to an active pending confirmation MUST consume that pending item atomically, and later yes/no responses for the same pending item MUST be side-effect free.
- **FR-006**: The system MUST reject late yes/no responses for expired pending confirmations and MUST return an explicit expired outcome without side effects.
- **FR-007**: The system MUST support secure enrichment of allowed links to collect title, site, and description metadata.
- **FR-008**: The system MUST evaluate enrichment destinations against security policy and MUST skip disallowed destinations.
- **FR-008a**: The system MUST enforce a maximum enrichment wait time of 4 seconds per link attempt and MUST complete capture without blocking beyond that limit.
- **FR-008b**: When enrichment times out or fails, the system MUST persist fallback metadata and explicit enrichment status so recall remains usable.
- **FR-009**: The system MUST audit every enrichment skip decision and every pending confirmation resolution outcome.
- **FR-010**: The system MUST return recall results with full normalized URL and available description for each matched link.
- **FR-010a**: Recall results MUST include enrichment status and reason when metadata is limited by policy block, timeout, or fetch failure.
- **FR-011**: The system MUST include provenance reference for recalled links so responses remain grounded.
- **FR-012**: The system MUST keep link-capture state isolated from non-link domains to avoid cross-domain state contamination.
- **FR-013**: The system MUST emit telemetry for capture, confirmation resolution, enrichment attempt, enrichment policy decision, and recall rendering outcomes.
- **FR-014**: Canonical-equivalent link submissions in the same tenant scope MUST upsert one active link artifact and merge provenance/context references without creating duplicate active artifacts.
- **FR-015**: Duplicate deliveries of the same canonical link event MUST return a deterministic idempotent outcome indicating no new active artifact was created.

### 7.2 Security and Governance Requirements

- **SG-001**: All link-capture operations MUST require resolved principal context before execution.
- **SG-002**: All data reads and writes for this feature MUST enforce tenant and context constraints.
- **SG-003**: Policy evaluation MUST occur before outbound metadata retrieval is attempted.
- **SG-004**: The feature MUST externalize secrets and MUST NOT embed credentials in feature logic or artifacts.

### 7.3 Operational State Requirements

- **OS-001**: Pending confirmation lifecycle MUST include at minimum `pending`, `resolved_yes`, `resolved_no`, and `expired` terminally consistent outcomes.
- **OS-002**: Transition timestamps and actor/system cause MUST be stored for each state change.
- **OS-003**: Retryable enrichment failures MUST produce explicit retry/stop outcomes without duplicate link creation.
- **OS-004**: Enrichment outcome state MUST include at minimum `enriched`, `policy_blocked`, `timeout`, and `failed` to support deterministic recall rendering.

## 8. Key Entities

- **LinkArtifact**: Captured URL memory artifact including canonical URL, user context label, enrichment metadata, and provenance references under tenant scope.
- **LinkPendingConfirmation**: Wait-state artifact representing a single unresolved yes/no decision window for a link-related action.
- **EnrichmentPolicyDecision**: Policy outcome record indicating whether enrichment was allowed or skipped, with reason code.
- **LinkRecallView**: User-facing result projection containing full URL identity, description, and provenance reference.

## 9. Success Criteria

### Measurable Outcomes

- **SC-001**: 99% of valid single-URL inbound messages result in exactly one persisted link artifact with canonical URL and provenance.
- **SC-002**: 100% of confirmation requests are resolved into a terminal state (`resolved_yes`, `resolved_no`, or `expired`) with no repeated confirmation loop for the same pending item.
- **SC-003**: 100% of enrichment attempts for disallowed destinations are blocked before outbound retrieval and produce an auditable decision record.
- **SC-003a**: 100% of enrichment attempts either complete within 4 seconds or exit with explicit timeout status while preserving captured link availability for recall.
- **SC-004**: At least 95% of recall responses for captured links include both full URL and non-empty description when enrichment is allowed and reachable.
- **SC-005**: 100% of recalled link items include provenance references so users can trace origin context.
- **SC-006**: 100% of post-resolution yes/no replies for the same pending item produce no additional side effects.
- **SC-007**: 100% of duplicate canonical-link submissions in tenant scope do not create more than one active link artifact.

## 10. Assumptions

- Inbound normalization and message provenance fields are already available from the existing channel ingestion baseline.
- Recall behavior in this feature remains grounded by existing platform citation/provenance enforcement.
- Confirmation windows use platform-standard wait patterns and expiration handling already available in long-running workflow capabilities.
- User yes/no intents are interpreted from explicit affirmative or negative replies; ambiguous replies are treated as non-resolving input.
- Fallback description for limited metadata is user-readable and explicitly indicates when enrichment was policy-blocked, timed out, or failed.

## 11. Constitution and Baseline Alignment

- **Skill-First Execution**: Business side effects are framed as skill-executed actions, with agent orchestration only.
- **Tenant-Scoped Security**: Requirements enforce principal context, tenant/context constraints, and auditability.
- **Durable Separation**: Wait-based confirmations are modeled as workflow lifecycle, not live-session blocking logic.
- **Grounded Memory**: Recall requirements require full URL identity plus provenance.
- **Observable Operations**: Telemetry and auditable policy decisions are mandatory for critical paths.
