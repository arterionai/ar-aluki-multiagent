# Research: Semantic Graph - Entity Deduplication Algorithms

## Entity Matching Strategies

### 1. Exact Match
Compare canonical names directly. Most reliable.

### 2. Substring Match
If one name is a substring of another (e.g., "John Smith" contains "John").

### 3. Phonetic Similarity (Soundex/Metaphone)
For name variations ("John" vs "Jon").

### 4. Levenshtein Distance
String similarity for typos (e.g., "Acme" vs "Acmee").

## Relationship Type Taxonomy

- **worksAt**: Person → Organization
- **owns**: Person/Organization → Asset/Organization
- **mentions**: Entity → Entity (generic reference)
- **collaboratesWith**: Person → Person
- **manages**: Person → Person/Project

## Performance Considerations

- Index aliases for fast lookup during deduplication
- Cache entity IDs in memory for high-frequency references
- Batch entity resolution for > 10 facts at a time

## External References

- PostgreSQL FTS (Full-Text Search) for fuzzy matching
- Azure Cognitive Services (optional) for NER enhancement
