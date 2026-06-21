# Feature Specification: AI Extraction (Starter Baseline)

Feature ID: SB-004
Status: Draft
Date: 2026-06-21

## 1. Objective

Provide structured extraction from audio, text, and receipt images to power actionability and downstream recall.

## 2. Architecture Adaptation

- KnowledgeAgent orchestrates ExtractionSkill and MemorySynthesisSkill.
- MediaTranscriptionSkill and receipt OCR flow execute via Skill Registry contracts.
- Durable orchestrations handle long audio and heavy extraction jobs with retries.
- Extracted structures are stored in PostgreSQL and indexed for recall.

## 3. In Scope

- Audio transcription (es-MX/en-US).
- Text summary + action items + decisions + amount/date/entity extraction.
- Receipt OCR including RFC when available.

## 4. Out of Scope

- Generic non-receipt image OCR.
- Calendar/reminder execution from extraction output.

## 5. User Stories

### US1 - Voice note to structured result (P0)
Given audio note, when extraction runs, then system returns transcription and structured action items.

### US2 - Text to summary and actions (P1)
Given long text or forwarded conversation, when processed, then summary and decisions are returned clearly.

### US3 - Receipt OCR with fiscal fields (P0)
Given receipt image, when OCR runs, then vendor/amount/date/tax and RFC are extracted when present.

## 6. Acceptance Criteria

- Audio under target duration returns structured response within defined SLA.
- Uncertain or unreadable fragments are flagged, never invented.
- Structured extraction output persists with provenance for future recall.

## 7. Clarifications

### Session 2026-06-21

- Q: How is the language of incoming transcripts detected and handled? → A: Auto-detection with region fallback: system attempts language detection on audio/text; if inconclusive, defaults to user's configured region preference (es-MX primary, en-US secondary). Language is recorded in extraction metadata for recall filtering.

- Q: What extraction confidence thresholds determine flagging or filtering? → A: Confidence scoring applied per extraction field (transcription, entity, decision, amount, date, RFC): **High** ≥0.85 (accepted as-is), **Medium** 0.70–0.84 (flagged for review), **Low** <0.70 (marked uncertain, not surfaced without user review). Confidence metadata persists with each extraction.

- Q: How are mixed-language inputs (code-switching, bilingual transcripts) handled? → A: Segments are language-tagged during transcription; multilingual extractions process each language segment independently, then merge results with language-pair notation (e.g., "es-MX:en-US" for bilingual passages). System notes language switches in metadata.

- Q: What is the OCR fallback behavior when primary receipt OCR fails? → A: Primary OCR attempt → if structured field extraction fails, secondary text-only OCR attempted → if both fail, fragment marked as "unreadable" with manual-review flag and raw image reference stored for later human verification. No invented data.

- Q: How do users track async job lifecycle and status? → A: Job status endpoint returns state: **pending** (queued), **processing** (active), **completed_success** (all fields extracted), **completed_with_warnings** (partial success, Medium/Low confidence fields flagged), **failed** (unrecoverable error). Progress metadata includes segment count and completion %. Job ID persists across retries for idempotency.
