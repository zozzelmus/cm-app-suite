# F2 — Tenant context middleware + Postgres RLS

## Goal
Enforce tenant isolation as an airtight Postgres-side boundary. Every request resolves a `TenantId`; that TenantId flows into Postgres via `SET LOCAL app.tenant_id = '<uuid>'`; Row-Level-Security policies on every tenanted table filter `WHERE tenant_id = current_setting('app.tenant_id')::uuid`. Even raw SQL or EF query bugs cannot leak Tenant A data to Tenant B.

Memory references (must read before starting):
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/project_data_access.md` — locked architecture for this feature
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/project_identity.md` — User row carries TenantId
- `CLAUDE.md` — project conventions
- `docs/features/F1-default-casetype-and-tenant-seed.md` — F1 patterns to match (tests via Testcontainers, AwesomeAssertions, etc.)
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/feedback_delivery_discipline.md` — TDD + self-review

## Acceptance criteria
1. **AC1 — `ITenantContext` interface + scoped DI service.** Lives in `libs/Infrastructure/Multitenancy/`. Resolves the current `TenantId` for the request. POC implementation: returns `SeedConstants.DemoTenantId`. Future implementations swap behind the same interface.
2. **AC2 — `TenantContextMiddleware` registered in `apps/api/Program.cs`.** Runs early (before MVC/Endpoints). Sets `ITenantContext` for the request. After F2, this middleware is the only place tenant resolution happens.
3. **AC3 — `TenantConnectionInterceptor : DbConnectionInterceptor`** — when the DbContext opens a connection, it issues `SET LOCAL app.tenant_id = '<uuid>'` so subsequent SQL on that connection is RLS-scoped. Wired in `ConductDbContext.OnConfiguring` (or via `DbContextOptionsBuilder.AddInterceptors`). Important: must use `SET LOCAL` (transaction-scoped) inside an explicit transaction, OR `SET` (session-scoped) if connection pooling resets sessions cleanly. Pick one and document the choice.
4. **AC4 — New EF migration `EnableTenantRls`** — adds RLS to every tenanted table:
   - `ALTER TABLE <t> ENABLE ROW LEVEL SECURITY;`
   - `ALTER TABLE <t> FORCE ROW LEVEL SECURITY;` (so the table owner is also subject to RLS)
   - `CREATE POLICY tenant_isolation ON <t> USING (tenant_id = current_setting('app.tenant_id')::uuid);`
   Applies to: `lobs`, `case_types`, `cases`, `case_parties`, `case_notes`, `parties`, `employee_profiles`, `customer_profiles`, `vendor_profiles`, `external_profiles`, `users`, `roles`, `groups`, `group_memberships`, `assignments`, `audit_events`, `outbox_messages`. Skip if a table doesn't carry TenantId (none should, per design — if any do, FAIL the AC and report).
5. **AC5 — Two-tenant integration test (in `tests/Api.Tests/Multitenancy/`):**
   - Insert a Lob row for Tenant A (`SET app.tenant_id = '<A>'`) and one for Tenant B.
   - Query as Tenant A — see only A's row.
   - Switch session to Tenant B — see only B's row.
   - Attempt cross-tenant SELECT with explicit predicate — RLS still filters.
   - Apply both Initial + EnableTenantRls migrations in the test DB before running assertions.
6. **AC6 — F1 tests still pass.** The `Seeder` already targets `SeedConstants.DemoTenantId`. After F2, seed must still work — confirm by running the test suite end-to-end. If the seed must run as a privileged role that bypasses RLS, document that and adjust the test fixture.

## Scope
**In scope:**
- Interface + middleware + interceptor + migration + tests as listed.
- Update `tests/Api.Tests/Seed/PostgresFixture.cs` if the test database needs `SET app.tenant_id` set before the seeder runs (seed inserts rows for the demo tenant — RLS policy permits inserts where the row's tenant_id matches the session setting).

**Out of scope (DO NOT TOUCH):**
- Anything in `apps/web/**` (Track C is working there in a separate worktree).
- Anything in `libs/Infrastructure/Outbox/**` or new files in `libs/Infrastructure/Kafka/**` (Track B is working there in main).
- `apps/bff/**` (no tenant boundary at BFF — token has it, BFF passes through).
- New domain entities (only RLS scaffolding).

## Manual test plan
1. `dotnet test tests/Api.Tests/Conduct.Api.Tests.csproj` — F1 + F2 tests pass.
2. `dotnet ef migrations script` — script for EnableTenantRls migration is plain SQL, no EF runtime calls in the migration body.
3. Start Aspire stack (`dotnet run --project apps/AppHost --launch-profile http`) — API + BFF + seed all succeed under RLS.

## Self-review checklist
- [ ] All AC met.
- [ ] Tests fail before impl (red), pass after (green).
- [ ] No type-conditional logic on CaseType / Lob / Tenant.
- [ ] `SET LOCAL` vs `SET` choice documented in code comments.
- [ ] `code-reviewer` agent run; findings addressed or justified.
- [ ] No items added to docs/backlog.md silently — surface them.
- [ ] Commit message: `F2: tenant context middleware + Postgres RLS` (mirror F1 style).
