# Backlog — future features (post-MVP)

Items captured from grilling sessions and incremental discovery. Not prioritized yet; this is a parking lot, not a roadmap.

## 🔴 Production blockers (architect + user-persona review, F8 close-out)
These items independently fail a basic infosec / compliance review. None should block the demo, but each is a hard gate for any regulated-bank prod environment. Tracked here so they aren't lost.

- **Wire request-derived tenant resolution.** `TenantContextMiddleware` currently hard-codes `SeedConstants.DemoTenantId`. Read `tenant_id` from a validated JWT claim (Keycloak claim mapper) and 401 if absent. **Until this lands the entire RLS story is theatre.**
- **Wire authorization at the API edge.** No `RequireAuthorization()` anywhere; `/api/cases*` is reachable anonymously through the BFF. At minimum: BFF `[Authorize]` on the YARP forwarder for `/api/{**catch-all}`, API endpoints check the `conduct:use` scope + a permission key per endpoint via `IAuthorizationService.HasPermissionAsync(...)`. Add an integration test asserting 401 for un-authed `POST /api/cases`. (Note: Playwright E2E will need a dev-only `_test/login-as` route to bypass OIDC.)
- **Sign or HMAC the Kafka command envelope.** `CaseIntakeConsumerHost` trusts the raw `tenant-id` header. A poisoned/spoofed message can flip ambient tenant under the consumer scope. Producer-side signature over `(tenantId, receiptId, caseTypeKey)` verified before `BeginScope` closes the spoof window.
- **Encrypt-at-rest PII columns.** `CaseParty.DataJson`, `Reporter.ContactEmail`/`ContactPhone`, `OutboxMessage.PayloadJson` are plaintext. Regulatory baseline (CLAUDE.md) says "encrypt at rest, PII tagging" — schema currently has zero PII tags or column-level encryption. Use pgcrypto + per-tenant DEK from Azure Key Vault; tag column on each entity.
- **Replace RLS-exempt `Outbox` with privileged-role read + payload encryption.** Today's RLS exemption (commit `5c6e99c`) makes the relay the one process that can read every tenant's command payloads. Better: keep RLS on `Outbox`, give the relay a dedicated Postgres role with `BYPASSRLS` (Postgres feature designed for this), and envelope-encrypt the payload so even a compromised relay role can't read across tenants.
- **`AuditEvent` INSERT-only role grant migration.** Project_audit_log.md is the policy; no migration enforces it. Test fixture uses superuser so the policy is uncovered. Ship a `CreateAppRuntimeRole` migration that grants only `INSERT` on `AuditEvents`, run integration tests under that role.
- **Quarantine table for outbox poison rows.** `OutboxRelay`'s `MaxAttempts` silent-skip leaves stuck rows invisible to ops. Move to `outbox_dead` + alerting metric.
- **Model-snapshot diff CI check.** Codify the F4-discovered lesson: a CI rule that fails the build when a new entity has `TenantId` but no matching `EnableRowLevelSecurity` migration. Without this the rule will regress.
- **Add EF `HasQueryFilter(x => x.TenantId == ambient)`** alongside RLS so query plans get the column-equality predicate (perf + index hints). Layer 2 of the "belt + suspenders" data-access design that wasn't shipped.

