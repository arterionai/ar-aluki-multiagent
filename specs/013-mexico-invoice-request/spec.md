# Feature Specification: Mexican Invoice Request (Facturación CFDI)

**Feature Branch**: `claude/mexico-invoice-request-spec-yzpaut`

**SB-ID**: SB-013

**Created**: 2026-06-23

**Status**: Draft

**Input**: User description: "Las facturas en México se tienen que solicitar en
diferentes sitios y de muchas maneras — algunas mandan la información por email,
otras escanean un código QR, otras te piden ir a un sitio particular. Diseña un
spec que cubra todos los escenarios posibles y que vaya aprendiendo cómo hacerlo,
probablemente tendrá que ir generando agentes al vuelo que generen diferentes
acciones y necesitará los datos fiscales del usuario."

---

## Executive Summary

In Mexico, obtaining a tax invoice (a CFDI 4.0 *factura*) after a purchase is a
fragmented, per-merchant chore. The buyer (the **receptor**) must hand their fiscal
data to the seller (the **emisor**), but **every merchant exposes a different
process**: a self-service web portal with its own fields, a QR code on the ticket
that deep-links to a pre-filled form, an email address that accepts a photo of the
ticket, a requirement to visit a physical location, or an aggregator. Each portal
asks for different ticket identifiers (folio, web ID, sucursal/serie, total, fecha,
transaction ID), enforces different time windows (commonly "same calendar month"),
and changes without notice.

This feature lets an Aluki user say *"factúrame este ticket"* (or forward a receipt
image) and have the runtime **request the invoice on their behalf** — identifying
the merchant, resolving the right strategy, executing the request, capturing the
resulting CFDI (XML + PDF), validating it, and confirming over WhatsApp. Because the
merchant universe is effectively unbounded, the system **learns each merchant's
procedure as a versioned, reusable "recipe"** and, when it meets an unknown merchant,
**spawns a short-lived execution agent that discovers and drives the flow**, then
persists what it learned so the next request is cheaper and more reliable.

The system holds the user's **fiscal profile** (RFC, razón social, código postal,
régimen fiscal, uso de CFDI, contact email) encrypted at rest and gated by explicit
consent. It always acts as the **user's authorized agent on the user's own
purchases** — never bypassing human-verification gates (captcha/OTP), never evading
anti-bot controls, and never invoicing anything the user did not buy.

---

## 1. Problem Statement

### Current pain

- **Fragmentation**: there is no single "request invoice" action. OXXO, Walmart,
  Costco, gas stations (incl. OXXO Gas), Liverpool, Starbucks, restaurants,
  pharmacies, hotels, and couriers each run their own *autofacturación* portal with
  distinct fields, validation rules, serie/folio conventions, and deadlines.
- **Heterogeneous channels**: portal form, QR deep-link, email-to-merchant, physical
  site, third-party aggregator, or the SAT free generator. The buyer must figure out
  which one applies for each ticket.
- **Repetitive data entry**: the same fiscal data (RFC, razón social *exactly* as on
  the Constancia de Situación Fiscal, código postal, régimen fiscal, uso de CFDI) is
  retyped on every portal, and small mismatches cause rejections.
- **Deadlines**: most merchants only allow invoicing within the same calendar month
  (or a few days). Missing the window means the expense cannot be deducted.
- **Lost receipts / unreadable tickets**: thermal tickets fade; QR codes smudge.

### Desired state

```
Receipt or "factúrame ..." message
  1. Capture + OCR the ticket (reuse SB-004); detect merchant + ticket fields.
  2. Resolve merchant -> invoicing strategy (portal | qr | email | aggregator | sat | manual).
  3. Resolve a recipe (learned playbook). If none/low-confidence -> discovery.
  4. Bind the user's fiscal profile (consented) to the receptor fields.
  5. Execute the strategy (a recipe-driven execution agent), pausing for any
     human-verification gate or missing field via WhatsApp.
  6. Capture CFDI (XML + PDF), validate (UUID/folio fiscal, totals, RFCs), store,
     and link to the originating expense/receipt artifact.
  7. Confirm to the user; record a learning event that updates recipe confidence.
  8. Track the merchant deadline and proactively remind before it lapses (reuse SB-005).
```

---

## 2. Architecture Adaptation

This feature composes existing Aluki building blocks rather than re-inventing them:

