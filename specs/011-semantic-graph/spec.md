# Spec: Semantic Graph - Entity Resolution and Knowledge Graph

**SB-ID**: SB-011  
**Feature**: F4 - Grafo semántico de entidades y relaciones  
**Status**: Ready for Phase 2  
**Date**: 2026-06-21  
**Owner**: Knowledge Graph Team

## Executive Summary

Implement a tenant-scoped semantic graph layer that resolves entity mentions across messages and facts, maintains relationships, and enables traversal queries. This feature is **foundational for personal-memory recall** — without entity resolution, facts remain isolated and recall becomes keyword-only without semantic understanding.

**Launch Requirement**: F4 must be complete before Phase 2 implementation of `002-personal-memory` begins. The two features are sequentially dependent: semantic graph provides the entity infrastructure that personal memory uses for disambiguation and relationship queries.

---

## 1. Problem Statement

### Current State (Without Semantic Graph)

User captures facts across multiple messages:
```
Message 1: "Met with John Smith from Acme Corp about project X"
Message 2: "John sent me the Acme proposal"
Message 3: "Call with John next week to discuss Acme timeline"
```

**Without semantic graph**, each mention is stored as a string:
- Fact 1: `{"person": "John Smith", "organization": "Acme Corp", ...}`
- Fact 2: `{"person": "John", "organization": "Acme", ...}`
- Fact 3: `{"person": "John", "organization": "Acme", ...}`

**Problem**: System cannot know that "John", "John Smith", and mentions in later facts all refer to the same person.

### Desired State (With Semantic Graph)

```
Entity: Person "John Smith" (id: entity-john-001)
  ├─ Aliases: ["John", "John Smith"]
  ├─ Relationships:
  │  └─ worksAt: Acme Corp (confidence: 0.98)
  └─ Facts linked: [fact-1, fact-2, fact-3]

Entity: Organization "Acme Corp" (id: entity-acme-001)
  ├─ Aliases: ["Acme", "Acme Corp"]
  ├─ Relationships:
  │  └─ hasEmployee: John Smith
  └─ Facts linked: [fact-1, fact-2]
```

**Benefit**: When user asks "What did Acme send?", system resolves "Acme" → entity-acme-001 and retrieves all facts where this entity appears.

---

## 2. Requirements

### F-4.1: Entity Recognition and Deduplication

**Description**:
- Extract entities (persons, organizations, places, concepts) from facts and messages
- Deduplicate mentions across messages (e.g., "John", "John Smith" → same entity)
- Support manual entity merging (user confirms aliases)

**Acceptance Criteria**:
- System extracts at least **4 entity types** (person, organization, location, date-range concept)
- Deduplication accuracy >= **95%** for exact/substring matches
- Manual merge operation completes within 2 seconds
- Every entity is tenant-scoped and inaccessible to other tenants

### F-4.2: Relationship Extraction and Storage

**Description**:
- Identify relationships between entities (e.g., "John works at Acme")
- Store relationships with confidence scores and source attribution
- Support bidirectional traversal (John → works at → Acme; Acme → has employee → John)

**Acceptance Criteria**:
- At least **5 relationship types** supported (worksAt, owns, mentions, collaboratesWith, manages)
- Relationship confidence scores from LLM extraction
- Bidirectional index maintained for traversal queries
- Relationships immutable once persisted (audit trail for changes)

### F-4.3: Graph Traversal and Path Finding

**Description**:
- Query relationships: "Show all people who work at Acme"
- Path finding: "How is John connected to Project X?"
- Relationship explanation: Why are these entities connected?

**Acceptance Criteria**:
- Traversal queries return results in < 500ms (single hop) or < 2s (multi-hop)
- Path finding supports up to 3-hop traversals
- Explanation includes source facts with citations

### F-4.4: Relationship Lifecycle Management

