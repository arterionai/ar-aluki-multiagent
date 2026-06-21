# Data Model: Semantic Graph

## Tables

### semantic_entities
- id: UUID PK
- tenant_id: UUID
- entity_type: VARCHAR(50)
- canonical_name: VARCHAR(500)
- description: TEXT
- active: BOOLEAN
- created_at, updated_at: TIMESTAMP

### semantic_entity_aliases
- id: UUID PK
- entity_id: UUID FK
- alias: VARCHAR(500)
- confidence: NUMERIC(3,2)

### semantic_relationships
- id: UUID PK
- tenant_id: UUID
- source_entity_id, target_entity_id: UUID FK
- relationship_type: VARCHAR(100)
- confidence: NUMERIC(3,2)
- explanation: TEXT
- active: BOOLEAN
- created_at, archived_at: TIMESTAMP

### semantic_entity_facts
- id: UUID PK
- entity_id: UUID FK
- fact_id: UUID
- tenant_id: UUID
- created_at: TIMESTAMP

## Indexes
- (tenant_id, entity_type, active)
- (tenant_id, canonical_name)
- (entity_id, alias)
- (tenant_id, source_entity_id, target_entity_id)
- (tenant_id, target_entity_id, source_entity_id)