- **Capture + OCR**: receipt images are captured (SB-001) and OCR-extracted (SB-004
  `FoundryReceiptOcrProvider`) to recover vendor / total / fecha / RFC emisor / folio.
- **Domain dispatch**: a new `InvoiceRequestDomainAgent` (`IDomainAgent`, SB-009B)
  claims invoice intent and routes to the invoicing skills.
- **Inference (Azure-only)**: merchant identification, portal field-mapping, recipe
  synthesis/refinement, and email composition use Azure AI Foundry model-router
  (`IChatModelRouter`). OCR uses the SB-004 Azure vision provider. No non-Azure
  inference is introduced (constitution VI).
- **Governance + consent**: fiscal-data use and "request on behalf" are consent-gated
  via SB-012 (`ConsentType.RequestInvoiceOnBehalf`, `ConsentType.UseFiscalData`);
  all decisions WORM-audited.
- **Reminders**: invoicing deadlines reuse SB-005 to nudge the user before the
  merchant's window closes.
- **Memory / semantic graph**: merchants and recipes are modeled as shared knowledge;
  captured invoices link back to expense memory artifacts (SB-002 / SB-011).
- **Long-running orchestration**: portal automation is wait-heavy (portal latency,
  human gates, deadline retries). Target home is **Durable Functions** (constitution
  IV); the interim mechanism is a **timer-sweep** (`InvoiceRequestSweepFunction`),
  mirroring the established reminders/delegated-reminders pattern.

### Key concept: Merchant Invoicing Recipe ("playbook")

A **recipe** is a *declarative, versioned, structured procedure* (data, not code)
describing how to obtain an invoice from one merchant via one strategy: the strategy
type, the ordered steps, and a **field map** from the canonical receptor/ticket schema
to that merchant's form fields. Recipes are interpreted at runtime by an **execution
agent**; they are never hand-coded per merchant. This is what makes the long tail of
thousands of merchants tractable and what the system "learns."

### Key concept: on-the-fly execution agents

Per request, the runtime spawns a short-lived **Invoice Execution Agent** that
interprets the recipe against an automation driver (`IPortalAutomationDriver`). When a
recipe is missing or stale, the agent runs in **discovery mode**: it inspects the
portal (or QR target, or merchant contact), uses the model-router to map fields and
propose steps, attempts the flow under a confidence gate, and emits a **candidate
recipe** plus a learning event. This is the "generating agents al vuelo" requirement —
realized as *recipe-driven interpretation* rather than arbitrary code generation, so
every action is bounded, auditable, and replayable.

---

## 3. In Scope

- Secure capture and management of one or more **fiscal profiles** per user (receptor
  data), including optional import from a Constancia de Situación Fiscal (CSF) via OCR.
- Merchant identification from receipt OCR, QR payloads, and RFC emisor.
- Strategy resolution across all real-world channels: **self-service portal**, **QR
  deep-link**, **email-to-merchant**, **aggregator/PAC API**, **SAT free generator**,
  and **manual/in-person guidance**.
- Recipe catalog: store, version, score, promote, deprecate per (merchant, strategy).
- Recipe-driven execution with a tiered automation driver (HTTP-form first, headless
  browser when required) and **human-in-the-loop** handoff for captcha/OTP/missing
  fields over WhatsApp.
- Unknown-merchant **discovery + learning**: candidate recipe synthesis, supervised
  first run, confidence updates from outcomes.
- CFDI capture (XML + PDF), validation, storage, and linkage to the originating
  expense/receipt artifact; retrieval of stored invoices.
- Deadline tracking and proactive reminders before the merchant window closes.

## 4. Out of Scope

- Acting as the **emisor** / issuing CFDIs ourselves or becoming a PAC (we request
  invoices as a buyer; we do not stamp them).
- Solving, bypassing, or evading captchas, anti-bot, or rate-limit controls. Any such
  gate triggers a human handoff (FR-026).
- Bulk / speculative invoicing of purchases the user did not make.
- Non-Mexican tax documents and non-CFDI invoice formats (v1).
- Accounting/ERP posting of the captured invoice (handoff to billing/expense features
  is a follow-up).
- Storing merchant portal *credentials* on the user's behalf (v1 uses public
  self-service flows; credential vaulting is a future consideration).

---

## 5. User Scenarios & Testing *(mandatory)*

### User Story 1 - Capture fiscal profile securely (Priority: P1)

