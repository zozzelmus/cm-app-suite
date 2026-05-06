using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Conduct.Infrastructure;

namespace Conduct.Api.Tests.Seed;

// Spins up a single Postgres container shared across the seed test class.
// Each test runs in a fresh database (CREATE DATABASE per test) for isolation.
public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; }

    public PostgresFixture()
    {
        Container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("conducttest")
            .Build();
    }

    public async Task InitializeAsync() => await Container.StartAsync();

    public async Task DisposeAsync() => await Container.DisposeAsync();

    // Build a DbContext bound to a fresh database name. Caller is responsible for disposing.
    public async Task<ConductDbContext> CreateFreshDbAsync()
    {
        var (db, _) = await CreateFreshDbWithConnStringAsync();
        return db;
    }

    // Variant that also returns the full connection string (incl. credentials) so a second
    // DbContext can be wired against the same database — used for concurrency tests.
    public async Task<(ConductDbContext Db, string ConnectionString)> CreateFreshDbWithConnStringAsync()
    {
        var dbName = $"db_{Guid.NewGuid():N}";
        await using (var admin = new Npgsql.NpgsqlConnection(Container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        var connStr = new Npgsql.NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        {
            Database = dbName
        }.ToString();

        var opts = new DbContextOptionsBuilder<ConductDbContext>()
            .UseNpgsql(connStr, o => o.UseVector())
            .Options;

        var db = new ConductDbContext(opts);
        await db.Database.EnsureCreatedAsync();
        return (db, connStr);
    }
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
