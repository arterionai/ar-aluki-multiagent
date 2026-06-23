# Data Model: Mexican Invoice Request (Facturación CFDI)

**Feature**: SB-013 | **Migration**: `db/migrations/024_mexico_invoice_request.sql`

> **Deploy wiring reminder** (CLAUDE.md): `024_mexico_invoice_request.sql` MUST be
> appended to the explicit migration loop in
> `.github/workflows/azure-deploy-runtime.yml` AND to the fixture list in
> `tests/integration/.../DbCaptureFixture.cs`, or it silently never runs.
> Use core `gen_random_uuid()` (pgcrypto is not allow-listed on Azure).

## Scoping model

- **Shared catalog (no RLS)** — PII-free operational knowledge, reusable across tenants
  (same pattern as the SB-010 billing catalog): `invoice_merchants`,
  `invoice_merchant_recipes`.
- **Tenant + user scoped (RLS)** — everything carrying user PII, ticket data, execution
  state, or documents: `fiscal_profiles`, `invoice_requests`,
  `invoice_request_steps`, `invoice_documents`, `invoice_learning_events`,
  `invoice_discovery_attempts`.

RLS uses the established `app.current_tenant` / `app.current_user_id` GUCs +
`app.user_in_tenant()`. WORM tables are SELECT+INSERT only.

---

## Shared catalog tables (no RLS)

### invoice_merchants
- id: UUID PK (`gen_random_uuid()`)
- rfc_emisor: VARCHAR(13) NULL — emisor RFC when known (unique when present)
- display_name: VARCHAR(300) NOT NULL
- aliases: JSONB — alternate vendor names seen on tickets
- portal_hosts: JSONB — known invoicing portal hostnames
- detection_signals: JSONB — OCR/QR signals used to identify this merchant
- default_strategy: VARCHAR(40) NULL — `self_service_portal|qr_deeplink|email_request|aggregator_api|sat_self_invoice|manual_in_person`
- deadline_window_days: INT NULL — default invoicing window if known
- created_at, updated_at: TIMESTAMPTZ
- Indexes: unique (rfc_emisor) WHERE rfc_emisor IS NOT NULL; (display_name)

### invoice_merchant_recipes
- id: UUID PK
- merchant_id: UUID FK → invoice_merchants(id)
- strategy: VARCHAR(40) NOT NULL
- version: INT NOT NULL
- status: VARCHAR(20) NOT NULL — `candidate|active|deprecated`
- recipe_definition: JSONB NOT NULL — steps + field map (see schema below)
- confidence: NUMERIC(3,2) NOT NULL DEFAULT 0.00
- success_count: INT NOT NULL DEFAULT 0
- failure_count: INT NOT NULL DEFAULT 0
- last_outcome_at: TIMESTAMPTZ NULL
- created_by: VARCHAR(40) — `discovery|seed|human`
- created_at, updated_at: TIMESTAMPTZ
- Indexes: (merchant_id, strategy, status, confidence DESC);
  unique (merchant_id, strategy, version)

**`recipe_definition` JSONB shape** (declarative — interpreted, never executed as code):
```json
{
  "strategy": "self_service_portal",
  "entry": { "url": "https://factura.merchant.mx/", "driver": "http_form|headless" },
  "field_map": {
    "rfc": { "selector": "#rfc", "from": "fiscal.rfc" },
    "razon_social": { "selector": "#razonSocial", "from": "fiscal.legal_name" },
    "codigo_postal": { "selector": "#cp", "from": "fiscal.postal_code" },
    "regimen_fiscal": { "selector": "#regimen", "from": "fiscal.regimen_fiscal" },
    "uso_cfdi": { "selector": "#uso", "from": "fiscal.uso_cfdi" },
    "email": { "selector": "#email", "from": "fiscal.email" },
    "folio": { "selector": "#folio", "from": "ticket.folio" },
    "total": { "selector": "#total", "from": "ticket.total" },
    "fecha": { "selector": "#fecha", "from": "ticket.fecha" },
    "sucursal": { "selector": "#serie", "from": "ticket.sucursal" }
  },
  "steps": [
    { "action": "navigate", "to": "entry.url" },
    { "action": "fill", "fields": ["folio","total","fecha","sucursal"] },
    { "action": "fill", "fields": ["rfc","razon_social","codigo_postal","regimen_fiscal","uso_cfdi","email"] },
    { "action": "ask_user_if_present", "gate": "captcha" },
    { "action": "submit", "selector": "#generar" },
    { "action": "download", "capture": ["xml","pdf"] }
  ],
  "required_ticket_fields": ["folio","total","fecha"]
}
```
Allowed `action` vocabulary (bounded, auditable): `navigate`, `read`, `fill`,
`click`, `submit`, `upload`, `download`, `decode_qr`, `ask_user`,
`ask_user_if_present`, `wait_for_email`. No arbitrary code.

