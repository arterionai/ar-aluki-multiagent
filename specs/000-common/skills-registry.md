# Global Skills Registry

**Status**: Baseline  
**Version**: 1.0  
**Date**: 2026-06-21

## Overview

This document is the **single source of truth** for all skills defined across Aluki features. Each skill has an explicit contract, ownership feature, and integration points.

## Skill Catalog

### Capture Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **UnifyMessageSkill** | 001-whatsapp-capture | Normalize multi-channel inputs to UMO | `Contracts/whatsapp-umo.yaml` | ✅ Phase 1 |
| **ValidateMessageSkill** | 001-whatsapp-capture | Structural validation, reject malformed | `Contracts/whatsapp-umo.yaml` | ✅ Phase 1 |
| **DetectLanguageSkill** | 001-whatsapp-capture | Auto-detect message language | `Contracts/ai-extraction.yaml` | ✅ Phase 1 |
| **ScopeValidationSkill** | 001-whatsapp-capture | Enforce tenant/principal context pre-persist | `Contracts/security.yaml` | ✅ Phase 1 |
| **IdempotencyKeySkill** | 001-whatsapp-capture | Generate/detect duplicate keys | `Contracts/whatsapp-umo.yaml` | ✅ Phase 1 |
| **AuditLogSkill** | 001-whatsapp-capture | Immutable audit trail for capture events | `Contracts/audit-event-schema.yaml` | ✅ Phase 1 |

### Memory Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **ClassifyMemorySkill** | 002-personal-memory | Categorize fact type (decision, fact, task) | `Contracts/memory-classification.yaml` | ✅ Phase 2 |
| **PersistFactSkill** | 002-personal-memory | Store with provenance + immutable binding | `Contracts/memory-persistence.yaml` | ✅ Phase 2 |
| **RetrieveFactSkill** | 002-personal-memory | Query facts by semantic/keyword search | `Contracts/memory-retrieval.yaml` | ✅ Phase 2 |
| **CorroborateFactSkill** | 002-personal-memory | Validate fact against evidence base | `Contracts/memory-retrieval.yaml` | ✅ Phase 2 |
| **CitationRenderSkill** | 002-personal-memory | Format citations for display (markdown + links) | `Contracts/memory-retrieval.yaml` | ✅ Phase 2 |
| **EntityResolutionSkill** | 011-semantic-graph | Resolve entity mentions, deduplicate | `Contracts/semantic-graph.yaml` | ✅ Phase 2 |
| **RelationshipInferenceSkill** | 011-semantic-graph | Extract and store relationships | `Contracts/semantic-graph.yaml` | ✅ Phase 2 |

### Calendar Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **NLPDateParsingSkill** | 003-calendar-integration | Convert "next Tuesday at 2pm" → datetime | `Contracts/calendar-datetime.yaml` | ✅ Phase 3 |
| **TimezoneResolutionSkill** | 003-calendar-integration | Resolve user timezone for date ambiguity | `Contracts/timezone-standards.md` | ✅ Phase 3 |
| **CalendarAuthSkill** | 003-calendar-integration | OAuth token lifecycle management | `Contracts/calendar-auth.yaml` | ✅ Phase 3 |
| **CalendarCreateSkill** | 003-calendar-integration | Write event to external calendar | `Contracts/calendar-sync.yaml` | ✅ Phase 3 |
| **CalendarSyncSkill** | 003-calendar-integration | Periodic sync check for conflicts | `Contracts/calendar-sync.yaml` | ⏳ Phase 3B |

### Extraction Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **TranscriptionSkill** | 004-ai-extraction | Audio → text (async) | `Contracts/extraction-media.yaml` | ✅ Phase 3 |
| **ExtractionSkill** | 004-ai-extraction | Structured fact extraction from text/media | `Contracts/extraction-facts.yaml` | ✅ Phase 3 |
| **ConfidenceScoringSkill** | 004-ai-extraction | Attach confidence labels to extractions | `Contracts/extraction-facts.yaml` | ✅ Phase 3 |

### Reminder Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **ReminderCreateSkill** | 005-reminders | Validate + persist reminder with quota check | `Contracts/reminders.yaml` | ✅ Phase 3 |
| **ReminderDeliverySkill** | 005-reminders | Match reminder time, deliver via channel | `Contracts/reminders-delivery.yaml` | ✅ Phase 3 |
| **SnoozeReminderSkill** | 005-reminders | Update reminder with new delivery time | `Contracts/reminders.yaml` | ✅ Phase 3 |
| **CompleteReminderSkill** | 005-reminders | Mark reminder as resolved, emit telemetry | `Contracts/reminders.yaml` | ✅ Phase 3 |
| **ReminderQuotaSkill** | 005-reminders | Check tenant active reminder count | `Contracts/quota-coordination.md` | ✅ Phase 3 |

