using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Api.Endpoints;

public sealed record IntakeStatusResponse(
    Guid ReceiptId,
    string Status,
    Guid? CaseId,
    string? CaseNumber,
    string? Errors);

public static class IntakeStatusEndpoints
{
    public static IEndpointRouteBuilder MapIntakeStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cases/intake/{receiptId:guid}", async (
            Guid receiptId,
            ConductDbContext db,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            // RLS scopes the query to the current tenant; cross-tenant lookup returns null
            // and the handler responds 404 (NOT 403) so existence isn't leaked.
            var receipt = await db.CaseIntakes.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == receiptId, ct);

            if (receipt is null)
            {
                return Results.NotFound();
            }

            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new IntakeStatusResponse(
                receipt.Id,
                receipt.Status.ToString(),
                receipt.CaseId,
                receipt.CaseNumber,
                receipt.ErrorsJson));
        }).WithTags("Intake");

        return app;
    }
}