---

## Tenant + user scoped tables (RLS)

### fiscal_profiles
- id: UUID PK
- tenant_id: UUID NOT NULL
- user_id: UUID NOT NULL
- label: VARCHAR(100) — e.g., "Personal", "Empresa"
- rfc: VARCHAR(13) NOT NULL
- legal_name_enc: BYTEA NOT NULL — razón social, AES-256-GCM
- postal_code: VARCHAR(5) NOT NULL
- regimen_fiscal: VARCHAR(5) NOT NULL — SAT c_RegimenFiscal
- default_uso_cfdi: VARCHAR(5) NOT NULL — SAT c_UsoCFDI
- email_enc: BYTEA NOT NULL — delivery email, encrypted
- csf_verified: BOOLEAN NOT NULL DEFAULT false — imported+confirmed from CSF
- active: BOOLEAN NOT NULL DEFAULT true
- created_at, updated_at: TIMESTAMPTZ
- Indexes: (tenant_id, user_id, active); unique (tenant_id, user_id, rfc, label)
- RLS: tenant_id = app.current_tenant AND user_id = app.current_user_id

### invoice_requests
- id: UUID PK
- tenant_id: UUID NOT NULL
- user_id: UUID NOT NULL
- merchant_id: UUID NULL FK → invoice_merchants(id) (null until identified)
- fiscal_profile_id: UUID NULL FK → fiscal_profiles(id)
- fiscal_snapshot: JSONB NULL — profile values bound at start (immutable per request)
- source_artifact_id: UUID NULL — originating receipt/expense artifact (SB-004/SB-002)
- ticket_fields: JSONB — extracted ticket identifiers (folio, total, fecha, sucursal, web_id…)
- ticket_fingerprint: VARCHAR(64) NOT NULL — sha256(merchant_rfc|folio|total|fecha)
- strategy_used: VARCHAR(40) NULL
- recipe_id: UUID NULL FK → invoice_merchant_recipes(id)
- recipe_version: INT NULL
- status: VARCHAR(40) NOT NULL — state machine (see spec §7)
- supervised: BOOLEAN NOT NULL DEFAULT false — candidate-recipe runs are supervised
- deadline_utc: TIMESTAMPTZ NULL
- attempt_count: INT NOT NULL DEFAULT 0
- next_retry_utc: TIMESTAMPTZ NULL — sweep harvest (mirrors reminders)
- last_error: TEXT NULL — redacted (never fiscal data)
- correlation_message_id: VARCHAR(120) NULL — inbound WhatsApp correlation
- delivery_route: VARCHAR(120) NULL — `whatsapp:{phoneNumberId}:{waId}` for replies
- created_at, updated_at: TIMESTAMPTZ
- Indexes: (tenant_id, user_id, status); (status, next_retry_utc) — sweep;
  (status, deadline_utc) — deadline sweep;
  unique (tenant_id, user_id, merchant_id, ticket_fingerprint) — idempotency
- RLS: tenant+user scoped

