using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace Conduct.Infrastructure.Outbox;

// Testable unit. One instance per scope; takes a ConductDbContext + producer.
// `TickOnceAsync` claims a batch of pending rows via FOR UPDATE SKIP LOCKED (multi-replica
// safe), publishes each to Kafka, marks PublishedAt, and commits in a single transaction.
// Returns true if it found work. The wrapping BackgroundService (`OutboxRelayHost`) loops
// over this with backoff; tests call `TickOnceAsync` directly.
public sealed class OutboxRelay(
    ConductDbContext db,
    IProducer<string, string> producer,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelay> logger)
{
    private readonly OutboxOptions _opts = options.Value;

    public async Task<bool> TickOnceAsync(CancellationToken ct)
    {
        // Aspire's Npgsql integration enables NpgsqlRetryingExecutionStrategy by default,
        // which forbids user-initiated transactions outside the strategy's ExecuteAsync.
        // Wrap so both retry-enabled (Aspire) and retry-disabled (tests) DbContexts work.
        bool didWork = false;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async cancellationToken =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            // FOR UPDATE SKIP LOCKED: each row claimed by exactly one tick across all replicas.
            // EF tracks the returned entities so the in-loop mutations land in SaveChangesAsync.
            // EF table/column names follow PascalCase (DbSet name → "Outbox", properties as-is).
            var pending = await db.Outbox
                .FromSqlInterpolated($"""
                    SELECT * FROM "Outbox"
                    WHERE "PublishedAt" IS NULL
                      AND "AttemptCount" < {_opts.MaxAttempts}
                    ORDER BY "CreatedAt"
                    LIMIT {_opts.BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
            {
                await tx.CommitAsync(cancellationToken);
                didWork = false;
                return;
            }

            foreach (var row in pending)
            {
                row.AttemptCount++;
                try
                {
                    var msg = new Message<string, string>
                    {
                        Key = row.Key ?? row.Id.ToString("N"),
                        Value = row.PayloadJson,
                        Headers = new Headers
                        {
                            new Header("outbox-id",       Encoding.UTF8.GetBytes(row.Id.ToString())),
                            new Header("tenant-id",       Encoding.UTF8.GetBytes(row.TenantId.ToString())),
                            new Header("created-at",      Encoding.UTF8.GetBytes(row.CreatedAt.UtcDateTime.ToString("O"))),
                            new Header("content-type",    Encoding.UTF8.GetBytes("application/json")),
                            new Header("schema-version",  Encoding.UTF8.GetBytes("1")),
                        },
                    };

                    var dr = await producer.ProduceAsync(row.Topic, msg, cancellationToken);
                    logger.LogInformation(
                        "Outbox→Kafka topic={Topic} partition={Partition} offset={Offset} id={Id}",
                        dr.Topic, dr.Partition.Value, dr.Offset.Value, row.Id);

                    row.PublishedAt = DateTimeOffset.UtcNow;
                    row.LastError = null;
                }
                catch (Exception ex)
                {
                    row.LastError = ex.Message;
                    if (row.AttemptCount >= _opts.MaxAttempts)
                    {
                        logger.LogError(ex,
                            "Outbox row {Id} exceeded MaxAttempts={Max}; quarantined (LastError set, no further publish attempts)",
                            row.Id, _opts.MaxAttempts);
                    }
                    else
                    {
                        logger.LogWarning(ex,
                            "Outbox publish failed topic={Topic} id={Id} attempt={Attempt}/{Max}",
                            row.Topic, row.Id, row.AttemptCount, _opts.MaxAttempts);
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            didWork = true;
        }, ct);

        return didWork;
    }
}