**Description**:
- Relationship refinement: User confirms/rejects auto-detected relationships
- Relationship archival: Mark as outdated when user contradicts (e.g., "John no longer works at Acme")
- Cascading updates: When one entity merges into another, update all inbound relationships

**Acceptance Criteria**:
- User confirmation prompt for low-confidence relationships (< 0.7)
- Archival preserves history but marks as inactive
- Merge operation updates all relationship references atomically

---

## 3. Architecture

### 3.1 Core Concepts

```csharp
// Entity: Resolved representation of a thing
record Entity(
    Guid EntityId,
    string TenantId,
    string EntityType,      // person, organization, location, concept
    string CanonicalName,   // "John Smith"
    List<string> Aliases,   // ["John", "JS"]
    string Description,     // Wikipedia-like summary
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Active = true
);

// Relationship: Directed connection between entities
record Relationship(
    Guid RelationshipId,
    string TenantId,
    Guid SourceEntityId,
    Guid TargetEntityId,
    string RelationType,    // worksAt, owns, mentions, etc.
    double Confidence,      // 0.0-1.0, from LLM or user
    List<Guid> SourceFactIds,  // Which facts support this relationship?
    string Explanation,     // "Mentioned in message: 'John works at Acme'"
    bool Active = true,
    DateTime CreatedAt,
    DateTime ArchivedAt = null
);

// Entity Resolution Job: Async extraction and deduplication
record EntityResolutionJob(
    Guid JobId,
    string TenantId,
    List<Guid> FactIds,     // Facts to process
    EntityResolutionStatus Status,
    List<EntityResolutionResult> Results,
    DateTime CreatedAt,
    DateTime CompletedAt = null
);

enum EntityResolutionStatus
{
    Pending,
    Processing,
    AwaitingUserConfirmation,
    Completed,
    Failed
}
```

### 3.2 Skills

#### EntityResolutionSkill

**Input**:
```json
{
  "fact_ids": ["fact-001", "fact-002"],
  "text_content": "Met with John Smith from Acme Corp",
  "tenant_id": "tenant-001",
  "idempotency_key": "resolution-20260621-tenant001-abc123"
}
```

**Output**:
```json
{
  "entities": [
    {
      "entity_id": "entity-john-001",
      "canonical_name": "John Smith",
      "type": "person",
      "confidence": 0.98,
      "aliases": ["John", "JS"],
      "status": "created_or_existing"
    },
    {
      "entity_id": "entity-acme-001",
      "canonical_name": "Acme Corp",
      "type": "organization",
      "confidence": 0.95,
      "aliases": ["Acme"],
      "status": "existing"
    }
  ],
  "relationships": [
    {
      "source_entity_id": "entity-john-001",
      "target_entity_id": "entity-acme-001",
      "type": "worksAt",
      "confidence": 0.92,
      "explanation": "Text mentions 'John Smith from Acme Corp'"
    }
  ]
}
```

**Failure Modes**:
- Entity type unrecognized → Log warning, skip that entity
- Relationship type unsupported → Store as `generic_relationship`
- Tenant context missing → Fail closed with audit event

#### RelationshipInferenceSkill

**Input**:
```json
{
  "relationship_id": "rel-001",
  "explanation_depth": "deep",
  "max_hops": 2,
  "tenant_id": "tenant-001"
}
```

**Output**:
```json
{
  "relationship": {
    "source_entity": "John Smith",
    "target_entity": "Acme Corp",
    "type": "worksAt",
    "confidence": 0.92
  },
  "path_evidence": [
    {
      "hop": 1,
      "entities": ["John Smith", "Acme Corp"],
      "fact_id": "fact-001",
      "citation": "Message: 'Met with John Smith from Acme Corp'"
    }
  ],
  "related_relationships": [
    {
      "source": "Acme Corp",
      "target": "Project X",
      "type": "owns"
    }
  ]
}
```

### 3.3 Database Schema

