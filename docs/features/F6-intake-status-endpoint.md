# F6 — `GET /api/cases/intake/{receiptId}` status endpoint

## Goal
Closes the async loop. Web client (or any caller with the receipt) polls this endpoint to learn whether their POST-then-202 submission was processed. Returns `{ status, caseId?, caseNumber?, errors? }`.

## Acceptance criteria
1. **AC1 — Endpoint** `GET /api/cases/intake/{receiptId}` returns:
   - 404 if receipt unknown
   - 200 `{ status: "Queued" | "Processing" | "Completed" | "Failed", caseId?, caseNumber?, errors? }` based on `CaseIntake` row state
   - 200 with status `Queued` if outbox row exists but consumer hasn't started, `Processing` if consumer has begun but not committed.
2. **AC2 — Tenant scope:** receipt is scoped by tenant; cross-tenant lookup returns 404 (not 403, to avoid leaking existence).
3. **AC3 — Response shape:** consistent w/ F4's 202 promise (`{ receiptId, statusUrl }`); `statusUrl` is `/api/cases/intake/{receiptId}` + same auth.
4. **AC4 — Caching:** `Cache-Control: no-store` (intake status is real-time).
5. **AC5 — Tests** (`tests/Api.Tests/IntakeStatus/`):
   - Receipt completed → 200 + caseId + caseNumber.
   - Receipt failed → 200 + errors array.
   - Receipt queued (consumer hasn't run) → 200 + status Queued.
   - Unknown receipt → 404.
   - Cross-tenant lookup → 404 (RLS or app-side filter, after F2).

## Scope
**In:** endpoint mapping + `CaseIntake` lookup + minimal tests.
**Out:** SignalR push notifications when status flips (defer to a follow-up — design supports it via existing notification framework).

## Manual test plan
1. POST via F4 → receipt
2. Poll F6 — see Queued → Processing → Completed within seconds
3. Web `IntakePage.tsx` (F7) wiring stays unchanged — its `bff` POST gets 202 + statusUrl, can poll F6 if desired (but F7 just shows the receipt for MVP)

## Self-review
- [ ] All AC met.
- [ ] No tenant leakage.
- [ ] code-reviewer findings addressed.
- [ ] Commit: `F6: intake status endpoint`.
