# Feature Specification: Contact Memory — Person Notes

Feature ID: SB-013
Status: Draft
Date: 2026-07-01

## 1. Objective

Let users save contextual notes about people ("Recuérdame que Fer Amaro la conocí en
galería") and later recall them by name ("¿Quién es Fer?"). Today "recuérdame que…"
is intercepted by the reminder agent (priority 60), which incorrectly asks for a
delivery time instead of saving a fact about the person.

## 2. Architecture Adaptation

- `PersonNoteDetector` — deterministic, accent-insensitive, no I/O. Distinguishes
  person-note intent from reminder intent by requiring no future temporal expression
  when "recuérdame que" is the trigger. Unconditional triggers ("guarda que",
  "anota que", etc.) never need the temporal check.
- `PersonMemoryDomainAgent` — `IDomainAgent` at priority 55, evaluated before
  `ReminderDomainAgent` (60). Saves via `IMemoryIngestionSink`, then replies with a
  short confirmation. No LLM involved — the save is deterministic.
- Recall ("¿Quién es Fer?") is fully served by the existing
  `ConversationalResponseAgent` + `MemoryRecallService` pipeline (vector search +
  LLM synthesis). No new recall code is required.
- Entity deduplication via `IEntityResolutionService` (SB-011) would be a
  fire-and-forget enhancement; not on the reply path and not included in this spec.

## 3. In Scope

- US1: Save person note from WhatsApp without a time expression.
- US2: Recall person by name via the existing conversational recall path.
- US3: "recuérdame que…" without a time expression never triggers the reminder flow.

## 4. Out of Scope

- Dedicated contacts list or profile management UI.
- Profile enrichment (age, health concern, product recommendations) — SheloNabel concern.
- Cross-contact relationship queries ("¿Quién conoce a Fer?") — SB-011 concern.
- Editing or deleting a saved person note via WhatsApp command.

## 5. User Stories

### US1 — Save person note (P1)
Given user sends "Recuérdame que Fer amaro la conocí en galería" (no time expression),
when `PersonMemoryDomainAgent` handles it, then the note is persisted as a memory
artifact and the user receives "¡Anotado! 📒 Fer amaro la conocí en galería."

### US2 — Recall person by name (P1)
Given a stored person note, when user asks "¿Quién es Fer?", then the conversational
agent returns a grounded answer (e.g., "La conociste en la galería afuera de la casa
de Bluey.") with a citation to the stored artifact.

### US3 — No spurious reminder prompt (P1)
Given "Recuérdame que Fer amaro la conocí en galería" without a time expression,
the system must NOT reply "¿A qué hora te lo recuerdo?" — the note is saved and
confirmed immediately without asking for a delivery time.

## 6. Acceptance Criteria

- `PersonNoteDetector` unit tests: ≥8 cases covering triggers, temporal blockers,
  null/empty, accent variants, mixed case.
- After US1, asking "¿Quién es Fer?" returns a non-empty grounded answer.
- Reply is sent with `CancellationToken.None` (CancellationToken discipline rule).
- `PersonMemoryDomainAgent` registered as `IDomainAgent` at priority 55.
- No new migration or database table required — reuses `memory_artifact` (migration
  `007_personal_memory.sql`).

## 7. Clarifications

### Disambiguation: "recuérdame que" vs. time-based reminders

"recuérdame" without "que" always falls through to the reminder agent
(e.g., "recuérdame llamar a Juan mañana").

"recuérdame que" is claimed by `PersonMemoryDomainAgent` UNLESS a future temporal
marker is found in the text (mañana, a las, el lunes, tomorrow, etc.). If blocked by
a temporal marker, the message falls through to `ReminderDomainAgent` at priority 60
as before.

### Corroboration for recall

If the user has saved only one note about a person, `MemoryRecallService` returns
`LowConfidence` (1 artifact). The existing low-confidence path handles this with a
clarifying message ("Solo encontré una nota relacionada..."). No special handling
needed in `PersonMemoryDomainAgent`.

### No migration needed

The feature reuses the `memory_artifact` table (migration `007_personal_memory.sql`)
and optionally the semantic graph tables (`019_semantic_graph.sql`). Person notes are
indistinguishable from any other memory artifact at the storage layer — the
distinction is purely in how they were captured (via `PersonMemoryDomainAgent`).
