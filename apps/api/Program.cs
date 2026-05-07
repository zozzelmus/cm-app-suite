using Conduct.Api.Auth;
using Conduct.Api.Endpoints;
using Conduct.Api.Hosted;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Cases.Intake;
using Conduct.Infrastructure.Multitenancy;
using Conduct.Infrastructure.Outbox;
using Conduct.Infrastructure.Seed;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Tenant context (singletons — AsyncLocal-backed so per-request state propagates without
// a DI scope, which Aspire's pooled DbContext can't carry). Must register BEFORE
// AddNpgsqlDbContext so the interceptor instance is available to the options callback.
var (_, tenantInterceptor) = builder.Services.AddTenantContext();

builder.AddNpgsqlDbContext<ConductDbContext>(
    "conductdb",
    configureDbContextOptions: opts => opts.AddInterceptors(tenantInterceptor));

builder.AddKafkaProducer<string, string>("kafka", settings =>
{
    settings.Config.Acks = Acks.All;
    settings.Config.EnableIdempotence = true;
    settings.Config.LingerMs = 5;
    settings.Config.CompressionType = CompressionType.Zstd;
});

// Consumer: manual offset commit (we commit only after IntakeProcessor finalises the case).
builder.AddKafkaConsumer<string, string>("kafka", settings =>
{
    settings.Config.GroupId = "conduct.api.intake";
    settings.Config.AutoOffsetReset = AutoOffsetReset.Earliest;
    settings.Config.EnableAutoCommit = false;
    settings.Config.SessionTimeoutMs = 30_000;
});

// JWT bearer at the edge + FallbackPolicy = require authenticated user. See AuthSetup.cs.
builder.Services.AddConductAuth(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddScoped<Seeder>();
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.AddScoped<OutboxRelay>();
builder.Services.AddHostedService<OutboxRelayHost>();
builder.Services.AddScoped<IntakeService>();
builder.Services.AddScoped<IntakeProcessor>();
builder.Services.AddScoped<CaseAllocator>();
builder.Services.AddHostedService<CaseIntakeConsumerHost>();

var app = builder.Build();

app.MapDefaultEndpoints();
// OpenAPI/Swagger metadata isn't user data — let it be reachable without auth in dev so the
// /openapi.json link works. Production drops MapOpenApi entirely (or guards it).
app.MapOpenApi().AllowAnonymous();

// Pipeline order:
//   1. UseAuthentication — populates HttpContext.User from the bearer token.
//   2. UseAuthorization  — enforces FallbackPolicy + per-endpoint policies; 401/403 short-circuits.
//   3. UseTenantContext  — reads `tenant_id` claim from the (now-authenticated) principal,
//                          starts the ambient scope, fail-closes 401 if absent.
//
// TenantContext sits BETWEEN authorization and endpoint dispatch on purpose: by then the
// authz layer has already vetted that the request is allowed to be here, so 401 here is
// purely about a malformed token (missing tenant_id) rather than a missing session.
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantContext();

app.MapGet("/api/_meta/echo", () => Results.Ok(new
{
    ok = true,
    service = "conduct.api",
    ts = DateTimeOffset.UtcNow
}));

app.MapIntakeEndpoints();
app.MapIntakeStatusEndpoints();

// Dev-only: apply migrations and run idempotent seed on startup.
// Production migrations run via `azd hook predeploy` or a one-shot job; seeding production
// tenants is a separate admin workflow, not a startup side-effect.
// `Seed:Enabled` defaults true in development but allows pointing devs at a shared dev DB
// without triggering seed every restart.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ConductDbContext>();
    await db.Database.MigrateAsync();
    if (app.Configuration.GetValue("Seed:Enabled", true))
    {
        // Seeder inserts rows under RLS — set the tenant for the seed scope so the
        // interceptor issues `SET app.tenant_id` on the seed connection.
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.BeginScope(SeedConstants.DemoTenantId);
        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync();
    }
}

app.Run();

// Expose the implicit Program class to test projects so WebApplicationFactory<Program> works.
public partial class Program;
