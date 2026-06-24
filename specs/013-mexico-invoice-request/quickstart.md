# Quickstart: Validation Scenarios for Mexican Invoice Request

End-to-end validation scenarios verifying SB-013. Each scenario lists prerequisites,
steps, and expected outcomes. All scenarios must pass before release.

## Prerequisites

- .NET 10 runtime; PostgreSQL with migration `024_mexico_invoice_request.sql` applied
  (plus SB-004 OCR, SB-012 consent, SB-005 reminders migrations).
- `Invoicing:FiscalDataEncryptionKey` set (base64 32-byte) or `Calendar:TokenEncryptionKey` fallback.
- A test fiscal profile and a seeded `active` recipe for the portal scenarios; a stub
  `IPortalAutomationDriver` and `IMerchantDiscoveryProvider` for deterministic tests.
- Principal context resolver (tenant_id, user_id, context_id).

---

## Scenario 1: Register and validate a fiscal profile (US1)

**Objective**: A user stores a valid receptor profile once; PII is encrypted; invalid
RFC is rejected.

**Steps**:
1. `POST api/invoicing/fiscal-profiles` with RFC, razón social, CP, régimen (601), uso
   (G03), email; grant `UseFiscalData` + `RequestInvoiceOnBehalf` consent.
2. Inspect the row.

**Expected**:
- ✓ Profile persisted with `active=true`; `legal_name_enc`/`email_enc` are ciphertext
  (not plaintext) in the DB.
- ✓ Consent records + audit events written (SB-012).
- ✓ A second POST with a malformed RFC (bad checksum) returns 400 and stores nothing.
- ✓ CSF import (`import-csf`) returns extracted fields for confirmation and persists
  only confirmed values.

---

## Scenario 2: Invoice a known merchant via portal — happy path (US2)

**Objective**: With an active recipe + complete profile + legible receipt, the request
completes with a validated CFDI and no extra user steps.

**Steps**:
1. Send a receipt image whose OCR yields all `required_ticket_fields`.
2. The dispatch agent (`InvoiceRequestDomainAgent`, priority 40) claims the intent and
   creates an `invoice_request`.
3. The execution agent runs the recipe via `HttpFormAutomationDriver` (stubbed to
   return a CFDI XML+PDF).

**Expected**:
- ✓ `invoice_request` transitions `captured → merchant_identified → strategy_resolved →
  executing → document_captured → validated → completed`.
- ✓ `invoice_documents` row: `validation_status='valid'`, `uuid_fiscal` set, `total`
  matches the ticket, XML+PDF blob refs present.
- ✓ Document linked to the source receipt artifact; user receives a WhatsApp confirmation.
- ✓ No fiscal data appears in `invoice_request_steps.detail_redacted` or logs (SC-004).

---

## Scenario 3: Missing ticket field → human-in-the-loop resume (US2/US6)

**Objective**: A required field absent from OCR triggers a targeted prompt; the request
resumes from the same step.

**Steps**:
1. Submit a receipt missing `web_id`.
2. Confirm `status=awaiting_user_input` and the user is asked for exactly `web_id`.
3. `POST api/invoicing/requests/{id}/input` with the value.

**Expected**:
- ✓ Request resumes at the same step (no restart); completes to `completed`.
- ✓ The prompt names only the missing field; the system never guesses the total/RFC.

---

## Scenario 4: QR deep-link (US3)

**Objective**: A ticket QR encoding a known portal deep-link pre-fills ticket params.

**Steps**:
1. Submit a ticket image whose QR decodes to a recognized portal URL with params.

**Expected**:
- ✓ Ticket params are read from the QR (not re-typed); fewer prompts than Scenario 2.
- ✓ A QR pointing to an unknown host transitions to `discovery_required` (Scenario 5),
  not failure.
- ✓ An unreadable QR falls back to OCR of the printed code and asks if still missing.

---

