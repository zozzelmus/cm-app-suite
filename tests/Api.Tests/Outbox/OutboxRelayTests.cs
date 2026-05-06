using AwesomeAssertions;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Outbox;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Conduct.Api.Tests.Outbox;

[Collection("postgres+kafka")]
public class OutboxRelayTests(PostgresAndKafkaFixture fx)
{
    [Fact]
    public async Task Relay_publishes_pending_outbox_row_to_kafka_then_marks_published()
    {
        await using var db = await fx.Postgres.CreateFreshDbAsync();
        var topic = $"events.test.{Guid.NewGuid():N}";

        var msg = new OutboxMessage
        {
            TenantId = Guid.NewGuid(),
            Topic = topic,
            Key = "case-42",
            PayloadJson = """{"hello":"world"}""",
        };
        db.Outbox.Add(msg);
        await db.SaveChangesAsync();

        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = fx.Kafka.BootstrapServers, EnableIdempotence = true, Acks = Acks.All }).Build();

        var relay = new OutboxRelay(
            db,
            producer,
            Options.Create(new OutboxOptions { BatchSize = 50, BusyDelay = TimeSpan.FromMilliseconds(100), IdleDelay = TimeSpan.FromMilliseconds(500) }),
            NullLogger<OutboxRelay>.Instance);

        // One tick = drain everything pending and exit
        await relay.TickOnceAsync(CancellationToken.None);

        var saved = await db.Outbox.SingleAsync(x => x.Id == msg.Id);
        saved.PublishedAt.Should().NotBeNull();
        saved.AttemptCount.Should().Be(1);
        saved.LastError.Should().BeNull();

        // Consume back from Kafka and assert the payload + key
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = fx.Kafka.BootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);
        var consumed = consumer.Consume(TimeSpan.FromSeconds(10));
        consumer.Close();

        consumed.Should().NotBeNull();
        consumed!.Message.Key.Should().Be("case-42");
        consumed.Message.Value.Should().Be("""{"hello":"world"}""");
        // Headers should carry the outbox id for idempotency on the consumer side
        consumed.Message.Headers.Should().Contain(h => h.Key == "outbox-id");
    }

    [Fact]
    public async Task Relay_increments_AttemptCount_and_records_LastError_on_produce_failure()
    {
        await using var db = await fx.Postgres.CreateFreshDbAsync();
        var topic = $"events.test.{Guid.NewGuid():N}";

        db.Outbox.Add(new OutboxMessage
        {
            TenantId = Guid.NewGuid(),
            Topic = topic,
            Key = "k",
            PayloadJson = """{"x":1}""",
        });
        await db.SaveChangesAsync();

        // Producer pointed at an unreachable broker — produce will time out
        using var brokenProducer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = "127.0.0.1:1",
            MessageTimeoutMs = 1500,
        }).Build();

        var relay = new OutboxRelay(
            db,
            brokenProducer,
            Options.Create(new OutboxOptions { BatchSize = 50, BusyDelay = TimeSpan.FromMilliseconds(100), IdleDelay = TimeSpan.FromMilliseconds(500) }),
            NullLogger<OutboxRelay>.Instance);

        await relay.TickOnceAsync(CancellationToken.None);

        var row = await db.Outbox.SingleAsync();
        row.PublishedAt.Should().BeNull();
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Relay_excludes_rows_at_or_above_MaxAttempts()
    {
        await using var db = await fx.Postgres.CreateFreshDbAsync();
        var topic = $"events.test.{Guid.NewGuid():N}";

        db.Outbox.Add(new OutboxMessage
        {
            TenantId = Guid.NewGuid(),
            Topic = topic,
            Key = "poison",
            PayloadJson = "{}",
            AttemptCount = 5,                  // already at MaxAttempts → quarantined
            LastError = "permanently broken",
        });
        await db.SaveChangesAsync();

        // Producer pointed at a working broker — should still skip the poison row entirely.
        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = fx.Kafka.BootstrapServers }).Build();

        var relay = new OutboxRelay(
            db,
            producer,
            Options.Create(new OutboxOptions { BatchSize = 50, MaxAttempts = 5, BusyDelay = TimeSpan.FromMilliseconds(100), IdleDelay = TimeSpan.FromMilliseconds(500) }),
            NullLogger<OutboxRelay>.Instance);

        var didWork = await relay.TickOnceAsync(CancellationToken.None);
        didWork.Should().BeFalse(); // poison row excluded → no work seen

        var row = await db.Outbox.SingleAsync();
        row.PublishedAt.Should().BeNull();
        row.AttemptCount.Should().Be(5); // unchanged, not retried
    }

    [Fact]
    public async Task Concurrent_relays_each_publish_each_row_exactly_once()
    {
        var (db1, connStr) = await fx.Postgres.CreateFreshDbWithConnStringAsync();
        await using var _ = db1; // dispose with `using var` semantics via outer scope
        var topic = $"events.test.{Guid.NewGuid():N}";

        // Insert 50 rows into db1's database — we'll connect a SECOND db context to the same
        // database and run two relays concurrently.
        for (int i = 0; i < 50; i++)
        {
            db1.Outbox.Add(new OutboxMessage
            {
                TenantId = Guid.NewGuid(),
                Topic = topic,
                Key = $"k-{i:D3}",
                PayloadJson = $$"""{"i":{{i}}}""",
            });
        }
        await db1.SaveChangesAsync();

        // Build a second DbContext bound to the SAME database (concurrency test point).
        var opts2 = new DbContextOptionsBuilder<ConductDbContext>()
            .UseNpgsql(connStr, o => o.UseVector())
            .Options;
        await using var db2 = new ConductDbContext(opts2);

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = fx.Kafka.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        }).Build();

        var relay1 = new OutboxRelay(db1, producer,
            Options.Create(new OutboxOptions { BatchSize = 25, MaxAttempts = 5, BusyDelay = TimeSpan.FromMilliseconds(50), IdleDelay = TimeSpan.FromMilliseconds(100) }),
            NullLogger<OutboxRelay>.Instance);
        var relay2 = new OutboxRelay(db2, producer,
            Options.Create(new OutboxOptions { BatchSize = 25, MaxAttempts = 5, BusyDelay = TimeSpan.FromMilliseconds(50), IdleDelay = TimeSpan.FromMilliseconds(100) }),
            NullLogger<OutboxRelay>.Instance);

        await Task.WhenAll(relay1.TickOnceAsync(CancellationToken.None),
                           relay2.TickOnceAsync(CancellationToken.None));
        // One more tick each in case 50 rows didn't fit in two batches of 25 due to FOR UPDATE SKIP LOCKED skipping
        await Task.WhenAll(relay1.TickOnceAsync(CancellationToken.None),
                           relay2.TickOnceAsync(CancellationToken.None));

        var rows = await db1.Outbox.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(50);
        rows.All(r => r.PublishedAt is not null).Should().BeTrue();
        rows.All(r => r.AttemptCount == 1).Should().BeTrue("FOR UPDATE SKIP LOCKED prevents double-claim → no row should have been attempted twice");

        // Drain the topic and assert exactly 50 distinct outbox-ids.
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = fx.Kafka.BootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);
        var seen = new HashSet<string>();
        for (int i = 0; i < 60 && seen.Count < 50; i++)
        {
            var c = consumer.Consume(TimeSpan.FromSeconds(2));
            if (c is null) continue;
            var idHeader = c.Message.Headers.GetLastBytes("outbox-id");
            seen.Add(System.Text.Encoding.UTF8.GetString(idHeader));
        }
        consumer.Close();

        seen.Count.Should().Be(50, "each outbox row should have been published exactly once");
    }

    [Fact]
    public async Task Relay_skips_already_published_rows()
    {
        await using var db = await fx.Postgres.CreateFreshDbAsync();
        var topic = $"events.test.{Guid.NewGuid():N}";

        var publishedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        db.Outbox.Add(new OutboxMessage
        {
            TenantId = Guid.NewGuid(),
            Topic = topic,
            Key = "already-done",
            PayloadJson = "{}",
            PublishedAt = publishedAt,
            AttemptCount = 1,
        });
        await db.SaveChangesAsync();

        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = fx.Kafka.BootstrapServers }).Build();

        var relay = new OutboxRelay(
            db,
            producer,
            Options.Create(new OutboxOptions { BatchSize = 50, BusyDelay = TimeSpan.FromMilliseconds(100), IdleDelay = TimeSpan.FromMilliseconds(500) }),
            NullLogger<OutboxRelay>.Instance);

        await relay.TickOnceAsync(CancellationToken.None);

        var row = await db.Outbox.SingleAsync();
        row.PublishedAt.Should().BeCloseTo(publishedAt, TimeSpan.FromSeconds(1)); // unchanged
        row.AttemptCount.Should().Be(1); // unchanged
    }
}