### Delegated Reminder Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **ConsentCheckSkill** | 006-delegated-reminders | Verify recipient has opted in | `Contracts/delegated-reminders.yaml` | ✅ Phase 4 |
| **DelegatedReminderCreateSkill** | 006-delegated-reminders | Persist delegation with audit + consent | `Contracts/delegated-reminders.yaml` | ✅ Phase 4 |
| **DelegatedReminderDeliverySkill** | 006-delegated-reminders | Deliver + track delivery attempt | `Contracts/delegated-reminders-delivery.yaml` | ✅ Phase 4 |
| **RateLimitSkill** | 006-delegated-reminders | Check per-recipient send limit (10/24h) | `Contracts/quota-coordination.md` | ✅ Phase 4 |

### Feedback & Suggestion Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **AttachmentStoreSkill** | 007-feedback-suggestions | Store user attachments (imgs, files) | `Contracts/feedback-storage.yaml` | ✅ Phase 3 |
| **SuggestionPersistSkill** | 007-feedback-suggestions | Store suggestion + metadata separately from memory | `Contracts/feedback-persistence.yaml` | ✅ Phase 3 |
| **SuggestionClassifySkill** | 008a-suggestions-admin | Label suggestion category (bug, feature, ux, other) | `Contracts/suggestion-classification.yaml` | ✅ Phase 4 |
| **RewardCalculationSkill** | 008a-suggestions-admin | Compute reward amount based on classification + metadata | `Contracts/reward-policy.yaml` | ✅ Phase 4 |

### Link Capture Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **LinkCaptureSkill** | 009a-link-capture | Extract + validate URL from message | `Contracts/link-capture.yaml` | ✅ Phase 3 |
| **LinkEnrichmentSkill** | 008b-youtube-links | Fetch metadata (title, description, thumbnail) | `Contracts/link-enrichment.yaml` | ✅ Phase 3 |
| **YoutubeDetectionSkill** | 008b-youtube-links | Identify YouTube URLs specifically | `Contracts/link-enrichment.yaml` | ✅ Phase 3 |
| **LinkConfirmationSkill** | 009a-link-capture | Async confirmation window + deduplication | `Contracts/link-capture.yaml` | ✅ Phase 3 |

### Domain Agent Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **MessageDispatcherSkill** | 009b-domain-agents | Route message to appropriate domain agent | `Contracts/domain-agents.yaml` | ✅ Phase 3 |
| **FallbackAgentSkill** | 009b-domain-agents | General fallback for unrouted messages | `Contracts/domain-agents.yaml` | ✅ Phase 3 |
| **DomainAgentRegistrySkill** | 009b-domain-agents | Maintain registry of available domain agents | `Contracts/domain-agents.yaml` | ✅ Phase 3 |

### Billing Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **EntitlementCheckSkill** | 010-billing | Query current entitlement quota for operation | `Contracts/billing-entitlement.yaml` | ✅ Phase 5 |
| **UsageRecordSkill** | 010-billing | Write immutable ledger entry | `Contracts/billing-ledger.yaml` | ✅ Phase 5 |
| **InvoiceGenerationSkill** | 010-billing | Compute invoice from ledger entries | `Contracts/billing-invoice.yaml` | ✅ Phase 5 |
| **LifecycleTransitionSkill** | 010-billing | Execute package upgrade/downgrade/cancel | `Contracts/billing-lifecycle.yaml` | ✅ Phase 5 |
| **ReconciliationExportSkill** | 010-billing | Generate audit export (invoice → ledger) | `Contracts/billing-reconciliation.yaml` | ✅ Phase 5 |

### Governance Skills

| Skill | Owner | Responsibility | Contract | Status |
|---|---|---|---|---|
| **PolicyDecisionSkill** | 012-governance-security | Evaluate policy rule → allow/deny/warn | `Contracts/policy-decision.yaml` | ✅ Phase 1B |
| **RLSEnforcementSkill** | 012-governance-security | Apply row-level security at query time | `Contracts/security.yaml` | ✅ Phase 1B |
| **ConsentManagementSkill** | 012-governance-security | Store/retrieve consent decisions | `Contracts/consent-management.yaml` | ✅ Phase 1B |
| **AuditQuerySkill** | 012-governance-security | Query audit logs with compliance filters | `Contracts/audit-query.yaml` | ✅ Phase 1B |

---

## Skill Lifecycle

