# Research: AI Extraction Feature

**Date**: 2026-06-21 | **Feature**: SB-004 AI Extraction  
**Source**: Spec clarifications (Session 2026-06-21)

## Resolved Clarifications

### C1: Language Detection and Handling

**Decision**: Auto-detection with region fallback

**Details**:
- System attempts language detection on audio/text input
- If inconclusive, defaults to user's configured region preference (es-MX primary, en-US secondary)
- Language is recorded in extraction metadata for recall filtering
- Supports bilingual/code-switching transcripts with language-pair notation

**Rationale**: Matches Aluki's regional focus (Mexico-first) while supporting bilingual user patterns common in North America.

**Alternatives Considered**:
- Static language enforcement per user: too rigid for mixed conversations
- Manual language selection per input: poor UX
- Declined: Requiring external language service calls outside primary provider

---

### C2: Extraction Confidence Thresholds

**Decision**: Confidence scoring per extraction field with three tiers

**Confidence Scale**:
- **High** ≥0.85 → accepted as-is, surfaced directly to user
- **Medium** 0.70–0.84 → flagged for review, included in response with uncertainty marker
- **Low** <0.70 → marked uncertain, not surfaced without user review, stored for async review

**Applied To**: transcription, entity extraction, decision items, amounts, dates, RFC (tax ID)

**Rationale**: Aligns with constitution principle III (grounded memory, no fabrication) while providing actionable partial results to users.

**Alternatives Considered**:
- Binary threshold: loses granularity for user action and recall
- Per-field thresholds: added complexity without clear benefit over unified scale
- Declined: Suppressing Medium confidence entirely (loses value)

---

### C3: Mixed-Language and Code-Switching

**Decision**: Segment-level language tagging with independent processing

**Flow**:
1. During transcription, segments are language-tagged
2. Multilingual extractions process each language segment independently
3. Results merged with language-pair notation (e.g., `es-MX:en-US` for bilingual passages)
4. Language switches recorded in extraction metadata

**Rationale**: Preserves extraction fidelity across language boundaries and supports accurate multi-language recall.

**Alternatives Considered**:
- Single-language extraction per input: loses data
- Concurrent extraction per language with all-English fallback: too complex
- Declined: Unsupported for initial MVP

---

### C4: OCR Fallback Behavior

**Decision**: Staged OCR failure handling

**Fallback Sequence**:
1. Primary structured OCR attempt (vendor/amount/date/tax/RFC extraction)
2. If structured field extraction fails → secondary text-only OCR
3. If both fail → fragment marked as "unreadable" with manual-review flag
4. Raw image reference stored for later human verification
5. **No invented data**: only persisted facts

**Rationale**: Prevents data loss while maintaining accuracy constraints from constitution principle III.

**Alternatives Considered**:
- Soft failure (return empty fields): data loss
- Hybrid: return structured + text: acceptable, same as staged approach
- Declined: Placeholder/default values for unread fields

---

### C5: Async Job Lifecycle and Status Tracking

**Decision**: Job status endpoint with explicit state machine

**States**:
- `pending` → queued, awaiting processing
- `processing` → active extraction underway
- `completed_success` → all fields extracted
- `completed_with_warnings` → partial success; Medium/Low confidence fields flagged
- `failed` → unrecoverable error

**Metadata**:
- Segment count (for long audio)
- Completion percentage
- Job ID persists across retries (idempotency)

**Rationale**: Transparent job tracking supports async extraction SLA and user experience expectations.

**Alternatives Considered**:
- Polling webhook: more complex, external dependency
- Blocked synchronous: doesn't meet latency targets for long audio (>5min)
- Declined: Eventual consistency without status endpoint (poor UX)

---

## Technology Stack Research

### Audio Transcription

**Provider**: Azure Cognitive Services (Speech-to-Text)
- Supported: Spanish (es-MX), English (en-US)
- Language detection available
- Confidence scores per phrase
- Batch transcription for long audio

**Alternative**: OpenAI Whisper
- Pros: Cost-effective, open-source support
- Cons: No out-of-box confidence scores, requires post-processing

**Decision**: Azure Speech (Cognitive Services) with fallback to Whisper for cost optimization in Phase 2

---

### Text Extraction and Summarization

**Primary Path**: Azure OpenAI (GPT-4 or GPT-4 Turbo)
- Structured output via function calling
- Bilingual support
- Confidence via token probabilities (engineering)

**Backup/Phase 2**: Anthropic Claude 3.5 Sonnet
- Better at nuanced decision extraction
- Explicit reasoning tokens

**Decision**: Azure OpenAI for MVP, explore Claude for Phase 2 quality improvements

---

### Receipt OCR

**Provider**: Azure Computer Vision (Form Recognizer v4)
- Structured receipt model
- Field-level confidence scores
- Tax ID (RFC) extraction for Mexico

**Alternative**: Google Cloud Vision + custom regex for RFC
- More mature: Google Vision + regex for RFC extraction

**Decision**: Azure Computer Vision (Form Recognizer) for unified Azure stack; migrate to Google Vision if cost/accuracy requires it

---

### Data Persistence

**Primary Store**: PostgreSQL (existing RLS foundation)
- Extraction artifacts table with tenant/context partitioning
- Metadata (language, confidence, job status) as JSONB
- Vector embeddings for recall (pgvector)

**Sequence Store**: DynamoDB (cost-effective for time-series job status)
- Consider if job status polling becomes bottleneck

**Decision**: PostgreSQL for MVP; defer DynamoDB until scale testing

---

## Security and Compliance Considerations

### Sensitive Data Handling

**Requirements**:
- Receipt images should not be persisted in plaintext
- Extracted amounts/dates/RFCs are audit-sensitive
- Transcripts may contain personal/financial information

**Approach**:
- Images: store reference pointers only, original in blob storage with lifecycle
- Extracted fields: encrypted at rest in PostgreSQL
- Audit trail: all extractions logged with user/timestamp/confidence

**Decision**: Implement in Phase B (security gate)

---

## Performance Targets (from Constitution)

- **Synchronous extraction** (short text, small images): P95 ≤ 2 seconds
- **Async extraction** (long audio): return job status within 5 seconds, complete within 60 seconds for standard audio (< 5 minutes)
- **Batch mode** (bulk receipt processing): supports 100+ items with chunking

---

## Known Risks

| Risk | Mitigation |
|------|-----------|
| Audio transcription latency for long files (>10 min) | Use durable orchestrations + batch API; stream processing for preview mode |
| Language detection errors on code-switched text | Fall back to user region; flag uncertain passages |
| OCR on low-quality receipt images | Implement confidence threshold; surface unreadable to user for manual entry |
| Cost explosion from API calls | Implement caching, confidence thresholds, batch pricing; monitor spend per user |
| Schema drift in extraction structures | Version extraction schema in migrations; add migrations for schema evolution |

---

## Phase 0 Completion

All clarifications resolved. No blocking unknowns remain for Phase 1 design.
