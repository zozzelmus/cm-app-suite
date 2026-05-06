using Conduct.Infrastructure;
using Conduct.Infrastructure.Outbox;
using Conduct.Infrastructure.Seed;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ConductDbContext>("conductdb");
builder.AddKafkaProducer<string, string>("kafka", settings =>
{
    settings.Config.Acks = Acks.All;
    settings.Config.EnableIdempotence = true;
    settings.Config.LingerMs = 5;
    settings.Config.CompressionType = CompressionType.Zstd;
});

builder.Services.AddOpenApi();
builder.Services.AddScoped<Seeder>();
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.AddScoped<OutboxRelay>();
builder.Services.AddHostedService<OutboxRelayHost>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

app.MapGet("/api/_meta/echo", () => Results.Ok(new
{
    ok = true,
    service = "conduct.api",
    ts = DateTimeOffset.UtcNow
}));

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
        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync();
    }
}

app.Run();