```sql
-- Entities
CREATE TABLE semantic_entities (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    entity_type VARCHAR(50) NOT NULL,  -- person, organization, location, concept
    canonical_name VARCHAR(500) NOT NULL,
    description TEXT,
    active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    
    INDEX idx_tenant_entity_type (tenant_id, entity_type, active),
    INDEX idx_tenant_canonical (tenant_id, canonical_name)
);

-- Entity Aliases
CREATE TABLE semantic_entity_aliases (
    id UUID PRIMARY KEY,
    entity_id UUID NOT NULL REFERENCES semantic_entities(id),
    alias VARCHAR(500) NOT NULL,
    confidence NUMERIC(3,2) NOT NULL,  -- 0.00-1.00
    
    FOREIGN KEY (entity_id) REFERENCES semantic_entities(id) ON DELETE CASCADE,
    INDEX idx_entity_alias (entity_id, alias),
    INDEX idx_tenant_alias (tenant_id, alias)
);

-- Relationships
CREATE TABLE semantic_relationships (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    source_entity_id UUID NOT NULL REFERENCES semantic_entities(id),
    target_entity_id UUID NOT NULL REFERENCES semantic_entities(id),
    relationship_type VARCHAR(100) NOT NULL,
    confidence NUMERIC(3,2) NOT NULL,
    explanation TEXT,
    active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    archived_at TIMESTAMP WITH TIME ZONE,
    
    FOREIGN KEY (source_entity_id) REFERENCES semantic_entities(id),
    FOREIGN KEY (target_entity_id) REFERENCES semantic_entities(id),
    INDEX idx_tenant_source_target (tenant_id, source_entity_id, target_entity_id),
    INDEX idx_tenant_target_source (tenant_id, target_entity_id, source_entity_id)
);

-- Entity-to-Fact Links (provenance)
CREATE TABLE semantic_entity_facts (
    id UUID PRIMARY KEY,
    entity_id UUID NOT NULL REFERENCES semantic_entities(id),
    fact_id UUID NOT NULL,
    tenant_id UUID NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    
    FOREIGN KEY (entity_id) REFERENCES semantic_entities(id) ON DELETE CASCADE,
    INDEX idx_entity_fact (entity_id, fact_id),
    INDEX idx_fact_entity (fact_id, entity_id)
);

-- Enable RLS on all tables
ALTER TABLE semantic_entities ENABLE ROW LEVEL SECURITY;
ALTER TABLE semantic_entity_aliases ENABLE ROW LEVEL SECURITY;
ALTER TABLE semantic_relationships ENABLE ROW LEVEL SECURITY;
ALTER TABLE semantic_entity_facts ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_entities ON semantic_entities
    FOR ALL USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- Similar policies for other tables...
```

---

## 4. Integration with Personal Memory

### Dependency: 002-personal-memory depends on 011-semantic-graph

When `002-personal-memory` persists a fact, it MUST:
1. Call `EntityResolutionSkill` to extract entities
2. Call `RelationshipInferenceSkill` to extract relationships
3. Link entities back to the fact via `semantic_entity_facts` table
4. Index entity mentions for later recall

**Example workflow**:
```
User: "John at Acme sent me the proposal"

Step 1: PersonalMemory.PersistFactSkill
  → fact_id = "fact-123"
  → fact_text = "John at Acme sent me the proposal"

Step 2: EntityResolutionSkill
  → Emit: entities=[John, Acme], relationships=[John worksAt Acme]
  → Create entity links: [entity-john → fact-123], [entity-acme → fact-123]

Step 3: Memory recall "What did Acme send?"
  → Query: SELECT facts WHERE entity_id IN (SELECT id FROM entities WHERE canonical_name ILIKE '%Acme%')
  → Return: "Acme sent me the proposal" with entity-acme highlighted
```

---

## 5. Data Model

### Input: Facts from `002-personal-memory`

