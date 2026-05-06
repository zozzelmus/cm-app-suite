using Testcontainers.Kafka;

namespace Conduct.Api.Tests.Outbox;

// Spins up a single Kafka container (KRaft, no Zookeeper) shared across the test class.
// Each test creates a unique topic name to keep messages isolated.
public sealed class KafkaFixture : IAsyncLifetime
{
    public KafkaContainer Container { get; }

    public KafkaFixture()
    {
        Container = new KafkaBuilder("confluentinc/cp-kafka:7.6.1").Build();
    }

    public string BootstrapServers => Container.GetBootstrapAddress();

    public async Task InitializeAsync() => await Container.StartAsync();

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

[CollectionDefinition("kafka")]
public class KafkaCollection : ICollectionFixture<KafkaFixture> { }

// Combined Postgres + Kafka collection for the OutboxRelay tests.
public sealed class PostgresAndKafkaFixture : IAsyncLifetime
{
    public Seed.PostgresFixture Postgres { get; } = new();
    public KafkaFixture Kafka { get; } = new();

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        await Kafka.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Kafka.DisposeAsync();
    }
}

[CollectionDefinition("postgres+kafka")]
public class PostgresAndKafkaCollection : ICollectionFixture<PostgresAndKafkaFixture> { }
