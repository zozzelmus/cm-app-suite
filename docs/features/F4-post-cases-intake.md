# F4 — `POST /api/cases` intake endpoint (outbox publish, 202 Accepted)

## Goal
First half of the async intake pipeline. Browser/external POSTs an intake payload → API validates the shape against the `CaseType.FieldsSchemaJson`, writes a `CreateCaseCommand` envelope to the outbox in the same DB transaction → returns **202 Accepted** with `{ receiptId, statusUrl }`. Behind the scenes F3's relay publishes the row to Kafka; F5's `CaseIntakeConsumer` writes the actual `Case`.

Memory references (must read):
- `~/.claude/projects/.../project_messaging_intake.md` — locked async-everywhere flow, 202 + receiptId contract
- `~/.claude/projects/.../project_custom_fields.md` — JSON Schema validation via JsonSchema.Net
- `~/.claude/projects/.../project_audit_log.md` — domain events on intake-accepted

## Acceptance criteria
1. **AC1 — Endpoint:** `POST /api/cases` accepts JSON body `{ caseTypeKey: string, lobShortCode: string, title: string, data: object, externalRefs?: object, reporter?: { ... }, subjects?: [...] }`. Response 202 with `Location: /api/cases/intake/{receiptId}` and body `{ receiptId, statusUrl }`. Error responses: 400 (schema/validation), 404 (unknown caseTypeKey or lobShortCode), 422 (RoleOnCase / lifecycle violations against the type's schema).
2. **AC2 — `IntakeReceipt` entity** in Domain (or a value-object on the outbox row): a stable id assigned at intake; consumer writes the actual `CaseId` against the receipt when processing completes (F6 owns the lookup).
3. **AC3 — `CreateCaseCommand v1` JSON envelope:** `{ receiptId, caseTypeKey, lobShortCode, title, data, externalRefs, reporter, subjects, schemaVersion }`. Topic name: `commands.case.create.v1`.
4. **AC4 — Validation pipeline:**
   - Resolve `CaseType` by `caseTypeKey` (404 if missing).
   - Resolve `Lob` by `lobShortCode` (404 if missing).
   - Validate `data` against `CaseType.FieldsSchemaJson` via `JsonSchema.Net` (400 with structured errors on fail).
   - For each entry in `subjects` / `reporter`, validate party shape against `CaseType.PartyDataSchemasJson[role]`.
5. **AC5 — Atomic outbox write:** in one DB transaction, write `OutboxMessage(Topic="commands.case.create.v1", Key=receiptId, PayloadJson=<envelope>, TenantId=...)` AND any persistent intake-tracking row (e.g., a `CaseIntake` table mapping receiptId → status/lastUpdatedAt/finalCaseId).
6. **AC6 — Tests** (`tests/Api.Tests/Intake/`):
   - Happy path: valid POST → 202 + receipt format + outbox row exists w/ correct envelope.
   - Schema fail: missing `summary` (required) → 400 w/ structured errors.
   - Schema fail: extra unknown property → 400 (default CaseType has `additionalProperties: false`).
   - Unknown caseTypeKey / lobShortCode → 404.
   - Idempotency stub: same receiptId already exists → handle gracefully (return existing receipt).
7. **AC7 — F1+F2+F3 tests still pass.**

## Scope
**In:**
- `apps/api/Endpoints/Intake.cs` (or wherever endpoint mappings live)
- `libs/Domain/Cases/Intake/CaseIntake.cs` + `CreateCaseCommand.cs` envelope DTO
- DbContext registration for `CaseIntake`
- New EF migration `AddCaseIntake`
- Tests under `tests/Api.Tests/Intake/`

**Out:**
- Consumer side (F5)
- Status endpoint (F6)
- Web form integration (F7 already in flight)
- Anonymous/public route — separate path (post-MVP)

## Manual test plan
1. `dotnet test` — all tests pass
2. Start Aspire stack
3. `curl -X POST http://localhost:5010/api/cases -H 'Content-Type: application/json' -d '{ "caseTypeKey": "default", "lobShortCode": "INV-APAC", "title": "Smoke test", "data": { "summary": "Hello" } }'` — receive 202 + receiptId
4. Inspect outbox row in pgAdmin; row should be `PublishedAt` non-null shortly after (the F3 relay picks it up)
5. Inspect Kafka UI for the `commands.case.create.v1` topic — message present

## Self-review
- [ ] All AC met.
- [ ] No tenant-resolution leakage (uses ITenantContext from F2 once that lands; meanwhile read SeedConstants.DemoTenantId — flag this in code w/ a TODO referencing F2).
- [ ] No type-conditional logic on caseTypeKey value.
- [ ] code-reviewer agent run; findings addressed or backlogged.
- [ ] Commit message: `F4: POST /api/cases intake endpoint`.