### invoice_request_steps  (WORM: SELECT+INSERT only)
- id: UUID PK
- request_id: UUID FK → invoice_requests(id)
- tenant_id: UUID NOT NULL
- step_no: INT NOT NULL
- action: VARCHAR(40) NOT NULL — from the bounded action vocabulary
- status: VARCHAR(20) NOT NULL — `ok|failed|awaiting_user|skipped`
- detail_redacted: JSONB NULL — payload with fiscal data redacted
- screenshot_ref: VARCHAR(400) NULL — blob ref (headless driver), redacted
- started_at, finished_at: TIMESTAMPTZ
- Indexes: (request_id, step_no)
- RLS: tenant scoped; no UPDATE/DELETE policies (immutable)

### invoice_documents
- id: UUID PK
- request_id: UUID FK → invoice_requests(id)
- tenant_id: UUID NOT NULL
- user_id: UUID NOT NULL
- uuid_fiscal: VARCHAR(36) NULL — CFDI folio fiscal (UUID)
- xml_ref: VARCHAR(400) NULL — blob ref to CFDI XML
- pdf_ref: VARCHAR(400) NULL — blob ref to CFDI PDF
- total: NUMERIC(14,2) NULL
- issued_at: TIMESTAMPTZ NULL
- validation_status: VARCHAR(20) NOT NULL — `valid|quarantined|unvalidated`
- validation_errors: JSONB NULL
- created_at: TIMESTAMPTZ
- Indexes: (tenant_id, user_id, created_at DESC); unique (uuid_fiscal) WHERE uuid_fiscal IS NOT NULL
- RLS: tenant+user scoped

### invoice_learning_events  (WORM: SELECT+INSERT only)
- id: UUID PK
- tenant_id: UUID NOT NULL — provenance of who produced the signal
- merchant_id: UUID NULL FK → invoice_merchants(id)
- recipe_id: UUID NULL FK → invoice_merchant_recipes(id)
- request_id: UUID NULL FK → invoice_requests(id)
- outcome: VARCHAR(30) NOT NULL — `success|failure|partial|manual_review|drift_demotion|promotion`
- confidence_delta: NUMERIC(4,3) NULL
- notes_redacted: TEXT NULL
- created_at: TIMESTAMPTZ
- Indexes: (merchant_id, recipe_id, created_at DESC)
- RLS: tenant scoped on read; recipe/merchant aggregates updated by a SECURITY DEFINER fn

### invoice_discovery_attempts
- id: UUID PK
- tenant_id: UUID NOT NULL
- request_id: UUID FK → invoice_requests(id)
- merchant_id: UUID NULL FK → invoice_merchants(id)
- queries: JSONB — discovery queries issued (web search / RFC lookup / QR target)
- candidate_portals: JSONB — discovered candidate URLs/contacts
- synthesized_recipe_id: UUID NULL FK → invoice_merchant_recipes(id)
- result: VARCHAR(30) NOT NULL — `recipe_synthesized|no_channel|guided_manual`
- created_at: TIMESTAMPTZ
- Indexes: (request_id); (merchant_id)
- RLS: tenant scoped

---

## SAT reference catalogs (validation; bundled reference data)

`c_RegimenFiscal`, `c_UsoCFDI`, `c_CodigoPostal` loaded as reference tables (or
bundled resource) for FR-002 validation. No RLS (static reference). Refreshable.

---

## Cross-tenant claim function (sweep)

`app.claim_due_invoice_requests(limit int)` — SECURITY DEFINER, `SKIP LOCKED`,
returns rows where `status` is non-terminal AND (`next_retry_utc <= now()` OR a
deadline reminder is due), mirroring `app.claim_due_reminders` (SB-005). The interim
`InvoiceRequestSweepFunction` calls this each minute.

## Recipe-stats update function

`app.apply_invoice_learning(recipe_id uuid, outcome text, delta numeric)` — SECURITY
DEFINER; atomically bumps `success_count`/`failure_count`, adjusts `confidence`, and
applies promote/demote thresholds. Keeps shared-catalog mutation off the tenant RLS
path while preserving auditability via `invoice_learning_events`.
