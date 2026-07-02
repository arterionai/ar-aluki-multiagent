-- 027: ANN index for personal-memory recall.
-- Recall's vector search (MemoryStore.SearchAsync) was an exact KNN scan over the
-- user's context; fine at small volumes, linear as notes grow. HNSW keeps it fast
-- at scale. Requires pgvector >= 0.5.0 (verify on the server:
--   select extversion from pg_extension where extname = 'vector';).
-- Queries filter by context_id/deleted_at_utc before ordering, so keep ef_search
-- comfortably above the recall top-k (default 40 >> RecallTopK) — no config change
-- needed today.

create index if not exists ix_memory_artifact_embedding_hnsw
    on memory_artifact using hnsw (embedding vector_cosine_ops);
