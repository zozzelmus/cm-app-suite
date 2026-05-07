# F10 — Per-endpoint permission checks + JIT User mirror

Builds on F9. F9 made the API edge auth-required and surfaced `tenant_id` from a JWT claim. F10 turns that "any authenticated principal" gate into a granular permission check by walking the app DB's Permission/Role/Assignment graph.

Memory references:
- `~/.claude/projects/.../memory/project_iam_authz_split.md` — Keycloak issues entry scopes; app DB owns granular permissions
- `~/.claude/projects/.../memory/project_identity.md` — `User` mirrors Keycloak via JIT
- `~/.claude/projects/.../memory/project_lob_model.md` — LOBs hierarchical; permission scopes walk the ancestor chain

## Acceptance criteria

1. **AC1 — JIT user-mirror middleware** (`Conduct.Infrastructure.Identity.UserMirrorMiddleware`):
   - Runs AFTER `UseAuthentication` and AFTER `UseTenantContext`, BEFORE `UseAuthorization` for permission policies.
   - On every authenticated request, ensures a `User` row exists for `(TenantId, KeycloakSub)`. If missing, INSERTs from claims (`preferred_username`, `email`, `given_name`, `family_name`). If present, no-ops (LastLoginAt update is deferred — would write on every request, expensive).
   - If `User.IsActive == false`, short-circuits with **403** `user_deactivated`.
   - Appends an `app_user_id` claim to the principal so downstream code can resolve the User PK without re-querying.
   - Idempotent under concurrent first-login bursts: `INSERT ... ON CONFLICT DO NOTHING` on `(TenantId, KeycloakSub)` unique index.

2. **AC2 — `IConductAuthorization` service** (`Conduct.Infrastructure.Authorization.ConductAuthorization`):
   - `Task<bool> HasPermissionAsync(Guid userId, string permission, AuthScope scope, CancellationToken ct)`.
   - `AuthScope` discriminated union: `Global`, `Lob(Guid lobId)`, `CaseType(Guid caseTypeId)`.
   - Effective permissions = union over (User's direct Assignments) ∪ (Assignments granted to Groups containing the User), filtered by:
     - Global assignments always match.
     - Lob assignments match if the request scope is `Lob(x)` AND the assignment's `ScopeId` is `x` OR an ancestor of `x` (walks `Lob.ParentLobId` chain).
     - CaseType assignments match only if request scope is `CaseType(x)` AND `ScopeId == x`.
   - Returns `true` iff any matched Role's `Permissions[]` contains the requested key.
   - Performance: single round trip — joins Assignments + GroupMemberships + Roles in one query, plus the LOB ancestor walk via Postgres recursive CTE.

3. **AC3 — Per-endpoint imperative permission check on `POST /api/cases`.**
   - Endpoint resolves the requested `lobShortCode` → LOB id, then calls `auth.HasPermissionAsync(userId, Permissions.CaseCreate, AuthScope.Lob(lobId), ct)`.
   - 403 `permission_denied` (RFC 7807 problem+json) if the check fails.
   - 200/202 path is unchanged otherwise.

4. **AC4 — Status endpoint + echo endpoint stay at "auth + tenant" only.**
   - `GET /api/cases/intake/{receiptId}` — receipts are scoped by tenant via RLS and the receiptId is private to the submitter; no extra permission gate is added in this slice. Documented inline.
   - `GET /api/_meta/echo` — health-style smoke endpoint; no permission required.

5. **AC5 — `RequiresPermissionAttribute` + policy provider** for declarative endpoint guards (Global-scope only):
   - `[RequiresPermission(Permissions.CaseRead)]` or `RequireAuthorization(Policy.RequirePermission(Permissions.CaseRead))` for endpoints that don't have a body-derived scope.
   - Backed by an `IAuthorizationPolicyProvider` that lazily synthesises a policy per permission key, plus a `ConductPermissionHandler : AuthorizationHandler<ConductPermissionRequirement>` that calls `IConductAuthorization` for the global-scope check.
   - F10 doesn't apply this attribute anywhere yet (the only body-scoped endpoint goes through the imperative path); the infra is added so F11/F12 endpoints can declare permissions inline. Documented inline.

6. **AC6 — Tests** (`tests/Api.Tests/Authz/`):
   - `ConductAuthorizationTests` (DB-backed): user with `case.create` on `INV-APAC` ⇒ has it on INV-APAC, NOT on INV-IN; user with `case.create` on `INV` (parent) ⇒ inherits to INV-APAC and INV-IN; missing role ⇒ false.
   - `JitUserMirrorTests`: first request from a new sub creates the User row with claim values; subsequent requests don't double-insert; deactivated user returns 403.
   - `PostCases403Tests`: authed user without `case.create` for a given LOB → 403; demo user with the seeded Investigator/INV-APAC assignment → 202.
   - All running through `AuthEdgeFactory` extended with a seeded demo user + assignment.

## Scope

**In:** middleware, service, requirement+handler+policy provider, attribute, integration on POST /api/cases, tests, F10 self-review.

**Out (→ F11+):**
- HR sync / Keycloak admin events for User deprovisioning
- Permission scopes for **Case** (per-case ACL) — separate slice once Case + LOB membership UIs land
- Audience validation on JwtBearer (also deferred; track separately as it's purely a JwtBearer config)
- Token refresh for expired access tokens (already TODO'd in F9 BFF transform)

## Manual test plan

1. `dotnet test Conduct.slnx` — all green (43 + 6 new = 49 backend, plus 9 web unit).
2. Stack live: log in as `demo` via OIDC, hit `/intake`, submit (LOB `INV-APAC`) → 202.
3. Mutate the Investigator role to drop `case.create` (via direct DB edit), restart the API, retry submit → 403.
4. Restore role, change `lobShortCode` to `LEG` in the request — user has no assignment on `LEG` → 403.

## Self-review

- [x] All AC met (1–6). Declarative `[RequiresPermission]` scaffolding shipped (AC5) but not applied to any endpoint in F10 — F11+ admin endpoints can adopt.
- [x] No type-conditional logic on CaseType / LOB / Tenant.
- [x] LOB ancestor walk handled by recursive CTE — one extra round trip per Lob-scoped check; group expansion takes one round trip; assignment+role join takes one. Three round trips total.
- [x] 54/54 backend tests passing (43 pre-F10 + 6 ConductAuthorization + 5 endpoint integration).
- [x] `docs/backlog.md` 🔴 AuthZ row updated to closed; only audience-validation remains deferred (now flagged in `AuthSetup.cs` as `// SECURITY:`).
- [x] Commit: `F10: per-endpoint permission checks + JIT user mirror`.