## Scenario 5: Unknown merchant → discovery + candidate recipe (US4)

**Objective**: A never-seen merchant is discovered, run supervised, and a candidate
recipe is learned.

**Steps**:
1. Submit a receipt for a merchant with no recipe.
2. The system enters `discovery_required`, gathers candidate portals/contacts via the
   (stubbed) discovery provider, and synthesizes a candidate recipe via the model-router.
3. Execute in supervised mode; record outcome.

**Expected**:
- ✓ `invoice_discovery_attempts` row with `result='recipe_synthesized'`.
- ✓ A new `invoice_merchant_recipes` row: `status='candidate'`, `confidence < 0.80`.
- ✓ A success `invoice_learning_event` raises confidence; after
  `PromoteAfterSuccesses` successes the recipe becomes `active` (a `promotion` event).
- ✓ When discovery finds no channel: `failed_permanent` + `manual_review` event + a
  guidance message (US6); nothing fabricated.

---

## Scenario 6: Email-to-merchant strategy (US5)

**Objective**: Compose+send an email request and capture the returned CFDI.

**Steps**:
1. With an `email_request` recipe, submit a receipt.
2. Simulate a merchant reply containing XML+PDF.

**Expected**:
- ✓ An `email_sent` step is recorded; `status=awaiting_merchant`.
- ✓ On reply, XML+PDF are captured, validated, stored; `status=completed`; user confirmed.
- ✓ No reply within SLA → user informed + follow-up reminder scheduled before deadline.

---

## Scenario 7: Captcha / anti-bot gate — never bypassed (US6, safety)

**Objective**: A human-verification gate triggers a handoff, never a bypass.

**Steps**:
1. Run a recipe whose step hits a captcha gate.

**Expected**:
- ✓ `status=awaiting_user_input`; user prompted (with a deep-link to finish manually
  where possible); the system makes **zero** attempts to solve/bypass the gate (SC-005).
- ✓ An anti-bot block response back-off rate-limits the merchant; no evasion.

---

## Scenario 8: Deadline awareness + expiry (US6)

**Objective**: Proactive reminder before the merchant window; clean expiry after.

**Steps**:
1. Create a request with `deadline_utc` near now; advance the sweep.

**Expected**:
- ✓ A reminder (SB-005) fires within `ReminderLeadDays` before the deadline (SC-007).
- ✓ When the deadline passes with the request incomplete, `status=expired`; no further
  automated attempts; user informed (never silently dropped).

---

## Scenario 9: Idempotency + validation quarantine

**Objective**: Duplicate submissions are no-ops; invalid CFDIs are quarantined.

**Steps**:
1. Submit the same ticket twice.
2. Submit a ticket whose captured CFDI has a mismatched receptor RFC.

**Expected**:
- ✓ The second submission returns the existing request/document (no duplicate row),
  enforced by `unique (tenant_id, user_id, merchant_id, ticket_fingerprint)` (SC-008).
- ✓ The mismatched CFDI → `invoice_documents.validation_status='quarantined'`, withheld
  from the completed surface, user warned, manual-review flagged (SC-006).

---

## Summary of Validation Coverage

| Scenario | Feature | Gate |
|---|---|---|
| 1 | Fiscal profile + encryption + validation | Foundation (US1) |
| 2 | Known-recipe portal happy path | Core pipeline (US2) |
| 3 | Missing field human-in-loop resume | Robustness (US2/US6) |
| 4 | QR deep-link | Channel coverage (US3) |
| 5 | Discovery + learning + promote/demote | Differentiator (US4) |
| 6 | Email strategy + async reply | Channel coverage (US5) |
| 7 | Captcha never bypassed | Safety (US6) |
| 8 | Deadline reminder + expiry | Lifecycle (US6) |
| 9 | Idempotency + CFDI quarantine | Correctness |

**Gate**: Quickstart scenarios complete. Ready for task generation and implementation.
