# conduct-app-suite — Claude instructions

## What this is
Greenfield conduct case management app for a finance org. Multi-LOB workflow w/ inter-LOB case transfers. Single-tenant, multi-tenant-ready schema.

## Stack (locked — do not re-litigate)
- **Web:** Vite + React + TS, shadcn/ui + Tailwind, TanStack Query/Table, RHF + Zod
- **BFF:** .NET 10, YARP, Duende.BFF, cookie auth, serves SPA static (same origin)
- **API:** .NET 10, REST + OpenAPI, EF Core 10 + Npgsql, SignalR
- **DB:** PostgreSQL (pgvector); separate DB for Keycloak
- **Auth:** Keycloak self-hosted (Aspire `AddKeycloak` for dev)
- **Orchestration:** .NET Aspire (`apps/AppHost`)
- **Cloud:** Azure (ACA via `azd`)
- **CI:** GitHub Actions, path-filtered

## Layout
```
apps/{web,bff,api,AppHost}/
libs/{Domain,Infrastructure,ServiceDefaults}/
tests/{Api.Tests,Bff.Tests,web-e2e}/
Conduct.slnx                          # Aspire 13 uses new XML solution format
```
Project assembly names: `Conduct.AppHost`, `Conduct.Api`, `Conduct.Bff`, `Conduct.ServiceDefaults`, `Conduct.Domain`, `Conduct.Infrastructure`, `Conduct.Api.Tests`, `Conduct.Bff.Tests`.

## Run / debug locally

**Prereqs:** Docker Desktop running (for Postgres + Keycloak containers). .NET 10 SDK, Node 22+, pnpm 10+.

**One-shot full-stack:**
```bash
# from repo root
dotnet run --project apps/AppHost
```
Aspire dashboard prints URLs (BFF is the user entry point). Postgres uses `pgvector/pgvector:pg17`, Keycloak runs in dev mode w/ realm imported from `infra/keycloak/realm/`.

**Web standalone (no Aspire):**
```bash
cd apps/web && pnpm dev   # http://localhost:5173, proxies /api + /bff to https://localhost:7001
```
Useful for fast UI iteration when BFF/API are already running.

**VSCode:** open repo, run launch config "Aspire AppHost (full stack)" — debugger attaches to AppHost; Aspire's debug session capability spawns child debuggers for `api`/`bff` automatically.

**EF migrations** (added once domain stabilizes):
```bash
dotnet ef migrations add <Name> --project libs/Infrastructure --startup-project apps/api
dotnet ef database update --project libs/Infrastructure --startup-project apps/api
```

**Tests:**
```bash
dotnet test Conduct.slnx                  # xUnit (Api.Tests, Bff.Tests)
cd apps/web && pnpm test                  # Vitest
```

**Default dev creds:** Keycloak admin `admin/admin` at port 8088. Realm `conduct` has demo user `demo/demo`.

## Working with the user (PROTOCOL)
**Grill-me protocol** — when designing features, scoping, or making architectural choices:
1. **One question per turn.**
2. Always include **own recommendation + brief why + concrete alternatives**.
3. User picks or overrides; never proceed on assumptions for product/feature decisions.
4. Reversible infra defaults can be batched as "assumed unless objected".

**Style** — extreme concision; sacrifice grammar for brevity (per global CLAUDE.md).

**Plans** — end every plan w/ a list of unresolved questions (concise, grammar-optional).

## Domain glossary
- **LOB** — Line of Business (internal division). Cases live within one LOB at a time and can be **transferred** between LOBs (first-class workflow).
- **Conduct case** — investigation of employee/firm conduct; typology TBD.
- **Tenant** — for now = the firm itself. Schema carries `TenantId` for future SaaS.

## Regulatory baseline
Finance org → encrypt at rest, append-only audit log, 7yr retention default, PII tagging on sensitive fields. Specific regulator (FCA / FINRA-SEC / etc.) TBD.

## Memory
User-private memory lives at `%USERPROFILE%\.claude\projects\P--Projects-Repos-conduct-app-suite\memory\`. Update when product decisions or user preferences shift.
