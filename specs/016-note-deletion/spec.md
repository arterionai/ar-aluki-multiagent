# Feature Specification: Note Deletion via WhatsApp

Feature ID: SB-016
Status: Draft
Date: 2026-07-02
Depends on: SB-002 (personal memory), SB-013 (person notes)

## 1. Objective

Let users delete their own saved notes from WhatsApp ("borra lo de Fer",
"olvida lo de la galería") without any admin surface. Today `memory_artifact`
already supports soft delete (`deleted_at_utc`, and recall distinguishes a
`deleted_evidence_gap`), but no user-facing command exercises it.

## 2. Architecture Adaptation

### Dispatch priority chain (updated)

55 `PersonMemoryDomainAgent` (save) → **57 `NoteDeletionDomainAgent` (new)** →
58 `PersonLookupDomainAgent` → 60 `ReminderDomainAgent` → 100 conversational.

### New components (all in `Aluki.Runtime.Memory`)

- **`NoteDeletionDetector`** (`Dispatch/NoteDeletionDetector.cs`) — pure static,
  accent-insensitive with index map back to the original text (shared
  `AccentInsensitiveText.NormalizeWithMap`, extracted from `PersonLookupDetector`).
  - `TryExtractDeletion(string? text, out string topic)`
  - Triggers: "borra lo de ", "borra la nota de ", "elimina lo de ",
    "elimina la nota de ", "olvida lo de ", "olvida a ", "forget about ",
    "delete the note about ".
  - Topic = trailing text, punctuation-trimmed; empty ⇒ no claim.
  - Save intent (SB-013) never claims (defense in depth).

- **`INoteDeletionService`/`NoteDeletionService`** (`Dispatch/NoteDeletionService.cs`)
  — embeds the topic (standalone CTS 15 s), calls the new
  `MemoryStore.SoftDeleteRelevantAsync`, returns the deleted note texts.

- **`MemoryStore.SoftDeleteRelevantAsync(scope, embedding, maxDistance, limit, ct)`**
  — single transaction: sets `deleted_at_utc = now()` on the non-deleted artifacts
  within `maxDistance` (closest first, capped at `limit`), RETURNING their texts;
  writes a `memory.note_deleted` audit per invocation. RLS via the existing
  `ScopedSessionContextSetter`.

- **`NoteDeletionDomainAgent`** (`Dispatch/NoteDeletionDomainAgent.cs`) — priority 57,
  WhatsApp only. Replies:
  - ≥1 deleted: "Olvidado 🗑️" + bullet list of the deleted notes (truncated 150 chars).
  - 0 matches: "No encontré notas sobre *{topic}*."
  - Failure: graceful fallback, `OutcomeCode = "note_deletion_error"`.
  Sends/persists outbound with `CancellationToken.None`.

- **Config**: `MemoryOptions.DeleteMaxDistance` (default **0.5** — stricter than
  recall's 0.6, deleting demands higher confidence than reading), cap 5 notes
  per command.

## 3. Semantics

- **Delete-all-within-threshold**: "borra lo de Fer" deletes every note whose
  embedding is within `DeleteMaxDistance` of "Fer" (max 5, closest first), and the
  reply lists exactly what was deleted so the user can immediately re-save anything
  removed by mistake.
- **Soft delete, recoverable**: rows keep their content; recall already reports
  `deleted_evidence_gap` when deleted evidence would have matched.
- **Corrections are out of scope**: "corrige que…" = save a new note (SB-013) —
  optionally followed by a delete command. No compound transaction.

## 4. Out of Scope

- Confirmation round-trip ("¿seguro?") — would need pending-confirmation state
  (cf. SB-009A); v1 favors the echo-what-was-deleted + soft-delete recoverability.
- Hard delete / retention purge.
- Undo command ("recupera lo de Fer") — natural v2 on top of soft delete.

## 5. Acceptance Criteria

- Detector unit tests ≥10: triggers es/en, accents, topic extraction, empty topic,
  save-intent guard, unrelated text, null/empty.
- Agent contract tests: identity/priority, ClaimsIntent ±, deleted list echoed,
  zero-match reply, failure fallback, outbound persisted.
- Deletion audit (`memory.note_deleted`) written in the same transaction.
- All sends/audits `CancellationToken.None`; embedding standalone CTS.
- No new migration — `deleted_at_utc` exists in `007_personal_memory.sql`.