A user provides their receptor fiscal data once so future invoice requests can be
auto-filled. They send the values in chat, or upload a photo/PDF of their Constancia
de Situación Fiscal and the system extracts them for confirmation.

**Why this priority**: No invoice can be requested without valid receptor data, and
re-entry per portal is the single biggest source of friction and rejections. This is
the foundational, independently valuable slice (the user gets a reusable, verified
profile even before any invoice is requested).

**Independent Test**: Send RFC + razón social + código postal + régimen fiscal +
uso de CFDI + email; confirm a profile is persisted (encrypted), marked active, and
retrievable, with consent recorded. Separately, upload a CSF and confirm the extracted
fields are surfaced for user confirmation before persistence.

**Acceptance Scenarios**:

1. **Given** a user with no fiscal profile, **When** they supply all required receptor
   fields and grant consent, **Then** a profile is stored encrypted at rest, marked
   `active`, and a consent record + audit event are written.
2. **Given** a CSF image/PDF, **When** the user uploads it, **Then** the system OCRs it,
   surfaces the extracted RFC/razón social/CP/régimen for confirmation, and persists
   **only** confirmed values (no fabrication of unread fields).
3. **Given** an RFC that fails checksum/format validation, **When** submitted, **Then**
   the system rejects it with a clear message and stores nothing.

---

### User Story 2 - Invoice a known merchant via self-service portal (Priority: P1)

A user forwards a receipt (or says *"factúrame este ticket de OXXO"*) for a merchant
whose portal recipe already exists. The system fills the portal with the ticket fields
+ the user's fiscal profile and returns the CFDI.

**Why this priority**: Self-service portals are the dominant real-world channel; the
"recipe exists" happy path is the highest-volume, highest-value flow and proves the
end-to-end pipeline.

**Independent Test**: With a seeded active recipe and a fiscal profile, submit a
receipt whose OCR yields the required ticket fields; confirm an `invoice_request`
reaches `completed`, a CFDI document (XML + PDF) is stored and validated, and the user
receives a WhatsApp confirmation.

**Acceptance Scenarios**:

1. **Given** an active high-confidence portal recipe and a complete fiscal profile,
   **When** the user submits a legible receipt with all required ticket fields, **Then**
   the request runs end-to-end, a validated CFDI is stored, and the user is confirmed.
2. **Given** the OCR is missing one required ticket field (e.g., web ID), **When** the
   request executes, **Then** the system asks the user for exactly that field over
   WhatsApp and resumes without restarting.
3. **Given** the merchant rejects the receptor data (e.g., RFC/CP mismatch), **When**
   the portal returns the error, **Then** the request enters `failed_recoverable`, the
   specific mismatch is surfaced, and no partial/invalid document is stored.

---

### User Story 3 - QR deep-link strategy (Priority: P2)

A ticket carries a QR code that encodes the merchant's invoicing portal URL with ticket
parameters embedded. The user sends a photo of the ticket/QR.

**Why this priority**: QR-on-ticket is a fast-growing channel that often removes the
need to hand-type ticket identifiers, raising success rates; it reuses the portal
machinery with a pre-fill shortcut.

**Independent Test**: Provide a ticket image whose QR decodes to a known portal
deep-link; confirm the system extracts the QR target, maps embedded parameters to the
recipe field map, and completes the request with fewer user prompts than US2.

**Acceptance Scenarios**:

1. **Given** a ticket whose QR encodes a recognized portal deep-link, **When** decoded,
   **Then** ticket parameters are taken from the QR (not re-typed) and the portal
   recipe completes the request.
2. **Given** a QR that points to an **unknown** host, **When** decoded, **Then** the
   request transitions to discovery (US4) rather than failing.
3. **Given** an unreadable/smudged QR, **When** decoding fails, **Then** the system
   falls back to OCR/text fields and asks for the ticket identifiers if still missing.

---

### User Story 4 - Unknown merchant discovery + on-the-fly recipe learning (Priority: P2)

The user submits a receipt from a merchant Aluki has never invoiced. The system
discovers the merchant's invoicing process, executes it under supervision, and saves a
candidate recipe so subsequent requests are automatic.

**Why this priority**: This is the differentiator and the "learning" mandate — it makes
the long tail of merchants tractable without per-merchant engineering.

