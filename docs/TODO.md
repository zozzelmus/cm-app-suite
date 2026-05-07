# TODO — current state of work

> Snapshot of what's done and what's queued. Long-horizon items live in [`backlog.md`](./backlog.md). Session-by-session notes live in [`MORNING-SUMMARY.md`](./MORNING-SUMMARY.md).

## Done — vertical slice F1–F8 ✅
| Feature | Description | Tests |
|---|---|---|
| F1 | Default CaseType + LOB tree + Roles + demo User+Party seed | 9 |
| F2 | Tenant context middleware + Postgres RLS | 5 |
| F3 | Kafka outbox relay (FOR UPDATE SKIP LOCKED, MaxAttempts) | 5 |
| F4 | `POST /api/cases` intake endpoint | 8 |
| F5 | `CaseIntakeConsumer` (Kafka → Case + parties + audit) | 4 |
| F6 | `GET /api/cases/intake/{receiptId}` status endpoint | 4 |
| F7 | Schema-driven intake form (Vite + React + RHF + Zod + shadcn) | 9 |
| F8 | Playwright E2E smoke + sync intake fallback | 5 |
| **Σ** | | **49** |

Plus must-fix patches from architect + user-persona evaluator passes (commit `f64c28d`, `86019a6`):
- Sync-fallback default → `IsDevelopment()` only
- Title validation (`title_required` / `title_too_long`)
- `CaseAllocator` seq-format whitelist
- Status endpoint no longer leaks raw `ErrorsJson`

## Up next (small / next session)
1. **Pick which 🔴 blocker to attack first** — see [backlog.md ▸ Production blockers](./backlog.md#-production-blockers-architect--user-persona-review-f8-close-out). Highest leverage: tenant resolution from JWT claim → unblocks AuthZ at the API edge → unblocks bank-prod readiness.
2. **Add a CasesPage list view** — closes the user feedback loop ("I filed a case; where did it go?"). Currently a placeholder.
3. **Add LobPicker + Title input + reporter/subject/witness controls** to the intake form. The schema-driven body covers `data` only; envelope-level fields are hardcoded.
4. **Add Tasks framework scaffolding** — designed in memory (`project_tasks_framework.md`) but no entities yet. First Task type: Cross-LOB Transfer Approval.

## Up next (medium / next 1–2 sessions)
5. **Replace dev Confluent image with RedPanda** — eliminates the advertised-listener port-drift bug that motivated the sync-fallback band-aid.
6. **Wire `[Authorize]` on YARP `/api/{**catch-all}`** + `RequireAuthorization` per endpoint, with a dev-only `_test/login-as` route for Playwright.
7. **Sign / HMAC the Kafka command envelope** so the consumer can't trust a forged `tenant-id` header.
8. **`AuditEvent` INSERT-only role-grant migration** + tests under that role.
9. **Outbox payload envelope encryption** (Azure Key Vault, per-tenant DEK).
10. **Model-snapshot CI check** — fail the build when a new entity has `TenantId` but no matching `EnableRowLevelSecurity` migration.

## Up next (deferred / 6-month horizon)
- **Architecture trades** see [backlog.md ▸ Architecture trades](./backlog.md#-architecture-trades-for-next-6-months-architect-review):
  - Outbox tenancy strategy (encrypt-at-rest vs per-tenant relay vs in-DB queue)
  - CaseType god-schema vs explicit subtype tables
  - Sync vs async on the public-facing intake channel
- Decisions to reverse if starting today (`backlog.md ▸ Decisions to reverse`).

## Live state
- Stack run: `dotnet run --project apps/AppHost --launch-profile http`
- SPA: `http://localhost:5010` · Intake: `/intake` · Aspire dashboard: `http://localhost:15041`
- Tests: `dotnet test Conduct.slnx` · `cd apps/web && pnpm test` · `cd tests/web-e2e && pnpm test`
- E2E videos: `tests/web-e2e/artifacts/test-output/*-chromium/video.webm`
