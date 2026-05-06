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
        var (connStr, opts) = await CreateFreshDbInfraAsync();
        var db = new ConductDbContext(opts);
        await db.Database.EnsureCreatedAsync();
        return (db, connStr);
    }

    // Variant that runs the full EF migration history (vs EnsureCreatedAsync). Required for
    // any test that depends on raw-SQL constructs migrations apply (RLS policies, triggers)
    // — EnsureCreatedAsync only materialises the EF model, skipping migration bodies.
    //
    // Postgres superusers bypass RLS unconditionally, and Testcontainers' default `postgres`
    // role IS a superuser. To actually exercise RLS we provision a non-superuser role
    // `app_user`, grant it CRUD on every table in the public schema, and return a connection
    // string that connects as that role. Tests get airtight RLS enforcement; migrations have
    // already run as the original superuser owner so the role grants are sufficient.
    public async Task<(ConductDbContext Db, string ConnectionString)> CreateFreshMigratedDbAsync()
    {
        var (ownerConnStr, ownerOpts) = await CreateFreshDbInfraAsync();
        var owner = new ConductDbContext(ownerOpts);
        await owner.Database.MigrateAsync();
        await owner.DisposeAsync();

        // Create an unprivileged role + grant CRUD on every table in public. Migrations
        // already ran as superuser, so DDL is done; from here, we only ever connect as
        // this role, which IS subject to RLS (FORCE ROW LEVEL SECURITY catches it).
        // Roles are cluster-scoped in Postgres (not per-DB), so we mint a unique name per
        // test DB to avoid 42710 collisions across parallel CreateFreshMigratedDbAsync calls.
        var roleName = $"app_user_{Guid.NewGuid():N}";
        const string appPassword = "app_user_pw_for_tests_only";
        await using (var admin = new Npgsql.NpgsqlConnection(ownerConnStr))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"""
                CREATE ROLE "{roleName}" LOGIN PASSWORD '{appPassword}';
                GRANT USAGE ON SCHEMA public TO "{roleName}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO "{roleName}";
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO "{roleName}";
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var appConnStr = new Npgsql.NpgsqlConnectionStringBuilder(ownerConnStr)
        {
            Username = roleName,
            Password = appPassword,
        }.ToString();

        var appOpts = new DbContextOptionsBuilder<ConductDbContext>()
            .UseNpgsql(appConnStr, o => o.UseVector())
            .Options;

        var db = new ConductDbContext(appOpts);
        return (db, appConnStr);
    }

    // Shared CREATE DATABASE + DbContextOptions plumbing for the two CreateFresh* variants.
    private async Task<(string ConnectionString, DbContextOptions<ConductDbContext> Options)>
        CreateFreshDbInfraAsync()
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

        return (connStr, opts);
    }
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
