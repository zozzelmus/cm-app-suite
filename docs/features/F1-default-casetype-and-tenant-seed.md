# F1 — Default CaseType + base LOB tree + demo Tenant seed

## Goal
Bootstrap the database with the minimum reference data needed for a case to be created in subsequent features:
- One demo `Tenant` UUID (single-tenant POC; multi-tenant resolver is post-MVP)
- The base LOB hierarchy from the grilling session
- The seeded "Default" `CaseType` carrying its lifecycle, fields schema (JSON Schema 2020-12), transfer rules, number format
- A small set of built-in `Role`s referencing the `Permissions` constants
- A demo `User` JIT-mirror entry (so seeded LobMemberships have a real user)
- A demo `Party` + `EmployeeProfile` linked to the demo user

Seeding lives in **`Conduct.Infrastructure.Seed.Seeder`** invoked from `apps/api`'s `Program.cs` during dev startup (gate by env flag). Migration creates schema; seeder fills reference data idempotently.

## Acceptance criteria
1. **AC1 — schema migration runs cleanly.** EF Core `dotnet ef migrations add Initial` produces a working initial migration; `database update` succeeds against a fresh Postgres + pgvector container.
2. **AC2 — seed is idempotent.** Running the seeder twice does not create duplicates. Detects existing rows by deterministic keys (`Lob.ShortCode`, `CaseType.Key`, `Role.Name`, `User.KeycloakSub`).
3. **AC3 — base LOBs created.** After seed, the following exist as a tree:
   ```
   Speak-Up Intake (SUI)
   Compliance (CMP)
   Investigations (INV)
     ├── Investigations APAC (INV-APAC)
     ├── Investigations India (INV-IN)
     └── Investigations Philippines (INV-PH)
   Employee Relations (HR-ER)
   Legal (LEG)
   Internal Audit (IA)
   ```
4. **AC4 — Default CaseType seeded** with:
   - `Key = "default"`, `Name = "Default"`, `IsBuiltIn = true`, `SchemaVersion = 1`
   - `LifecycleJson` containing states `Open → Triaged → Investigating → PendingDecision → Closed` + closure reasons `Substantiated|Unsubstantiated|NoActionWarranted|Withdrawn|Duplicate|OutOfScope`
   - `FieldsSchemaJson` — JSON Schema 2020-12 with `summary` (string, required) + `occurredAt` (date-time) + `severity` (enum: Low/Medium/High/Critical), each w/ `x-ui:control` + `x-ui:order`
   - `PartyDataSchemasJson` — empty object `{}` (per-role schemas optional MVP)
   - `TransferRulesJson` — empty allowed-targets matrix; `ApprovalRequirement` defaults applied at code level when matrix is empty
   - `NumberFormat = "{year}-{lobCode}-{seq:000000}"`
5. **AC5 — built-in Roles seeded** matching the Q23 set:
   - `Investigator` (5 case-related permissions)
   - `LOB Manager` (Investigator + `task.approve.lob_manager`, `case.close`)
   - `LOB Admin` (LOB Manager + `lob.membership.manage`)
   - `Compliance Reviewer` (read + update + `audit.view`)
   - `System Admin` (all permissions)
6. **AC6 — demo User + Party + EmployeeProfile** created, linked: `User.PartyId → Party.Id`, `EmployeeProfile.PartyId → Party.Id`. KeycloakSub matches the realm's `demo` user from `infra/keycloak/realm/conduct-realm.json`.
7. **AC7 — Demo `Investigator` Assignment** for the demo user scoped to `Investigations APAC`.
8. **AC8 — JsonSchema.Net validation passes.** A unit test loads `CaseType.FieldsSchemaJson` and validates a sample valid `Case.Data` payload against it (passes), and an invalid payload (fails with structured errors).

## Scope
**In scope:**
- Initial EF migration
- Seeder service (idempotent, dev-only invocation gate)
- Default CaseType lifecycle + fields schema content
- All entities listed in AC3–AC7
- Unit tests for: idempotency, JSON Schema validation, lifecycle JSON shape

**Out of scope (deferred to later features):**
- Tenant context middleware (F2)
- RLS migration (F2)
- Kafka publishing (F3)
- API endpoints (F4–F6)
- Web UI (F7)
- Real HR sync, multi-tenant resolver, admin CRUD UIs

## Manual test plan
1. `dotnet ef migrations add Initial --project libs/Infrastructure --startup-project apps/api` produces a migration file.
2. `dotnet run --project apps/AppHost` brings up Postgres; API runs migrations + seed on startup.
3. Connect via pgAdmin (Aspire-spawned) and verify rows in `lobs`, `case_types`, `roles`, `users`, `parties`, `employee_profiles`, `assignments`.
4. Kill + restart AppHost → no duplicate rows (seed is idempotent).
5. `dotnet test tests/Api.Tests/Conduct.Api.Tests.csproj` — F1 tests pass.

## Files (planned)
**Add:**
- `libs/Infrastructure/Seed/Seeder.cs` — orchestrator
- `libs/Infrastructure/Seed/SeedData/DefaultCaseType.cs` — content (JSON literal strings)
- `libs/Infrastructure/Seed/SeedData/BaseLobs.cs`
- `libs/Infrastructure/Seed/SeedData/BuiltInRoles.cs`
- `libs/Infrastructure/Seed/SeedData/DemoUser.cs`
- `libs/Infrastructure/Seed/SeedData/Tenants.cs` — single demo tenant constant
- `libs/Infrastructure/Migrations/00000000000000_Initial.cs` (auto-generated)
- `tests/Api.Tests/Seed/SeederIdempotencyTests.cs`
- `tests/Api.Tests/Seed/DefaultCaseTypeSchemaTests.cs`

**Modify:**
- `apps/api/Program.cs` — wire seed invocation in dev
- `apps/api/Conduct.Api.csproj` — add `JsonSchema.Net` package
- `tests/Api.Tests/Conduct.Api.Tests.csproj` — add Testcontainers + JsonSchema.Net for tests

## Self-review checklist (before commit)
- [ ] All AC met; manual test plan passes
- [ ] Tests fail before impl, pass after
- [ ] No hardcoded type-conditional logic on CaseType.Key
- [ ] Seeder idempotency: detected by stable keys, not row count
- [ ] JSON schema content validated by JsonSchema.Net (no malformed JSON committed)
- [ ] `code-reviewer` subagent run; findings addressed or justified
- [ ] No new entries in this feature doc beyond acceptance — anything new goes to `docs/backlog.md`
