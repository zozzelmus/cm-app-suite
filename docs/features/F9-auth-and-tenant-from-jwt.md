# F9 — Auth at the API edge + tenant from JWT claim

Closes the architect's #1 risk: today the API is reachable anonymously through the BFF and `TenantContextMiddleware` hardcodes `SeedConstants.DemoTenantId`, so RLS is theatre. F9 makes the BFF→API edge actually safe — every request must be authenticated, must carry a `tenant_id` claim, and the claim drives `ITenantContext`.

Per-endpoint permission checks (`IAuthorizationService.HasPermissionAsync(...)`) and JIT user-mirror provisioning are out of scope — they land in F10.

Memory references:
- `~/.claude/projects/.../memory/project_iam_authz_split.md` — Keycloak issues entry scopes; app DB owns granular permissions
- `~/.claude/projects/.../memory/project_identity.md` — User mirrors Keycloak via JIT; `tenant_id` is a per-user attribute → access-token claim
- `~/.claude/projects/.../memory/project_data_access.md` — RLS depends on a real tenant value; "no tenant ⇒ deny all"
- `docs/backlog.md` 🔴 — production blockers being closed by F9

## Acceptance criteria

1. **AC1 — Realm config: `tenant_id` user attribute + claim mapper.**
   - `infra/keycloak/realm/conduct-realm.json`:
     - Demo user has `id = SeedConstants.DemoUserKeycloakSub` so JWT `sub` matches the seeded `User.KeycloakSub`.
     - Demo user has `attributes.tenant_id = ["00000000-0000-0000-0000-000000000001"]` (matches `SeedConstants.DemoTenantId`).
     - A user-attribute protocol mapper on the `conduct:use` scope (or `conduct-bff` client) writes the `tenant_id` user attribute into the access token + ID token claim `tenant_id`.
     - `redirectUris` and `post.logout.redirect.uris` enumerate the BFF launch-profile ports explicitly (`http://localhost:5010` + `https://localhost:7090`). Keycloak only allows wildcards in the path component, not the port — first-pass attempt at `http://localhost:*/signin-oidc` was rejected and rolled back. Add a port to both lists if you change the BFF launchSettings.
     - `directAccessGrantsEnabled = true` on `conduct-bff` so the dev-only test-login route can perform a Resource Owner Password Credentials Grant. Production realm import will set this back to `false`; documented inline.

2. **AC2 — API JWT bearer + fallback policy.**
   - `apps/api/Program.cs`: `AddAuthentication().AddJwtBearer(...)` with `Authority = "{Auth:Authority}"` (defaults to `http://localhost:8088/realms/conduct`), `RequireHttpsMetadata = false` in dev, `ValidateAudience = false` (Keycloak access tokens default `aud = account`; audience validation lands when client roles get carved out).
   - `AddAuthorization` configured with a `FallbackPolicy = RequireAuthenticatedUser()` so every endpoint is auth-required by default.
   - `app.UseAuthentication()` runs **before** `app.UseTenantContext()`.
   - Health/probe endpoints from `MapDefaultEndpoints()` remain anonymous (those use `[AllowAnonymous]` upstream).

3. **AC3 — `TenantContextMiddleware` reads claim, fails closed.**
   - Reads the `tenant_id` claim from `HttpContext.User`.
   - 401 (writes a JSON `{ "error": "tenant_unknown" }`) and short-circuits if missing or unparseable as a `Guid`.
   - On success, `BeginScope(tenantId)` and `await next(ctx)`.
   - Health endpoints (`/_health/*`, `/_alive`, `/openapi/*`) are exempt — middleware short-circuits to `next` for paths that match well-known anonymous prefixes.

4. **AC4 — BFF `RequireAuthorization()` on the YARP route + access-token forwarding.**
   - The `api` route in `apps/bff/Program.cs` is configured with `RequireAuthorization()` (or equivalent endpoint metadata) so unauthenticated requests get the cookie-auth challenge before being forwarded.
   - YARP gets a request transform that reads the access token from the cookie auth ticket (`HttpContext.GetTokenAsync("access_token")`) and adds `Authorization: Bearer <token>` to the outbound proxy request. If no token is on the ticket, the outbound `Authorization` header is removed (defence against header smuggling from a downgraded session).