## 🟡 UX gaps surfaced by user-persona review (F8 close-out)
- **No way to choose LOB on intake form.** Hardcoded `INV-APAC` in `IntakePage.tsx`. Add a LobPicker control that fetches the visible LOB tree (filtered by user's accessible LOBs once RBAC is wired).
- **No Title input on intake form.** Currently derived from first 80 chars of summary. Add explicit Title field above the schema-driven body.
- **No reporter / subject / witness inputs on intake form.** API supports them; form doesn't expose them. Without these the intake fidelity is "free-text summary".
- **CasesPage is a placeholder.** Need at minimum a list view + filter so a Compliance Officer can see what was filed. The closed-loop is currently invisible.
- **PII detection / sensitivity tagging on intake.** Regex/PII scanner pass on `summary` + party fields before persist; auto-tag `Confidential` if hit.
- **Forward `traceparent` / correlation id from intake into AuditEvent.Context** so an audit row can be tied back to the originating HTTP trace.
- **Anonymous reporter follow-up token.** Anonymous reporters need a way to add info / check status without creating an account.
- **Schema for occurrence timezone.** UI converts datetime-local to ISO using browser TZ — no record of what TZ the reporter actually meant. Material for cross-jurisdiction cases.

## 🟡 Architecture trades for next 6 months (architect review)
- **Outbox-tenancy:** encrypt-at-rest vs per-tenant relay vs in-database queue (e.g. pg-boss/listen-notify). Current RLS-exempt outbox won't survive a regulator who reads the schema. Recommendation: per-tenant payload envelope encryption keyed off Azure Key Vault, decrypt only inside tenant-scoped consumer.
- **CaseType god-schema vs explicit subtype tables.** JSON-Schema-on-`CaseType` will scale to ~20 types if (a) `FieldsSchema` is referenced via `$ref` to a shared registry, (b) `schemaVersion` is per-CaseType-row not per-instance-only, (c) lifecycle validation gets a real state-machine engine (Stateless / Workflow library). Without those three, dozens of types degenerate into copy-paste schemas.
- **Sync vs async intake on the public-facing channel.** "Sync fallback" reveals that for human-driven UI, the 202-only contract has no UX value. Keep async for high-volume machine intakes (surveillance, hotline) but offer a sync-completing endpoint variant (`POST /api/cases?wait=true`) for the form path.

## 🟡 Decisions to reverse if starting today (architect review)
- **`Outbox` RLS exemption.** Pragmatic under time pressure but a regulator finding waiting to happen.
- **`ITenantContext` as `AsyncLocal` singleton.** Fragile in long-lived background workers; a scoped `ITenantScopeFactory` would be more debuggable.
- **Hand-rolled JSON-Schema → Zod via `new Function`.** Fine today (server-controlled input) but ships an `eval` to every browser. Precompiled per-CaseType bundle (build-time) eliminates the eval.
- **Confluent confluent-local Kafka image for dev.** The advertised-listener port-drift is the bug that motivated the sync-fallback band-aid. RedPanda's single-binary, no-ZK, stable-port story removes the entire class of bug. Don't change prod (Event Hubs); change dev.

## 🟢 Bugs surfaced + already fixed at F8 close
- ~~`Intake:SyncProcess` defaulted `true` globally — would double-process every intake in prod.~~ **FIXED:** default now `IHostEnvironment.IsDevelopment()`-conditional.
- ~~`POST /api/cases` accepted empty `title` and returned 202.~~ **FIXED:** validation added, returns 400 `title_required` / `title_too_long`.
- ~~`CaseAllocator.Render` accepted any seq format spec including format-string-shaped inputs.~~ **FIXED:** whitelist (digits or `D` + digits) added.
- ~~`IntakeStatusEndpoints` returned raw `ErrorsJson` to anonymous callers, leaking internal failure detail.~~ **FIXED:** replaced with `HasErrors` boolean + generic `ErrorSummary`.



## Identity & RBAC
- HR system sync (Workday / SuccessFactors via SCIM 2.0 or REST)
- Keycloak admin event webhook → User.IsActive sync (near-real-time deprovisioning)
- Periodic full reconciliation job (Keycloak Admin API → User mirror)
- Multi-manager quorum: `NofM` and `SpecificUsers` quorum modes
- OOO / delegation chain for approval tasks (manager-delegate routing)
- Group hierarchy (currently flat)
- CaseType-scoped role assignments UI

## Cases
- Bulk operations (close many, transfer many, bulk reassign)
- Bulk import legacy cases (preserves CaseNumber, sets `IsImported=true`)
- Reopen workflow w/ explicit reason capture + privileged-only gate
- Threaded note replies (post-MVP)
- @mention support in notes (parser + `CaseNoteMention` table + routing rule)
- Case priority / severity field (CaseType-config'd levels)

## Tasks framework
- Beyond Cross-LOB Transfer Approval: closure approval, evidence sign-off, escalation review, RCA completion, training verification
- Task SLA + breach notifications
- Concurrent-transfer locking enforcement tests
- Same-person-both-sides approval audit annotation

## Evidence
- TUS resumable upload protocol for >1GB files
- ClamAV scanner container in Aspire local; Azure Defender for Storage in prod
- Per-tenant Azure Storage account / container provisioning
- Evidence redaction tooling (UI for annotating + producing redacted variant)
- Sensitivity-tier promotion UI w/ approval flow
- Retention policy admin UI

## Intake channels
- Email-to-case adapter (IMAP poller / Microsoft Graph subscription)
- Third-party hotline vendor integrations (NAVEX, EthicsPoint, Convercent)
- Surveillance feed adapters (Behavox/Theta Lake/NICE chat-comms; trade surveillance)
- Anonymous reporter follow-up channel (status-check token UI)

## Notifications
- SMS channel implementation (Azure Communication Services / Twilio)
- Microsoft Teams adapter
- User notification preferences UI
- Digest delivery (daily/weekly summaries vs immediate)
- Escalation rules (if not acknowledged in N minutes, fan out)

## SLA / KPIs / dashboards
- SLA model per CaseType (target time-in-state, total time-to-close)
- Time-bucket tracking (active investigation vs approval-wait vs in-flight transfer)
- KPI dashboards per LOB / per CaseType / per role
- Manager dashboard cut (cases in their LOB by stage + age)
- Auditor dashboard cut (audit-event-based timelines)

## Reporting / regulatory
- Regulator export bundle (case + evidence + audit log) in standard formats (e.g., FINRA Form U4 amendments, SAR)
- Per-jurisdiction reporting (UK FCA / US FINRA-SEC / EU / APAC variations)
- Audit log export tooling (filtered, signed)
- Schema migration tools for evolving CaseType field schemas (data backfill)

## Search / discovery
- Full-text search on Case / Notes (Postgres `tsvector` + GIN index for MVP; Azure AI Search later)
- Saved views per user
- Faceted filters on cases list (CaseType, LOB, Status, Date ranges, Custom fields)
- Vector-similarity search for case similarity (uses pgvector — already wired)

## Tenant + multi-tenant
- Per-tenant resolver (subdomain `bank-a.conduct.example` or JWT `tenant_id` claim)
- Per-tenant Keycloak realm vs single realm w/ tenant claim mapper
- Per-tenant Azure Storage container isolation
- Tenant onboarding workflow

## Schema governance
- Confluent Schema Registry integration for Kafka topics
- CaseType field schema versioning + automated data migration
- LOB schema versioning

## Admin UIs
- LOB tree CRUD (add/edit/move/archive LOBs, set ShortCode + ApprovalQuorum)
- CaseType CRUD + lifecycle editor + field schema editor
- Role + Permission management
- Group management
- Assignment management (who has what role on what scope)
- User admin (deprovisioning, manual overrides)

## Operational
- Outbox relay metrics + dead-letter handling
- Kafka topic provisioning automation
- Backup + restore runbook
- Disaster recovery RPO/RTO documentation
- Performance: closure-table for LOB hierarchy if adjacency-walk slows

## Tech debt — surfaced by F4 review
- **JsonSchema parse cache:** `IntakeService.ValidateAgainstSchema` calls `JsonSchema.FromText` on every request. Cache compiled schemas by `(caseTypeId, schemaVersion)` in `IMemoryCache` or `ConcurrentDictionary`. Worth it once intake volume grows.
- **Endpoint-level tests:** current F4 tests cover `IntakeService` directly. Add `WebApplicationFactory`-based tests that exercise the endpoint mapping (route binding, model binding, error response shape, status codes).
- **Idempotency-Key HTTP header:** clients can't dedupe retries today (server generates a new receiptId each call). Reserve the `Idempotency-Key` header name in `IntakeRequest` doc-comment so F7 can adopt later.
- **Tighten outbox payload assertion:** F4 happy-path test does `Contain("summary")`. Deserialize the JSON and assert the structural envelope (`ReceiptId`, `TenantId`, `SchemaVersion`, etc.).
- **Lesson — every new tenanted entity needs its own RLS migration.** F4 surfaced this gap (CaseIntakes shipped without RLS until a follow-up migration). Codify a checklist + maybe a model-snapshot diff test that fails CI if a new tenanted table lacks an `ENABLE ROW LEVEL SECURITY` migration.

## Tech debt — surfaced by F2 review
- **OutboxRelay reads under RLS will return zero rows in production.** `OutboxRelayHost.ExecuteAsync` opens a fresh DI scope per tick but never sets `app.tenant_id`; once migrations apply RLS, the relay SELECT against `Outbox` is filtered to nothing. Fix options: (a) make `Outbox` RLS-exempt (drop policy on that table — outbox is infra, not domain data), (b) loop tenants per tick + set GUC per loop, (c) introduce a privileged "outbox publisher" role that bypasses RLS and connect under it for the relay context. Recommend (a) — simplest, matches "outbox = infra" mental model.
- **Tests-only role grants are DELETE-capable.** `PostgresFixture.CreateFreshMigratedDbAsync` grants `app_user` SELECT/INSERT/UPDATE/**DELETE** on `public`. Production app role should be INSERT/UPDATE-only on `AuditEvents` (per `project_data_access.md`); we'll codify that grant set in a separate migration once the runtime role is defined.
- **Per-test `app_user_<guid>` roles leak in the cluster.** Testcontainers nukes the container on dispose so the leak is bounded, but if we ever add `WithReuse()` for fast inner-loop dev, these accumulate. Drop role on test teardown OR use a single test-cluster role with `SET ROLE`.
- **`TenantContext` has no defence against accidental scope nesting in HTTP path.** Middleware uses `BeginScope` so previous values restore correctly, but if downstream code calls `BeginScope` with a different tenant the ambient flips silently for that subtree. Add an opt-in "strict" mode that throws on conflicting nested scopes.
- **No EF Core global query filter on `TenantId`** — relying solely on RLS means an EF query bug that drops the WHERE clause still works correctly (RLS catches it), but the SQL plan also doesn't get the column-equality predicate, so query stats and indexes that assume `WHERE TenantId = ...` may underperform. Add a complementary `HasQueryFilter(x => x.TenantId == ambient)` once the ambient resolver is wired into the DbContext (post-MVP, paired with LOB-visibility filters per `project_data_access.md` Layer 2).
- **`FrameworkReference Microsoft.AspNetCore.App` on `Conduct.Infrastructure`** pulls in the whole web stack for one middleware. Acceptable for the POC; consider splitting `Conduct.Infrastructure.Web` once we have >1 middleware living there.

## Tech debt — surfaced by F7 (web intake form)
- **F7-followup:** replace fixture import in `IntakePage.tsx` with TanStack Query against `/api/case-types/default` once F4 lands; cache the compiled Zod by `$id+SchemaVersion`.
- **F7-followup:** real Radix-based `Select` for keyboard-navigable enum w/ search (current native `<select>` is fine for MVP).
- **F7-followup:** timezone-aware datetime input (control assumes user's local TZ on submit).
- **Pre-existing lint debt on `main`:** 4 errors break `pnpm lint` if it's CI-gated — `main.tsx` `any`, `auth.tsx` fast-refresh, `vite.config.ts` triple-slash (+ one more). Recommend a chore commit.
- **`new Function('z', code)` in `buildZodFromSchema.ts`:** safe because input is server-controlled CaseType schema, NOT user input. If admin UI ever lets unprivileged users author schemas, switch to a sandboxed `json-schema → zod` runtime (or a precompiled bundle of schemas) to avoid arbitrary code execution.

## Tech debt — surfaced by F3 review
- **Switch outbox publish from per-row `await ProduceAsync` to batched `Produce` + `Flush(ct)`** — current per-row await defeats `LingerMs` and bottlenecks throughput at ~1/RTT. Reviewer flagged HIGH; deferred since correctness is unaffected.
- **`OutboxOptions.MaxAttempts` quarantine instead of skip** — currently rows above MaxAttempts are silently excluded from polling; should move to a `outbox_dead` sister table or surface a metric / log alert when rows quarantine.
- **`OutboxRelayHost.StopAsync` explicit `producer.Flush(TimeSpan.FromSeconds(5))`** once the per-row→batch switch lands (Aspire disposal handles current per-row case).
- **Bound `ProducerConfig.MessageTimeoutMs`** (e.g., 30s) so a stuck broker surfaces as a row-level failure rather than hanging the relay loop.
- **Outbox relay tests missing**: large-batch ordering preservation (50 rows same Key → monotonic offsets), retry-then-success after broker recovers (was AC5's `OutboxRelayIdempotencyTests.cs`), cancellation mid-batch.

## Tech debt — surfaced by F1 review
- **FK constraints** on cross-table Guid columns (Cases.CaseTypeId/OwnerLobId, CaseParty.CaseId/PartyId, Assignments.RoleId/SubjectId, User.PartyId, EmployeeProfile.PartyId, GroupMembership.GroupId/UserId, CustomerProfile.AccountManagerEmployeePartyId, etc.). Currently absent so migrations stay flexible while domain stabilizes; revisit before prod.
- **PostgresFixture cleanup** — drop test databases on dispose (not just stop container) to avoid leaks if container is ever reused (e.g. `WithReuse()` switch).
- **PostgresFixture SQL** — db-name interpolation is regex-bounded by `Guid:N` but the pattern looks injection-shaped; tighten with explicit guard or use a parameterized identifier helper.
