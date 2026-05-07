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
app.MapOpenApi();

// Resolve the active tenant for every request before any endpoint runs.
app.UseTenantContext();

app.MapGet("/api/_meta/echo", () => Results.Ok(new
{
    ok = true,
    service = "conduct.api",
    ts = DateTimeOffset.UtcNow
}));

app.MapIntakeEndpoints();

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