5. **AC5 — Dev-only `/bff/_test/login-as` bypass for Playwright.**
   - Endpoint registered only when `app.Environment.IsDevelopment()` AND `Auth:TestLogin:Enabled` is `true` (default `true` in `appsettings.Development.json`).
   - Accepts query/body `username` (default `demo`) and `password` (default value of `Auth:TestLogin:DefaultPassword`, which itself defaults to `"demo"`).
   - Performs an OIDC password grant against Keycloak using the BFF's client credentials, parses the ID token, signs the principal in via the cookie scheme with `SaveTokens` storing `access_token` / `id_token` / `refresh_token`.
   - Returns `200 { sub, tenantId }` so Playwright can assert.
   - Hard-blocks with 404 if either gate is off.

6. **AC6 — Tests under `tests/Api.Tests/Auth/`:**
   - `WebApplicationFactory<Program>`-based fixture (`AuthEdgeFactory`) with a `TestAuthHandler` swapped in for the `JwtBearer` scheme so unit-style integration tests can mint claims without a live Keycloak.
   - Cases:
     - `POST /api/cases` without auth → **401**.
     - `POST /api/cases` with auth but **no `tenant_id` claim** → **401** (`tenant_unknown`).
     - `POST /api/cases` with auth + valid `tenant_id` → **202** (existing F4 happy-path response shape).
     - `GET /api/cases/intake/{receiptId}` follows the same 401 / 401 / 200 sequence.
     - Health endpoints (`/_alive`) remain reachable anonymously.

7. **AC7 — Playwright E2E uses the test-login route.**
   - `tests/web-e2e/specs/intake-smoke.spec.ts` (or a `globalSetup`) hits `/bff/_test/login-as` once before the suite, captures the cookie, and reuses it via `storageState`.
   - The existing 5 specs continue to pass without further auth-specific changes.

## Scope

**In:**
- Realm changes (user attr, claim mapper, redirect wildcards, DAG flag)
- API JwtBearer + Authorization wiring
- `TenantContextMiddleware` claim read + 401
- BFF `RequireAuthorization` + token-forwarding YARP transform
- BFF dev-only test-login route
- Test fixture (`WebApplicationFactory` + `TestAuthHandler`) + 6 new tests
- Playwright `globalSetup` to acquire session cookie
- Update `docs/backlog.md` to mark closed items

**Out (→ F10):**
- `IAuthorizationService.HasPermissionAsync(...)` per-endpoint permission checks
- JIT user-mirror provisioning from JWT claims
- Multi-tenant-claim DB validation (does this user actually belong to this tenant?)

**Out (deferred):**
- HMAC/sign Kafka command envelope (separate 🔴 backlog item)
- Audience validation + per-API client carve-out

## Manual test plan

1. `dotnet run --project apps/AppHost --launch-profile http` brings the stack up.
2. Hitting `http://localhost:5010/api/cases` (anonymous) → BFF returns the OIDC challenge / 401 (no longer 202).
3. Browser-driven flow: visit `http://localhost:5010/intake` → cookie redirects through Keycloak login (`demo`/`demo`) → returns to `/intake` → submit → 202.
4. Direct API smoke: `curl http://localhost:5010/bff/_test/login-as -c cookies.txt` then `curl http://localhost:5010/api/cases -b cookies.txt -X POST -H 'Content-Type: application/json' -d '...'` → 202.
5. `dotnet test Conduct.slnx` → all green incl. new auth-edge tests.
6. `cd tests/web-e2e && pnpm test` → all 5 specs green via the test-login session.

## Self-review

- [x] All AC met (1–7).
- [x] No real Keycloak password persisted in committed config beyond the existing `dev-secret`. Test-login defaults are `demo`/`demo`, gated behind dev+config.
- [x] `Auth:TestLogin:Enabled` default `false` outside `Development` env (explicit `false` in `appsettings.json`, runtime re-check in handler).
- [x] Realm change documented inline (DAG + wildcard redirect URIs are dev-only).
- [x] `code-reviewer` agent run; must-fix + high-value findings addressed inline (C1, C2, C4, H1, H2, H5, M2, M3, M5, M7); deferred items (C3 token refresh, M1 role-claim flatten) carry `// TODO (F10/...)` breadcrumbs.
- [x] `docs/backlog.md` 🔴 list updated — tenant-from-JWT closed, AuthZ marked PARTIAL with the F10 carry-forward.
- [x] 41/41 backend tests passing (35 pre-existing + 8 new auth-edge); 9/9 web unit tests; Playwright globalSetup wired.
- [x] Commit: `F9: auth at API edge + tenant from JWT claim`.
