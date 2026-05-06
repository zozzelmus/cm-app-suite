using Conduct.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ConductDbContext>("conductdb");

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

app.MapGet("/api/_meta/echo", () => Results.Ok(new
{
    ok = true,
    service = "conduct.api",
    ts = DateTimeOffset.UtcNow
}));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ConductDbContext>();
    if (app.Environment.IsDevelopment())
    {
        await db.Database.EnsureCreatedAsync();
    }
}

app.Run();
