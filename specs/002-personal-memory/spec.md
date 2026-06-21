# Feature Specification: Personal Memory and Grounded Recall (Starter Baseline)

Feature ID: SB-002
Status: Draft
Date: 2026-06-21

## 1. Objective

Turn captured messages into tenant-scoped memory and answer recall queries using only grounded evidence.

## 2. Architecture Adaptation

- MemoryAgent orchestrates RecallSkill, CitationRenderSkill, and MemorySynthesisSkill.
- EmbeddingIndexSkill writes vectors to pgvector-backed storage in PostgreSQL.
- Orleans handles live session context; Durable Functions handle long-running synthesis and rebuild jobs.
- RLS enforces tenant isolation at data plane.

## 3. In Scope

- Intent split between note-to-store and recall query.
- Grounded recall with citations.
- Topic grouping and memory organization.

## 4. Out of Scope

- Autonomous task execution from recalled data.
- External channel adapters beyond already connected channels.

## 5. User Stories

### US1 - Ask and get grounded answer (P1)
Given user asks a memory question, when recall runs, then answer contains only retrieved user data and citations.

### US2 - Topic-based organization (P2)
Given many related notes, when user queries a topic, then response groups relevant items coherently.

### US3 - Cross-channel memory continuity (P3)
Given multiple channels are connected later, when query is asked on any channel, then memory remains unified.

## 6. Acceptance Criteria

- No answer without provenance.
- If no evidence exists, system responds with explicit no-result.
- Deleted memory artifacts are excluded from future recall.
