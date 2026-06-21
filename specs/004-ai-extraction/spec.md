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
