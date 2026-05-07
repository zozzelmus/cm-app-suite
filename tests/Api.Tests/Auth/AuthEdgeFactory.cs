using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Conduct.Api.Tests.Auth;

// WebApplicationFactory-backed integration host for testing the API's auth/tenant edge.
//
// What it changes vs. real prod startup:
//   1. ASPNETCORE_ENVIRONMENT=Development so the migrations block runs against the
//      throwaway Postgres testcontainer (the auth tests don't query seeded data, but
//      Program.cs needs the schema to exist to start cleanly).
//   2. Env vars provide:
//        ConnectionStrings__conductdb -> Postgres testcontainer
//        ConnectionStrings__kafka     -> dummy address (we strip the Kafka-bound hosted
//                                       services below, so producer/consumer factories
//                                       never connect).
//      Env vars are used (vs ConfigureAppConfiguration) because Aspire's
//      AddNpgsqlDbContext reads its connection string before the WebApplicationFactory's
//      ConfigureAppConfiguration delta is applied to the WebApplication's builder.
//   3. Removes hosted services so the test host doesn't open Kafka sockets in the background.
//   4. Replaces JwtBearer with TestAuthHandler — header-driven principal construction.
public sealed class AuthEdgeFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("conductauth")
        .Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        // TODO (test hygiene): env vars mutate process-wide state — ordering becomes load-
        // bearing if a parallel test class touches the same keys. Tried `UseSetting(...)`
        // and `ConfigureAppConfiguration(...)` first; both were applied AFTER Aspire's
        // AddNpgsqlDbContext resolves its connection string, so the host fails to build.
        // Env vars work because they're picked up by AddEnvironmentVariables() in the
        // default config layer, which runs before any DI factory. Acceptable since Api.Tests
        // doesn't parallelise the auth-edge fixture w/ another fixture that reads these keys.
        Environment.SetEnvironmentVariable("ConnectionStrings__conductdb", _pg.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__kafka", "localhost:9999");
        // Seed enabled so F10 permission tests can use the seeded demo user + Investigator
        // assignment on INV-APAC. F9 tests don't care; the extra ~150ms is fine.
        Environment.SetEnvironmentVariable("Seed__Enabled", "true");

        // Force eager host build so any startup error surfaces inside InitializeAsync rather
        // than the first test call.
        _ = Server;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _pg.DisposeAsync();

        // Reset env vars so a subsequent test class doesn't inherit a stopped container's
        // connection string.
        Environment.SetEnvironmentVariable("ConnectionStrings__conductdb", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__kafka", null);
        Environment.SetEnvironmentVariable("Seed__Enabled", null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureTestServices(services =>
        {
            // Strip Kafka-bound hosted services — they'd otherwise spin connection retry
            // loops in the background trying to reach the dummy broker. This also drops
            // Aspire-injected hosted services (telemetry, resource health) — fine for
            // testing the request pipeline.
            services.RemoveAll<IHostedService>();

            // Swap auth scheme: register the Test handler and make it the default everywhere.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                o.DefaultForbidScheme = TestAuthHandler.SchemeName;
                o.DefaultSignInScheme = TestAuthHandler.SchemeName;
                o.DefaultSignOutScheme = TestAuthHandler.SchemeName;
            });
        });
    }
}