**Independent Test**: Submit a receipt for a merchant with no recipe; confirm the
system performs discovery (RFC lookup / web search / QR / portal probe), produces a
**candidate** recipe, completes (or cleanly hands off) the request, and persists the
recipe with `status=candidate` and an initial confidence < auto-run threshold.

**Acceptance Scenarios**:

1. **Given** no recipe for the merchant, **When** a receipt is submitted, **Then** the
   system enters `discovery_required`, gathers candidate portal/contact signals, and
   synthesizes a candidate recipe via the model-router.
2. **Given** a synthesized candidate recipe below the auto-run confidence threshold,
   **When** executed, **Then** execution runs in **supervised** mode (user confirms key
   steps / provides any uncertain field) and the outcome updates recipe confidence.
3. **Given** a candidate recipe that succeeds N times above the promotion threshold,
   **When** the threshold is met, **Then** it is promoted to `active` for that
   (merchant, strategy) and future requests auto-run.
4. **Given** discovery cannot identify any viable channel, **When** exhausted, **Then**
   the request ends in `failed_permanent` with a clear explanation and a manual-guidance
   message (US6), and a `manual_review` learning event is recorded.

---

### User Story 5 - Email-to-merchant strategy (Priority: P2)

Some merchants only accept invoice requests by email (send fiscal data + ticket photo
to `facturacion@merchant`). The system composes and sends the request, then watches for
the returned CFDI.

**Why this priority**: Email is a common channel for restaurants and smaller merchants;
covering it materially widens real-world coverage.

**Independent Test**: With an email-strategy recipe, confirm the system composes a
correctly-formatted request (fiscal data + ticket attachment), sends it, records the
outbound request, and — on receipt of a reply with XML/PDF — captures, validates, and
stores the CFDI.

**Acceptance Scenarios**:

1. **Given** an email-strategy recipe, **When** a receipt is submitted, **Then** the
   system composes the request with the required fiscal fields + ticket attachment and
   records an `email_sent` step.
2. **Given** the merchant replies with a CFDI, **When** the reply is processed, **Then**
   the XML + PDF are captured, validated, stored, and the user is confirmed.
3. **Given** no reply within the configured SLA, **When** the SLA elapses, **Then** the
   request enters `awaiting_merchant`, the user is informed, and a follow-up reminder is
   scheduled before the deadline.

---

### User Story 6 - Human-in-the-loop gates and deadline awareness (Priority: P3)

When a portal presents a captcha/OTP/human-verification gate, or a required field is
genuinely missing, the system pauses and asks the user over WhatsApp. It also tracks
the merchant deadline and proactively reminds the user before it lapses.

**Why this priority**: Necessary for correctness and for the safety posture (no
captcha-bypass), and it prevents silently missing the invoicing window. Lower priority
because it augments rather than constitutes the core flows.

**Independent Test**: Force a captcha gate in a recipe; confirm the request enters
`awaiting_user_input`, the user is prompted with clear instructions, and on user
response the request resumes from the same step. Separately, create a request with a
near deadline and confirm a reminder fires before expiry.

**Acceptance Scenarios**:

1. **Given** a portal captcha/OTP gate, **When** reached, **Then** the request enters
   `awaiting_user_input`, the user is prompted, and the system never attempts to solve
   or bypass the gate.
2. **Given** a request whose merchant deadline is approaching, **When** the lead-time
   window opens, **Then** a reminder is delivered (SB-005) prompting the user to
   complete any pending step.
3. **Given** a deadline passes with the request incomplete, **When** the window closes,
   **Then** the request transitions to `expired` with an explanatory message; no further
   automated attempts are made.

---

### Edge Cases

- **Duplicate request**: same ticket submitted twice → idempotent no-op returning the
  existing request/document (dedupe on `(tenant, user, merchant, ticket fingerprint)`).
- **Already invoiced ticket**: merchant reports the ticket was already invoiced →
  request resolves as `already_invoiced`, surfaces any existing CFDI if retrievable.
- **Multiple fiscal profiles**: user has personal + business RFCs → request asks which
  profile to use (or uses a per-merchant default), recorded on the request.
- **Régimen / uso mismatch**: merchant restricts allowed `uso de CFDI` for the régimen →
  system selects a compatible value or asks; never silently substitutes an invalid code.
- **Total mismatch**: OCR total ≠ portal total → request pauses for confirmation rather
  than guessing the amount.
- **Recipe drift**: portal layout changed and a previously-active recipe fails → recipe
  is auto-demoted from `active` to `candidate`, the failure feeds discovery, and the
  user is told it may take longer this time.
