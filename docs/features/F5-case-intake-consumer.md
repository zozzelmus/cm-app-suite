# F5 — `CaseIntakeConsumer` (Kafka → Case write + audit + receipt update)

## Goal
Second half of the async intake pipeline. F4 publishes `CreateCaseCommand v1` to Kafka topic `commands.case.create.v1`; this consumer subscribes, validates the envelope is still well-formed against current `CaseType`, writes the actual `Case` (+ Subjects + Reporter + Witnesses CaseParty rows) inside one DB transaction, emits domain audit events, and updates the `CaseIntake` receipt with `Status=Completed` + `CaseId`. Idempotent on `outbox-id` header so re-delivery doesn't double-write.

Memory references (must read):
- `~/.claude/projects/.../project_messaging_intake.md`
- `~/.claude/projects/.../project_audit_log.md`
- `~/.claude/projects/.../project_case_number.md` — sequence-allocation belongs here, NOT in F4

## Acceptance criteria
1. **AC1 — `CaseIntakeConsumer : BackgroundService`** in `apps/api/Hosted/CaseIntakeConsumer.cs`. Single-partition consumer subscribed to `commands.case.create.v1`. Manual offset commits (commit only after DB write succeeds).
2. **AC2 — Idempotency.** Read `outbox-id` from headers; before processing, check whether a row in `CaseIntake` w/ `ReceiptId = outboxId` already has `Status=Completed` and a `CaseId`. If yes, commit offset and skip. (Out-of-order delivery handled.)
3. **AC3 — `CreateCaseCommand v1` envelope deserialization** with explicit `schemaVersion` field check; reject unknown versions to a poison topic `commands.case.create.poison.v1` (configurable).
4. **AC4 — Atomic write** in one DB tx (wrap in execution strategy):
   - Allocate next `CaseNumber` via Postgres sequence per (TenantId, Year, LobShortCode) — see `project_case_number.md` for format.
   - Insert `Case` row (Status=`Open`, OwnerLobId resolved from `lobShortCode`, etc.)
   - Insert `Party` rows for reporter/subjects/witnesses (use existing parties if matched by `EmployeeId`/`CustomerId`; else create new).
   - Insert `CaseParty` join rows w/ correct `RoleOnCase`.
   - Emit explicit `AuditEvent`s: `CaseCreated`, `PartyAdded` per party, `StateTransition` Initial→Open.
   - Update `CaseIntake.Status = Completed` and `CaseIntake.CaseId`.
   - Commit Kafka offset.
5. **AC5 — Failure paths:**
   - Schema validation fails → `CaseIntake.Status = Failed`, store error list in `CaseIntake.ErrorsJson`, commit offset (don't retry forever).
   - DB transient (e.g., serialization failure) → don't commit offset, let retry strategy backoff and consumer redeliver.
   - Permanent DB errors (constraint violation) → log, dead-letter to poison topic, mark receipt Failed.
6. **AC6 — Tests** (`tests/Api.Tests/IntakeConsumer/`):
   - Happy path: produce a valid envelope to the topic, run consumer one tick, assert Case row + parties + audit events + receipt updated + offset committed.
   - Idempotent re-delivery: produce same envelope twice, only one Case written.
   - Schema fail: bad payload → receipt Failed + offset committed.
   - CaseNumber sequence: produce 3 envelopes for same LOB, assert numbers `2026-INV-APAC-000001`, `-000002`, `-000003`.
7. **AC7 — Existing tests still pass.**

## Scope
**In:**
- `apps/api/Hosted/CaseIntakeConsumer.cs`
- `libs/Infrastructure/Cases/Intake/CaseAllocationService.cs` (CaseNumber sequence + Case construction)
- DI wiring in `Program.cs`
- Tests under `tests/Api.Tests/IntakeConsumer/`

**Out:**
- Status endpoint (F6)
- Web intake (F7 done)
- Notification dispatch on CaseCreated (F9 — defer the wiring; just emit the domain event)

## Manual test plan
1. `dotnet test` — all green.
2. Start Aspire stack.
3. POST a case via `/api/cases` (F4) → consumer picks it up → `Case` row visible in pgAdmin within seconds.

## Self-review
- [ ] All AC met.
- [ ] Idempotency proven by test, not just claimed.
- [ ] No type-conditional logic on `caseTypeKey`.
- [ ] code-reviewer findings addressed.
- [ ] Commit message: `F5: CaseIntakeConsumer`.
