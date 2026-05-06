# F8 — E2E Playwright smoke test

## Goal
End-to-end coverage for the canonical flow: launch the Aspire stack, hit the BFF, render the SPA, sign in via Keycloak (or skip auth in test mode), submit an intake form, observe the case create receipt + eventually the resulting case via the status endpoint. Catches regressions across BFF/API/web/Kafka/Postgres at once.

## Acceptance criteria
1. **AC1 — Test project at `tests/web-e2e/`** (separate from web app). Playwright config + `package.json` w/ `@playwright/test` dep. Targets the BFF entry URL (`http://localhost:5010` by default; configurable).
2. **AC2 — Auth handling:** two strategies supported via env flag:
   - `bypass` mode (default for CI smoke): `/bff/_test/login-as` endpoint that's only registered in `Development` and accepts a JSON body `{ keycloakSub }` and creates a session cookie. Tests use this to skip the OIDC redirect dance.
   - `oidc` mode: full Keycloak login form interaction. For local manual runs.
3. **AC3 — Smoke spec `tests/web-e2e/specs/intake.spec.ts`:**
   - "anonymous user redirected to sign-in" — visits `/`, sees sign-in CTA, no React errors in console.
   - "authenticated user can submit intake" — logs in via bypass route, navigates to `/intake`, fills required fields (summary), submits, sees the receipt-id success state.
   - "after submit, GET /api/cases/intake/{receiptId} eventually returns Completed" — polls F6's status endpoint w/ a short timeout, asserts caseId + caseNumber populated.
4. **AC4 — Trace + screenshot artifacts** on failure. Playwright's built-in `--trace=retain-on-failure` + `--video=retain-on-failure`.
5. **AC5 — GitHub Actions workflow** `.github/workflows/e2e.yml` — spins up the Aspire stack via `dotnet run --project apps/AppHost` in background, polls for BFF readiness, runs `pnpm exec playwright test`, uploads artifacts. Path-filter so it only runs on relevant PRs.

## Scope
**In:** test harness, smoke spec, dev-only test login endpoint, GHA workflow.
**Out:** broader regression suite (login forms, edge cases, multi-tenant) — defer.

## Manual test plan
1. Stack running locally.
2. `cd tests/web-e2e && pnpm install && pnpm exec playwright test` — all green.
3. Force a regression (e.g., break the `/api/cases` 202 response shape) and confirm the smoke flags it.

## Self-review
- [ ] All AC met.
- [ ] No real Keycloak credentials in test code (bypass route is dev-only and clearly named `_test/`).
- [ ] code-reviewer findings addressed.
- [ ] Commit: `F8: E2E Playwright smoke test`.
