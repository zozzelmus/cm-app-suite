using Conduct.Infrastructure;
using Conduct.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ConductDbContext>("conductdb");

builder.Services.AddOpenApi();
builder.Services.AddScoped<Seeder>();

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
