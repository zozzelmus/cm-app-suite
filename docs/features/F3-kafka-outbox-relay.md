# F3 — Kafka outbox relay (background service)

## Goal
Bridge Postgres `OutboxMessage` rows to Kafka. Domain handlers write business state + an outbox row in the same DB transaction; this background service polls the outbox and publishes to Kafka, marking rows as published. At-least-once delivery; consumers must be idempotent (the outbox `Id` is the message key for dedup).

Memory references (must read):
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/project_messaging_intake.md` — locked Kafka + outbox architecture
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/project_audit_log.md` — outbox + audit relationship
- `~/.claude/projects/P--Projects-Repos-conduct-app-suite/memory/feedback_delivery_discipline.md` — TDD + self-review

## Acceptance criteria
1. **AC1 — `OutboxRelay : BackgroundService`** in `apps/api/Hosted/OutboxRelay.cs`.
   - Polls `OutboxMessage` WHERE `PublishedAt IS NULL` ordered by `CreatedAt`, batched (default 50/poll, configurable via `Outbox:BatchSize`).
   - Poll interval 250 ms when there's work; 2 s when idle (back-off configurable via `Outbox:IdleDelay` / `Outbox:BusyDelay`).
   - Per row: build a `Confluent.Kafka.Message<string, string>` w/ `Key = Id` (so consumers dedup on it) and `Value = PayloadJson`; produce to `Topic = OutboxMessage.Topic`.
   - On successful produce: set `PublishedAt = utcNow`, save.
   - On produce failure: increment `AttemptCount`, set `LastError`, save. Don't crash the relay; back off and retry on next poll.
   - Graceful shutdown on `stoppingToken`.
2. **AC2 — Producer registration in DI.** Aspire's `Aspire.Confluent.Kafka` is already added; use `builder.AddKafkaProducer<string, string>("kafka")` to register a singleton `IProducer<string, string>` typed for our string-keyed messages. Wire the relay as a `BackgroundService` (`builder.Services.AddHostedService<OutboxRelay>()`).
3. **AC3 — Producer config:** acks=all, enable.idempotence=true, linger.ms=5, compression=zstd. Surface knobs via `Outbox:Producer:*` config.
4. **AC4 — Health endpoint integration:** the relay reports a heartbeat via `IHostApplicationLifetime` / OTel meter; the existing default `health` endpoint should be unaffected (no-op required, just don't break it).
5. **AC5 — Tests** in `tests/Api.Tests/Outbox/`:
   - `OutboxRelayTests.cs` — uses Testcontainers Postgres + Testcontainers `Confluent.Kafka` (or the `confluentinc/cp-kafka` image). Insert an OutboxMessage row → start the relay (one tick) → assert PublishedAt set + a Kafka consumer reads the message back from the topic w/ correct key/payload.
   - `OutboxRelayIdempotencyTests.cs` — simulate a transient produce failure (point the relay at an unreachable broker first, then valid), verify retry + AttemptCount increments + eventual success.
6. **AC6 — F1 + F2 tests still pass** end-to-end.

## Scope
**In scope:**
- `apps/api/Hosted/OutboxRelay.cs` + DI wiring in `apps/api/Program.cs`
- `tests/Api.Tests/Outbox/` test class(es) + a small Kafka container fixture (mirror of `PostgresFixture` pattern)
- `appsettings.Development.json` defaults for the new `Outbox` section (optional)

**Out of scope:**
- Defining domain commands/events that GO into the outbox — F4 owns that
- A consumer service (`CaseIntakeConsumer`) — F5 owns that
- Multi-tenant routing / partition assignment — defer; for now, single partition per topic is fine
- Schema Registry integration — backlogged

## Manual test plan
1. `dotnet test tests/Api.Tests/Conduct.Api.Tests.csproj` — all F1+F2+F3 tests pass.
2. Start Aspire stack — relay logs poll lines at INFO; no errors at startup.
3. Manually `INSERT INTO outbox_messages` from psql; observe relay publishing to the kafka topic via Kafka UI (`http://localhost:<port>`).

## Self-review checklist
- [ ] All AC met.
- [ ] Tests fail before impl (red), pass after (green).
- [ ] No type-conditional logic on Topic / Key.
- [ ] Producer disposed cleanly on shutdown (`producer.Flush(TimeSpan.FromSeconds(5))`).
- [ ] Polling tx isolation correct: read pending rows, publish, mark published — race-safe under multiple relay replicas (use `FOR UPDATE SKIP LOCKED` to claim rows).
- [ ] `code-reviewer` agent run; findings addressed or backlogged.
- [ ] Commit message: `F3: Kafka outbox relay`.