- **Anti-bot / rate-limit response**: portal blocks automation → request enters
  `awaiting_user_input` with a deep-link for the user to finish manually; the merchant
  is back-off rate-limited; no evasion is attempted.
- **CFDI fails validation** (UUID malformed, totals/RFC mismatch) → document stored as
  `quarantined` (withheld from "completed" surface), user warned, manual review flagged.
- **Fiscal data changes** (new CSF) → old profile retained for audit; new profile
  becomes active; in-flight requests keep the profile snapshot they started with.
- **Consent revoked mid-flight** → in-flight automated execution stops at the next safe
  boundary; request ends `cancelled_consent_revoked`; no fiscal data is re-sent.

---

## 6. Requirements *(mandatory)*

### Functional Requirements

**Fiscal profile & consent**

- **FR-001**: System MUST let a user create, view, update, and deactivate one or more
  receptor **fiscal profiles** containing RFC, razón social/nombre, código postal,
  régimen fiscal, default uso de CFDI, and contact email.
- **FR-002**: System MUST validate RFC format/checksum, código postal, régimen fiscal
  (SAT `c_RegimenFiscal`), and uso de CFDI (SAT `c_UsoCFDI`) before persistence;
  invalid values are rejected, not stored.
- **FR-003**: System MUST store fiscal-profile PII **encrypted at rest** and MUST NOT
  emit fiscal data in logs, telemetry, or execution traces (redaction required).
- **FR-004**: System MUST support importing a fiscal profile from a Constancia de
  Situación Fiscal via OCR, surfacing extracted fields for explicit user confirmation
  before persistence and never persisting unread/uncertain fields as facts.
- **FR-005**: System MUST require explicit, auditable user consent
  (`UseFiscalData`, `RequestInvoiceOnBehalf`) before requesting any invoice on the
  user's behalf, and MUST honor consent revocation (fail-closed).

**Capture, identification & strategy**

- **FR-006**: System MUST accept an invoice request from (a) a forwarded receipt image,
  (b) an explicit text intent (e.g., *"factúrame …"*), or (c) a previously captured
  receipt artifact referenced by the user.
- **FR-007**: System MUST OCR the receipt (reuse SB-004) to recover candidate ticket
  fields (vendor, total, fecha, folio/web ID, sucursal/serie, RFC emisor) and decode
  any QR present.
- **FR-008**: System MUST identify the merchant from RFC emisor, QR target host, and/or
  vendor name, resolving to a `invoice_merchants` record (creating one on first sight).
- **FR-009**: System MUST resolve an invoicing **strategy** for the merchant from the
  set {`self_service_portal`, `qr_deeplink`, `email_request`, `aggregator_api`,
  `sat_self_invoice`, `manual_in_person`, `unknown`}.
- **FR-010**: System MUST select the highest-confidence applicable **recipe** for the
  (merchant, strategy); if none exists or all are below the auto-run threshold, it MUST
  enter discovery.

**Recipes, execution & learning**

- **FR-011**: System MUST represent each recipe as versioned declarative data: strategy,
  ordered steps, and a field map from the canonical receptor/ticket schema to the
  merchant's fields.
- **FR-012**: System MUST execute a recipe via a recipe-interpreting **execution agent**
  using a pluggable automation driver, trying an HTTP-form driver before a headless
  browser when both are viable.
- **FR-013**: System MUST, for unknown/low-confidence merchants, perform **discovery**
  (RFC/portal lookup, web search, QR target probe), synthesize a **candidate** recipe
  via the model-router, and run it in **supervised** mode.
- **FR-014**: System MUST record a **learning event** for every execution outcome
  (success/failure/partial/manual) and update recipe `confidence`, `success_count`, and
  `failure_count` accordingly.
- **FR-015**: System MUST promote a recipe to `active` only when its confidence and
  success streak exceed configured thresholds, and MUST auto-demote an `active` recipe
  to `candidate` on repeated failures (drift).
- **FR-016**: System MUST treat recipes as **shared catalog knowledge** (cross-tenant
  reusable), while keeping all execution state, fiscal data, ticket data, and captured
  documents **tenant- and user-scoped** with RLS.
- **FR-017**: System MUST bound execution: per-merchant rate limits, per-request step
  and time budgets, and a maximum automated-retry count before requiring user action.

