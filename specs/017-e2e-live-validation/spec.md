# SB-017 — E2E Live Validation Test Suite

## Problem

Unit and contract tests cover individual components in isolation. There is no automated
test that exercises the full production stack: Meta webhook → Azure Function → dispatch →
agent → WhatsApp reply → Postgres persistence.

Gaps this test suite closes:
- Correct HMAC-SHA256 webhook signature enforcement.
- End-to-end dispatch routing (correct agent selected for each intent).
- Reply content visible to the user (outbound_messages body).
- Postgres side-effects (reminders row, dispatch audit events).
- SheloNabel routing detection (priority-40 agent may intercept before Aluki agents).

## Scope

Test sender: **+14252307522** (seeded principal, tenant `44444444-…`, user `55555555-…`).
Target: deployed Azure Function `func-araluki-dev-6155`.
Database: direct Postgres reads for assertions (`app.outbound_messages`, `app.dispatch_audit_events`, `app.reminders`).

Real WhatsApp sends reach the user's phone — that is expected and acceptable.

## Test scenarios

| ID | Intent | Expected agent | Reply assertion |
|----|--------|---------------|----------------|
| T01 | Any message | — | HTTP 200 |
| T02 | Tampered signature | — | HTTP 401 |
| T03 | `guarda que Fer es mi prima` | `memory.person_note` | body ⊇ `¡Anotado! 📒` |
| T04 | `¿quién es Fer?` | `memory.person_lookup` | body ⊇ `📇 *Fer*` |
| T05 | `¿quién es Zaphod Beeblebrox?` | `memory.person_lookup` | body ⊇ `No tengo notas sobre *Zaphod Beeblebrox*` |
| T06 | `borra lo de Fer` | `memory.note_deletion` | body ⊇ `Olvidado 🗑️` |
| T07 | `borra lo de Aragorn` | `memory.note_deletion` | body ⊇ `No encontré notas sobre *Aragorn*` |
| T08 | `recuérdame llamar al dentista mañana a las 10` | `reminders.whatsapp_scheduler` | body ⊇ `✅` (or clarification); reminders row exists |
| T09 | `recuérdame comprar leche` (no time) | `reminders.whatsapp_scheduler` | body exists, no `✅` |
| T10 | `agéndame una reunión mañana a las 3pm` | `calendar.scheduling` | body mentions calendar/conectar |
| T11 | Link URL without `?` | `conversation.whatsapp_response` | body ⊇ `Guardado 🔗` |
| T12 | `¿qué notas tengo?` | `conversation.whatsapp_response` | non-empty body |
| T13 | `¿cuál es la receta de la paella?` | `conversation.whatsapp_response` | body does NOT contain recipe content |
| T14 | Audit completeness | — | 0 `contained_failure` events in last 10 min |

T03 must run before T04 (lookup requires the note to exist) and T06 (deletion requires it too).
This ordering is enforced via `xunit.extensions.ordering`.

## SheloNabel routing

`SheloNabelDomainAgent` (priority 40) intercepts messages from `SheloNabel__AuthorizedWaIds`
before Aluki agents. A preflight probe detects this: if `14252307522` is in that list,
T03–T13 are skipped (not failed) with the message "Messages from 14252307522 route to
SheloNabelDomainAgent". T01, T02, and T14 always run.

Resolution: remove `14252307522` from `SheloNabel__AuthorizedWaIds` in the Function app
settings when running E2E tests, or seed a dedicated test wa_id.

## Implementation

- **Project**: `tests/e2e/Aluki.Runtime.E2ETests/` (Category=E2E, not referenced by deploy CI)
- **Workflow**: `.github/workflows/e2e-whatsapp.yml` (`workflow_dispatch` only)

### Required env vars / secrets

| Env var | Source |
|---------|--------|
| `E2E_FUNCTION_URL` | workflow input (default: `https://func-araluki-dev-6155.azurewebsites.net`) |
| `E2E_META_APP_SECRET` | Key Vault `Meta--AppSecret` |
| `E2E_META_PHONE_NUMBER_ID` | Key Vault `Meta--PhoneNumberId` |
| `E2E_POSTGRES_CONNECTION` | Key Vault `PostgresConnectionString` |
| `E2E_SENDER_WA_ID` | hardcoded `14252307522` |

GitHub Actions secrets needed: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
(same as deploy workflow).

### Local run

```bash
export E2E_FUNCTION_URL=https://func-araluki-dev-6155.azurewebsites.net
export E2E_META_APP_SECRET=<secret>
export E2E_META_PHONE_NUMBER_ID=<pnid>
export E2E_POSTGRES_CONNECTION="<connection string>"

dotnet test tests/e2e/Aluki.Runtime.E2ETests/ --filter "Category=E2E" -v normal
```

## Non-goals

- No migration changes (reads existing tables only).
- Does NOT run on push to `main` (deploy pipeline uses `Category=Unit|Category=Contract`).
- Does NOT send real media/audio — text-only webhook payloads.
