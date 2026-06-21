# Tasks: Semantic Graph (Phase 2)

## Phase 2a: Schema & Skills (2 weeks)

- [ ] **SG-001**: Create PostgreSQL schema (entities, aliases, relationships, fact-links)
- [ ] **SG-002**: Implement RLS policies for semantic tables
- [ ] **SG-003**: Build EntityDeduplicationService (exact, substring, phonetic matching)
- [ ] **SG-004**: Implement EntityResolutionSkill (extract → deduplicate → persist)
- [ ] **SG-005**: Implement RelationshipInferenceSkill (extract relationships → confidence)
- [ ] **SG-006**: Unit tests for deduplication algorithms
- [ ] **SG-007**: Unit tests for relationship type validation

## Phase 2b: Graph Queries (2 weeks)

- [ ] **SG-008**: Implement single-hop traversal queries
- [ ] **SG-009**: Implement multi-hop path finding (3-hop max)
- [ ] **SG-010**: Implement relationship explanation queries
- [ ] **SG-011**: Entity merge operation (deduplicate after creation)
- [ ] **SG-012**: Query performance optimization (indexing, caching)
- [ ] **SG-013**: Integration tests for graph queries
- [ ] **SG-014**: Performance tests (< 500ms single-hop, < 2s 3-hop)

## Phase 2c: Memory Integration (1 week)

- [ ] **SG-015**: Fact → Entity resolution pipeline
- [ ] **SG-016**: Entity mention indexing for recall
- [ ] **SG-017**: User confirmation workflow for low-confidence relationships
- [ ] **SG-018**: End-to-end test: Fact → Graph → Recall
- [ ] **SG-019**: Documentation and team handoff