**Human-in-the-loop & safety**

- **FR-018**: System MUST pause to `awaiting_user_input` and ask the user over WhatsApp
  whenever a required field is missing or ambiguous, resuming from the same step on
  reply without restarting the request.
- **FR-019**: System MUST NOT attempt to solve, bypass, or evade captcha, OTP, or
  anti-bot controls; any such gate MUST trigger a human handoff (with a deep-link for
  the user to complete the step when possible).
- **FR-020**: System MUST act only on purchases the requesting user made; it MUST NOT
  request invoices speculatively or in bulk for arbitrary tickets.

**Document capture, validation & storage**

- **FR-021**: System MUST capture the resulting CFDI as both XML and PDF and persist
  document references (binary in blob storage, metadata in PostgreSQL).
- **FR-022**: System MUST validate the captured CFDI (well-formed UUID/folio fiscal,
  receptor RFC matches the chosen profile, emisor RFC matches the merchant, total
  matches the ticket within tolerance); validation failures `quarantine` the document
  and withhold it from the completed surface.
- **FR-023**: System MUST link the captured invoice to the originating receipt/expense
  artifact and make stored invoices retrievable by the user (list + download).

**Deadlines & lifecycle**

- **FR-024**: System MUST track each merchant's invoicing **deadline** (per-recipe or
  default window) and schedule a proactive reminder (SB-005) before it lapses.
- **FR-025**: System MUST drive each request through an explicit state machine
  (see §7) and persist every transition to a WORM audit/execution log.
- **FR-026**: System MUST be idempotent per ticket: a duplicate submission for the same
  `(tenant, user, merchant, ticket fingerprint)` returns the existing request/document.
- **FR-027**: System MUST expire requests whose merchant deadline passes
  (`expired`) and stop automated attempts, informing the user.

### Key Entities

- **Fiscal Profile**: a user's receptor identity (RFC, razón social, CP, régimen, uso
  de CFDI, email). Sensitive; encrypted; consent-gated; tenant+user scoped; versioned.
- **Merchant**: an emisor directory entry (RFC emisor, display name, portal host(s),
  aliases, detection signals). Shared catalog.
- **Recipe (Playbook)**: a versioned, declarative procedure for one (merchant, strategy)
  — steps + field map + confidence + lifecycle status. Shared catalog.
- **Invoice Request**: a single user's attempt to invoice one ticket — references a
  merchant, a fiscal profile snapshot, ticket fields, the recipe/version used, status,
  deadline, attempts. Tenant+user scoped.
- **Execution Step / Log**: an append-only record of each action taken for a request
  (action, status, redacted payload, optional screenshot ref, timing). WORM.
- **Invoice Document (CFDI)**: the captured result — UUID/folio fiscal, XML ref, PDF
  ref, total, validation status, issued-at. Tenant+user scoped.
- **Learning Event**: an outcome record that updates recipe confidence (success,
  failure, partial, manual_review, drift_demotion).

---

## 7. Lifecycle (state machine)

```
captured
  └─> merchant_identified
        ├─> awaiting_fiscal_data        (no/ambiguous profile or missing consent)
        └─> strategy_resolved
              ├─> discovery_required    (no/low-confidence recipe)  ──┐
              └─> executing  <───────────────────────────────────────┘
                    ├─> awaiting_user_input   (captcha/OTP/missing field/anti-bot)
                    ├─> awaiting_merchant      (email/aggregator async reply)
                    ├─> document_captured ─> validated ─> completed
                    ├─> already_invoiced
                    ├─> failed_recoverable     (retry within budget)
                    ├─> failed_permanent       (no viable channel / budget exhausted)
                    └─> quarantined            (CFDI failed validation)
  (any non-terminal) ─> expired                 (merchant deadline passed)
  (any non-terminal) ─> cancelled_consent_revoked
```

Terminal states: `completed`, `failed_permanent`, `expired`, `already_invoiced`,
`cancelled_consent_revoked`. `quarantined` is terminal-with-review.

---

## 8. Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can register a complete, validated fiscal profile (or import one
  from a CSF) in under 2 minutes and never re-enter that data per merchant.
- **SC-002**: For merchants with an `active` recipe, ≥ 90% of legible-receipt requests
  reach `completed` with a validated CFDI without any user step beyond the initial
  submission.
