# Plan: Semantic Graph - Entity Resolution and Knowledge Graph

**Branch**: `011-semantic-graph` | **Date**: 2026-06-21 | **Spec**: `specs/011-semantic-graph/spec.md`

**Input**: Feature specification from `specs/011-semantic-graph/spec.md`

## Summary

Implement semantic graph layer with entity deduplication, relationship extraction, and graph traversal queries to support `002-personal-memory` recall with semantic understanding (F4 from BRD). Design uses PostgreSQL with efficient indexing for tenant-scoped entity and relationship storage, LLM-driven extraction for initial relationships, and user-confirmable relationship refinement workflow.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`
- `Microsoft.DurableTask.Client`, `Microsoft.DurableTask.Worker.Grpc` (for async entity resolution jobs)
- `Npgsql` for PostgreSQL with RLS
- `System.Text.Json` for entity/relationship serialization
- Existing LLM integration from `004-ai-extraction` for entity extraction

**Storage**: PostgreSQL (`semantic_entities`, `semantic_entity_aliases`, `semantic_relationships`, `semantic_entity_facts`) with tenant RLS and optimized indexes

**Testing**:
- Unit tests for entity deduplication algorithms (exact, substring, phonetic similarity)
- Unit tests for relationship type validation
- Integration tests for end-to-end fact → entity → relationship → query
- Contract tests for EntityResolutionSkill and RelationshipInferenceSkill
- Performance tests for graph traversal (single-hop < 500ms, 3-hop < 2s)

**Target Platform**: Backend runtime (`src/Aluki.Runtime.Host`, `src/Aluki.Runtime.Functions`) on Azure baseline

**Project Type**: Backend knowledge graph orchestration with durable job coordination for large fact batches

**Performance Goals**:
- Entity resolution for 50+ facts completes in < 10s (async job)
- Single-hop relationship query (P95 < 500ms)
- 3-hop path finding (P95 < 2s)
- Graph traversal remains non-blocking for conversational flow

**Constraints**:
- All entities and relationships are tenant-scoped (RLS mandatory)
- Entity deduplication accuracy >= 95% for exact/substring matches
- Relationship extraction confidence >= 0.8 for auto-acceptance
- Relationship archival preserves history (no deletion)
- Bidirectional relationship index maintained for efficient traversal

**Scale/Scope**:
- Per-tenant entity graphs (independent per organization)
- Support 5+ relationship types (worksAt, owns, mentions, collaboratesWith, manages)
- Entity merge operation (deduplication) triggers cascading relationship updates
- Relationship lifecycle: creation, user refinement, archival

## Constitution Check

*GATE: Must pass before Phase 2 research. Re-check after Phase 1 design.*

### Principle I: Skill-First Execution - PASS
- Entity extraction, relationship inference, and graph traversal are explicit skill contracts.
- No side effects hidden in routing; all entity/relationship operations are skill-driven.

### Principle II: Tenant-Scoped Security by Default - PASS
- All entities and relationships scoped by tenant_id.
- RLS-compatible schema; cross-tenant queries impossible.

### Principle III: Grounded Memory and Provenance - PASS
- Relationships linked to source facts via `semantic_entity_facts` table.
- User can trace why entities are connected (source fact citations).

### Principle IV: Durable Session and Workflow Separation - PASS
- Entity resolution jobs are async (Durable Functions) for large batches.
- Graph traversal queries are synchronous (fast-path in Orleans).

### Principle V: Cost-Aware and Observable Intelligence - PASS
- Entity extraction and relationship inference can use value models.
- Audit events for all entity/relationship operations.

### Principle VI: Azure Deployment Baseline and LTS Runtime - PASS
- Plan targets .NET 10 and existing Azure runtime baseline.

**Initial Gate Result**: PASS

---

## Phase Breakdown

### Phase 2a: Schema & Skill Implementation (2 weeks)
- [ ] PostgreSQL schema creation (entities, aliases, relationships, fact-links)
- [ ] RLS policies for all tables
- [ ] Entity deduplication library (exact match, substring, phonetic)
- [ ] EntityResolutionSkill (extract → deduplicate → store)
- [ ] RelationshipInferenceSkill (extract relationships → calculate confidence)
- [ ] Unit tests for deduplication and relationship type validation

### Phase 2b: Graph Traversal & Querying (2 weeks)
- [ ] Single-hop traversal (followers of a person, companies a person owns)
- [ ] Multi-hop path finding (3-hop max)
- [ ] Relationship explanation (why are entities connected?)
- [ ] Entity merge operation (deduplicate after creation)
- [ ] Performance optimization (indexing, caching strategy)
- [ ] Integration tests for graph queries

### Phase 2c: Integration with Personal Memory (1 week)
- [ ] Fact → Entity resolution pipeline
- [ ] Entity mention indexing for recall
- [ ] User confirmation workflow for low-confidence relationships
- [ ] End-to-end test: Fact → Graph → Recall
- [ ] Documentation and team handoff

---

## Key Dependencies

**Blocking Upstream**: None (independent feature)  
**Blocking Downstream**: `002-personal-memory` (cannot proceed without entity infrastructure)

**External Dependencies**:
- LLM extraction from `004-ai-extraction` team (reuse existing integration)
- PostgreSQL instance with vector support (already available)

---

## Related Specs

- `specs/002-personal-memory/spec.md` - Uses semantic graph for entity-aware recall
- `specs/000-common/skills-registry.md` - EntityResolutionSkill, RelationshipInferenceSkill definitions
- `specs/000-common/contracts/semantic-graph.yaml` - Skill contracts