### Definition Phase
Each skill MUST document:
- **Input contract**: What parameters, constraints
- **Output contract**: What result structure
- **Side effects**: DB writes, external API calls
- **Failure modes**: How to handle errors
- **Audit requirements**: What events to emit

### Registration Phase
- Add to `Skill Registry` in container configuration
- Register skill route in `CoordinatorGrain`
- Add integration test covering happy path + one failure scenario

### Deprecation Phase
- Mark as `deprecated: true` in registry
- All callers must migrate within 2 feature releases
- Final removal after 3 months

---

## Skill Composition Rules

### Rule 1: No Direct Skill-to-Skill Calls

❌ **WRONG**:
```csharp
var citationMarkdown = await _citationRenderSkill.Render(fact);
```

✅ **CORRECT**:
```csharp
// Request orchestration through CoordinatorGrain
var response = await _coordinator.ExecuteSkillChain(new[] {
    ("retrieve_fact", retrieveRequest),
    ("corroborate_fact", corroborateRequest),
    ("render_citation", renderRequest)
});
```

### Rule 2: Idempotency Keys Required for Side Effects

All skills that persist state MUST:
1. Accept `idempotency_key` parameter
2. Check for duplicate before side-effect
3. Return same result if key already seen

```csharp
// PersistFactSkill
async Task<FactId> Persist(PersistRequest req)
{
    var existing = await _db.LookupByIdempotencyKey(req.IdempotencyKey);
    if (existing != null)
        return existing.FactId;  // Idempotent
    
    var factId = Guid.NewGuid();
    await _db.InsertFact(factId, req);
    return factId;
}
```

### Rule 3: Audit Events on Every Execution

```csharp
// Every skill MUST emit an audit event
await _auditLog.EmitEvent(new AuditEvent
{
    EventType = "reminder_created",
    SkillName = "ReminderCreateSkill",
    Status = status,  // success | denied | failed
    Duration = stopwatch.ElapsedMilliseconds
});
```

---

## Skills by Phase

| Phase | Count | Skills |
|---|---|---|
| **Phase 1** | 6 | UnifyMessage, ValidateMessage, DetectLanguage, ScopeValidation, IdempotencyKey, AuditLog |
| **Phase 1B** | 4 | PolicyDecision, RLSEnforcement, ConsentManagement, AuditQuery |
| **Phase 2** | 7 | ClassifyMemory, PersistFact, RetrieveFact, CorroborateFact, CitationRender, EntityResolution, RelationshipInference |
| **Phase 3** | 19 | Calendar (5), Extraction (3), Reminder (5), Links (4), Domain Agents (3), Feedback (1 - already in Phase 3) |
| **Phase 4** | 7 | DelegatedReminder (4), Suggestions Admin (3) |
| **Phase 5** | 5 | Billing (5) |
| **TOTAL** | **48** | Comprehensive runtime skill set |

---

## Contract Mapping

Each skill has a formal contract in `contracts/`:

```
contracts/
├── audit-event-schema.yaml              [Shared]
├── whatsapp-umo.yaml                    [001]
├── memory-classification.yaml           [002]
├── memory-persistence.yaml              [002]
├── memory-retrieval.yaml                [002]
├── semantic-graph.yaml                  [011]
├── calendar-datetime.yaml               [003]
├── calendar-auth.yaml                   [003]
├── calendar-sync.yaml                   [003]
├── extraction-media.yaml                [004]
├── extraction-facts.yaml                [004]
├── reminders.yaml                       [005]
├── reminders-delivery.yaml              [005]
├── delegated-reminders.yaml             [006]
├── delegated-reminders-delivery.yaml    [006]
├── feedback-storage.yaml                [007]
├── feedback-persistence.yaml            [007]
├── suggestion-classification.yaml       [008a]
├── reward-policy.yaml                   [008a]
├── link-capture.yaml                    [009a]
├── link-enrichment.yaml                 [008b]
├── domain-agents.yaml                   [009b]
├── billing-entitlement.yaml             [010]
├── billing-ledger.yaml                  [010]
├── billing-invoice.yaml                 [010]
├── billing-lifecycle.yaml               [010]
├── billing-reconciliation.yaml          [010]
├── policy-decision.yaml                 [012]
├── security.yaml                        [012]
├── consent-management.yaml              [012]
└── audit-query.yaml                     [012]
```

---

## Implementation Notes

- Total skill count: **48** (realistic for production runtime)
- Phase 1 focus: Ingestion + foundational governance
- Phase 2 focus: Core memory retrieval with grounding
- Phase 3 focus: Feature breadth (parallel implementation)
- Phase 4+: Extensions and monetization

This registry ensures explicit contracts, avoids skill sprawl, and enables clear testing strategy.