- **SC-003**: For a previously-unknown merchant, the system either completes the request
  or produces a reusable candidate recipe in the **first** encounter ≥ 70% of the time
  (supervised allowed), and the **second** encounter for that merchant requires fewer
  user prompts than the first.
- **SC-004**: Zero fiscal-data leakage: 0 occurrences of RFC/razón social/CP in logs,
  telemetry, or execution traces across the test corpus.
- **SC-005**: Zero captcha/anti-bot bypass attempts: 100% of such gates result in a
  human handoff.
- **SC-006**: ≥ 95% of captured CFDIs pass validation (UUID/RFC/total) before being
  surfaced as `completed`; any that fail are quarantined, never surfaced as success.
- **SC-007**: For requests with a known deadline, ≥ 95% receive a proactive reminder
  before the merchant window closes; `expired` requests are never silently dropped.
- **SC-008**: Idempotency holds: duplicate submissions of the same ticket never produce
  a second request or a duplicate CFDI.

---

## 9. Constitution Check

- **I. Skill-First Execution**: invoice request, fiscal-profile management, recipe
  resolution, execution, and capture are explicit skills with input/output schemas,
  declared side effects, idempotency keys, telemetry, and authorization scope. Agents
  route; skills own side effects.
- **II. Tenant-Scoped Security**: fiscal profiles, requests, execution logs, and
  documents are RLS-scoped to tenant+user; recipes/merchants are shared catalog (no
  user PII). Principal context required before any side effect.
- **III. Grounded Memory / No Fabrication**: OCR/CSF fields below confidence are not
  asserted; missing fields trigger clarification; captured CFDIs must validate before
  being surfaced — the system never invents an invoice or a fiscal value.
- **IV. Durable Session vs Workflow**: long, wait-heavy portal/email flows target
  Durable Functions; interim timer-sweep matches the established reminders pattern;
  conversational turns stay non-blocking.
- **V. Cost-Aware & Observable**: HTTP-form driver tried before headless browser;
  recipes cache learned procedures to avoid repeated discovery cost; every execution
  emits latency/cost/outcome telemetry.
- **VI. Azure Baseline**: all inference via Azure OpenAI / Azure AI Foundry; deployed in
  the Functions isolated worker; secrets via Key Vault; documents in Azure blob storage.

**Gate Result**: PASS (no violations requiring complexity justification).

---

## 10. Assumptions

- Users are legitimate buyers requesting invoices for their own purchases; the runtime
  acts strictly as their authorized agent.
- The deployed environment can run an automation driver (HTTP-form always; headless
  browser via a separate Azure Container Apps job for JS-heavy portals — see plan.md).
- A web/RFC discovery capability is available behind `IMerchantDiscoveryProvider`
  (stubbed initially); discovery degrades gracefully to user guidance when unavailable.
- SAT catalog data (`c_RegimenFiscal`, `c_UsoCFDI`, `c_CodigoPostal`) is available for
  validation (bundled reference set, refreshable).
- Recipes are shareable across tenants as non-sensitive operational knowledge; if a
  given deployment requires tenant-private recipes, that is a configuration toggle
  (NEEDS CLARIFICATION if a customer demands strict recipe isolation).
- v1 uses public self-service flows only; storing user portal **credentials** for
  account-gated portals is out of scope (future consideration).

## 11. Dependencies

- **Upstream**: SB-001 (capture), SB-004 (receipt/CSF OCR), SB-009B (domain dispatch),
  SB-012 (consent + audit), SB-005 (deadline reminders), SB-002/SB-011 (linkage).
- **External**: merchant self-service portals / email endpoints (uncontrolled, may
  change); optional aggregator/PAC APIs; a discovery/search provider; Azure blob
  storage for documents.
- **Downstream (follow-up)**: expense/accounting posting of captured invoices;
  credential-vault for account-gated portals; Durable Functions migration from the
  interim sweep.

---

## Related Documents

- `specs/013-mexico-invoice-request/research.md` — domain investigation (CFDI 4.0,
  channel taxonomy, "best way" recommendation + alternatives, safety posture).
- `specs/013-mexico-invoice-request/data-model.md` — tables, RLS, migration `024`.
- `specs/013-mexico-invoice-request/plan.md` — implementation plan & technical context.
- `specs/013-mexico-invoice-request/quickstart.md` — end-to-end validation scenarios.
- `specs/000-common/skills-registry.md`, `.specify/memory/constitution.md`.