```json
{
  "fact_id": "fact-123",
  "tenant_id": "tenant-001",
  "fact_text": "John at Acme sent me the proposal",
  "fact_type": "decision",
  "source_message_id": "msg-456",
  "created_at": "2026-06-21T14:32:10Z"
}
```

### Output: Entity Graph

```json
{
  "entities": [
    {
      "id": "entity-john-001",
      "tenant_id": "tenant-001",
      "type": "person",
      "canonical_name": "John Smith",
      "aliases": ["John", "JS"],
      "fact_links": ["fact-123", "fact-124"],
      "relationships": [
        {
          "type": "worksAt",
          "target_entity": "entity-acme-001",
          "confidence": 0.92
        }
      ]
    },
    {
      "id": "entity-acme-001",
      "type": "organization",
      "canonical_name": "Acme Corp",
      "aliases": ["Acme"],
      "fact_links": ["fact-123"]
    }
  ]
}
```

---

## 6. Acceptance Criteria

| Criterion | Metric | Target |
|---|---|---|
| **Entity deduplication** | Accuracy | >= 95% for exact/substring matches |
| **Relationship extraction** | Supported types | >= 5 (worksAt, owns, mentions, collaboratesWith, manages) |
| **Query latency (single hop)** | P95 | < 500ms |
| **Query latency (3-hop path)** | P95 | < 2s |
| **Entity isolation** | Breach attempt | 0 (all cross-tenant queries denied) |
| **Relationship immutability** | Audit trail | 100% of changes logged |
| **Confident relationships** | Accuracy | >= 90% for confidence >= 0.8 |

---

## 7. Testing Strategy

### Unit Tests
- Entity deduplication logic (exact match, substring, phonetic)
- Relationship type validation
- Alias conflict detection

### Integration Tests
- End-to-end: Fact → Entity extraction → Graph persistence → Query
- Entity merging (deduplicate after creation)
- Relationship archival (user feedback integration)
- Cross-fact relationship propagation

### Contract Tests
- EntityResolutionSkill input/output validation
- RelationshipInferenceSkill path-finding correctness
- Graph traversal correctness (bidirectional edges)

---

## 8. Constitution Check

✅ **Principle I: Skill-First Execution**  
Entity resolution, relationship inference, and graph traversal are explicit skill contracts.

✅ **Principle II: Tenant-Scoped by Default**  
All entities scoped by tenant; RLS enforced; cross-tenant queries denied.

✅ **Principle III: Grounded Memory**  
Entity-to-fact links provide provenance; relationships traced to source facts.

✅ **Principle IV: Durable Session vs Workflow**  
Graph persistence is durable (PostgreSQL); traversal queries are fast-path (Orleans cache possible).

✅ **Principle V: Cost-Aware Intelligence**  
Entity extraction can use value models; relationship inference via LLM is charged to billing.

✅ **Principle VI: Observable and Governed**  
All entity operations audit-logged; access denials recorded.

**Gate Result**: ✅ **PASS**

---

## 9. Dependencies & Blockers

### Hard Dependencies
- PostgreSQL with pgvector support (for future semantic search)
- LLM extraction capability (from `004-ai-extraction`)

### Blocking Downstream Features
- `002-personal-memory` cannot reach Phase 2 without this

### No Blockers Identified

---

## 10. Timeline

| Phase | Duration | Deliverable |
|---|---|---|
| **Phase 2a** | 2 weeks | Database schema + EntityResolutionSkill + RelationshipInferenceSkill |
| **Phase 2b** | 2 weeks | Graph traversal queries + caching strategy |
| **Phase 2c** | 1 week | Integration with 002-personal-memory + testing |

**Total**: 5 weeks as part of Phase 2

---

## Related Documents

- `specs/000-common/skills-registry.md` - EntityResolutionSkill and RelationshipInferenceSkill
- `specs/002-personal-memory/spec.md` - Dependency documentation
- `specs/000-common/contracts/semantic-graph.yaml` - Skill contracts
