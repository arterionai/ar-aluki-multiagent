# Capture Operations Runbook

Operational guide for the WhatsApp capture foundation (spec 001). Covers
mandatory audit events, correlation usage, retry/terminal handling, and the
telemetry that backs the capture SLOs.

## 0. Where the pipeline runs

The capture pipeline lives in the shared library `src/Aluki.Runtime.Capture`
(skills, persistence, security, retry, telemetry — no web dependency). Two hosts
consume it via `AddWhatsAppCapture(configuration)`:

- **`Aluki.Runtime.Functions`** (Azure Functions, isolated worker) — the
  **deployed** ingress. The `azure-deploy-runtime.yml` workflow publishes this
  project to the Function App (`func-araluki-dev-6155`) on push to `main`.
- **`Aluki.Runtime.Host`** (ASP.NET Core) — local/dev ingress and the host for
  integration/contract tests.

Both expose the same route and dispatch the same `WhatsAppCaptureCoordinator`.

## 1. Ingress

- Endpoint: `POST /api/channels/whatsapp/inbound` (Function auth level: Function key)
- Health: `GET /api/health` (Functions) / `GET /health` (Host)
- Request/response contract: `specs/001-whatsapp-capture/contracts/whatsapp-inbound-contract.yaml`
- Outcomes:
  - `202` with `CaptureAck` — `accepted`, `duplicate_suppressed`, `accepted_unsupported`
  - `400` with `CaptureError` — `invalid_payload`
  - `403` with `CaptureError` — `scope_denied`
  - `500` with `CaptureError` — `retry_exhausted` (terminal, controlled failure)

## 2. Correlation

Every request carries a `correlation_id`. If the inbound envelope omits it, the
runtime generates one and returns it on the ack/error. The same id is stamped on:

- telemetry spans/metrics (`correlation_id` tag),
- every `capture_audit_event` row,
- structured logs.

Use the `correlation_id` as the primary key when tracing a single capture across
logs, audit, and metrics.

## 3. Mandatory audit events

All persisted in `capture_audit_event` (see migration 004). Event name is
constrained at the database level.

| Event | When | Status | Scope written |
|-------|------|--------|---------------|
| `capture.accepted` | canonical message persisted | success | tenant/context/user |
| `capture.duplicate_suppressed` | duplicate redelivery suppressed | suppressed | tenant/context/user |
| `capture.unsupported_payload` | unsupported content stored as minimal artifact | success | tenant/context/user |
| `capture.scope_denied` | principal/scope/consent denial | denied | tenant (context may be null) |
| `capture.retry_scheduled` | transient failure, retry queued | scheduled | tenant/context/user |
| `capture.failed_terminal` | retry budget exhausted / permanent failure | failed | tenant/context/user |

Audit writes for accepted/duplicate/unsupported are committed atomically with the
canonical persistence. Denial, retry-scheduled, and terminal audits are committed
in their own transaction so they survive a rolled-back capture attempt.

> If a `scope_denied` cannot resolve any tenant (unknown sender), the denial is
> emitted to logs only (no tenant scope exists to satisfy RLS). Alert on the
> `capture.scope_denied could not be persisted` log line.

## 4. Idempotency

- Canonical key: `(tenant_id, source_channel, provider_message_id)`.
- Enforced by `idempotency_record` unique index and an atomic claim
  (`INSERT ... ON CONFLICT DO UPDATE`).
- Duplicates increment `duplicate_count` and return the existing canonical id; no
  new message/media artifacts are created.

## 5. Retry and terminal handling

- Bounded exponential backoff, **max 5 attempts** (`Capture:Retry:MaxAttempts`).
- Only transient faults are retried (connection errors, serialization/deadlock,
  timeouts). Permanent faults fail terminally on the first occurrence.
- Each scheduled retry emits `capture.retry_scheduled` with `attempt_number` and
  `failure_category`.
- Exhaustion emits `capture.failed_terminal` and returns `retry_exhausted`. There
  is no false-success path.

Triage a terminal failure:

1. Find `capture.failed_terminal` rows / `aluki.capture.outcome{result=failed}`.
2. Pull `correlation_id`, `attempt_number`, `failure_category`.
3. Inspect host logs for the matching correlation id stack trace.
4. If `failure_category=transient` and recurring, check PostgreSQL availability
   and connection pool saturation.

## 6. Telemetry & SLOs

Activity source / meter name: `Aluki.Capture`.

- Histogram `aluki.capture.stage.latency{stage,result}` — per-stage latency.
- Counter `aluki.capture.outcome{stage,result,failure_category}` — outcomes.
- Counter `aluki.capture.retry{attempt,failure_category}` — scheduled retries.

Instrumented stages: `ingress`, `scope_check`, `normalize`, `dedupe`, `persist`,
`audit`, `retry_schedule`, `terminal_failure`.

SLO targets (spec SC-006/SC-007):

- P95 ack latency ≤ 2s, P99 ≤ 3s for valid non-blocking events.
- Skill error rate < 1%.

Suggested dashboards/alerts:

- P95/P99 of `aluki.capture.stage.latency{stage=persist}` and end-to-end.
- Rate of `aluki.capture.outcome{result=failed}` (page on sustained > 0).
- Ratio of `capture.duplicate_suppressed` to `capture.accepted` (spike = provider
  redelivery storm).
- Any `capture.scope_denied` burst (possible misconfiguration or abuse).

## 7. Configuration

| Key | Purpose |
|-----|---------|
| `Postgres:ConnectionString` / `PostgresConnectionString` (Key Vault) | database |
| `Capture:SourceChannel` | channel label (default `whatsapp`) |
| `Capture:Retry:MaxAttempts` | retry ceiling (default 5) |
| `Capture:Retry:BaseDelayMilliseconds` / `MaxDelayMilliseconds` | backoff bounds |
| `Capture:ConsentStop:BlockedSenders` | interim consent-stop block list (FR-011) |
| `KeyVault:Enabled` / `KeyVault:Optional` / `KeyVault:VaultUri` | secret loading |

## 8. Database objects

- Migrations: `db/migrations/004_whatsapp_capture_foundation.sql`,
  `db/migrations/005_whatsapp_capture_rls.sql`.

### Migration automation

`azure-deploy-runtime.yml` runs a `migrate-database` job before deploying the
Function App (deploy is skipped if migrations fail). The job:

1. Opens a temporary firewall rule on `pg-araluki-dev-6155` for the runner IP.
2. Reads the connection string from Key Vault (`PostgresConnectionString`).
3. Applies `001`–`005` in order with `psql`, tracked in a `schema_migrations`
   ledger so already-applied files are skipped.
4. Removes the firewall rule (always, even on failure).

Server prerequisites:
- **Public network access** must be enabled (the runner connects over the
  temporary firewall rule). For private-endpoint-only servers, run migrations
  from inside the VNet instead.
- **pgvector** must be allow-listed in the server parameter `azure.extensions`
  (migration `002` runs `create extension vector`).
- Tables: `inbound_message_event`, `unified_message_artifact`, `media_artifact`,
  `idempotency_record`, `capture_audit_event`.
- RLS: all capture tables are tenant-scoped; reads require active membership.
  `capture_audit_event` permits scoped inserts (for denial audits) while keeping
  reads membership-gated.
