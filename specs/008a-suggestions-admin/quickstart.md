# Quickstart Validation: Suggestions Admin and Rewards (SB-008A)

This guide validates end-to-end behavior for admin triage, immutable audit, idempotent rewards, and notification retry semantics.

## Prerequisites

- .NET 10 SDK installed
- Database migrations for suggestions admin and reward ledgers applied
- Tenant test data available with sample suggestions in `captured` and `under_review`
- Test principals configured with roles:
  - `AdminReviewer`
  - `AdminApprover`
  - `AdminAuditor`
- Runtime services available:
  - Host service (`src/Aluki.Runtime.Host`)
  - Durable Functions app (`src/Aluki.Runtime.Functions`)

## Setup

1. Build solution:
   - `dotnet build Aluki.Runtime.slnx -v minimal`
2. Start host service:
   - `dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj`
3. Start functions service (if separate process):
   - `dotnet run --project src/Aluki.Runtime.Functions/Aluki.Runtime.Functions.csproj`

## Scenario 1: RBAC Action Matrix

1. As `AdminReviewer`, call list/detail and perform:
   - classification update
   - `captured -> under_review`
2. As `AdminReviewer`, attempt `under_review -> accepted`.
3. As `AdminApprover`, perform `under_review -> accepted` and `accepted -> archived`.
4. As `AdminAuditor`, attempt any mutation endpoint.

Expected outcomes:
- Reviewer allowed only reviewer actions.
- Reviewer forbidden from final approvals.
- Approver allowed all review + final transitions.
- Auditor receives forbidden for mutations, read allowed.
- Denied operations produce auditable authorization records.

## Scenario 2: Immutable Audit for Triage Mutations

1. Perform status/category/priority updates for a suggestion.
2. Query suggestion audit endpoint.
3. Attempt direct update/delete on audit ledger row (through privileged test path).

Expected outcomes:
- Exactly one audit record per mutation with actor, old/new values, reason, timestamp.
- Immutable sequence ordering maintained.
- Update/delete prohibited; corrections represented only via compensating inserts.

## Scenario 3: Reward Idempotency Replay (Same Payload)

1. Submit `POST /admin/rewards/decide` with a boundary tuple and payload.
2. Re-submit same request multiple times and concurrently.

Expected outcomes:
- First request returns `granted` (or rule-appropriate decision).
- Replays return deterministic existing outcome (`duplicate` semantics).
- No duplicate entitlement rows are created.

## Scenario 4: Reward Conflict on Payload Mismatch

1. Submit a valid reward decision request.
2. Re-submit same boundary tuple with changed `policy_version`, amount, or `rule_metadata`.

Expected outcomes:
- Response decision is `conflict`.
- No new entitlement grant side effect is created.
- Conflict decision is auditable and telemetry is emitted.

## Scenario 5: Notification Retry and Dead-Letter

1. Trigger successful reward grant where notification channel is forced to fail.
2. Observe delivery attempts over schedule: 1m, 5m, 15m, 60m, 360m.
3. Verify terminal dead-letter after attempt 5.
4. Execute operator replay flow and verify new attempt chain.

Expected outcomes:
- Grant accounting remains unchanged during all notification retries.
- Delivery state transitions to `dead_letter` after fifth failed attempt.
- Replay is explicit operator action; no automatic infinite retries.

## Scenario 6: Queue Filtering and Deterministic Pagination

1. Seed high-volume suggestions dataset.
2. Execute queue queries with status/category/priority/date/search filters.
3. Re-run same query parameters multiple times.

Expected outcomes:
- Deterministic order and stable pagination.
- Response time remains within operational target for triage use.

## Validation Checklist

- [ ] RBAC role boundaries enforced and auditable.
- [ ] Controlled lifecycle transitions enforced.
- [ ] Audit and entitlement ledgers behave as append-only WORM stores.
- [ ] Idempotency tuple honored exactly.
- [ ] Payload mismatch conflict behavior validated.
- [ ] Notification retry schedule and dead-letter behavior validated.
- [ ] No duplicate grants under retries or concurrency.

## Related Artifacts

- Plan: `specs/008-suggestions-admin/plan.md`
- Research: `specs/008-suggestions-admin/research.md`
- Data model: `specs/008-suggestions-admin/data-model.md`
- Contracts: `specs/008-suggestions-admin/contracts/suggestions-admin-contract.yaml`