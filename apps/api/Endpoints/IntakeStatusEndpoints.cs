using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Api.Endpoints;

public sealed record IntakeStatusResponse(
    Guid ReceiptId,
    string Status,
    Guid? CaseId,
    string? CaseNumber,
    // Public-safe summary only — see project_audit_log.md / user-persona review.
    bool HasErrors,
    string? ErrorSummary);

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

            // Don't leak internal failure detail (e.g. "CaseType '...' not found at process
            // time") to anonymous callers. Surface a stable boolean + a generic summary
            // string. Privileged "audit-view" caller can fetch the raw ErrorsJson via a
            // future admin endpoint.
            var hasErrors = !string.IsNullOrEmpty(receipt.ErrorsJson);
            var errorSummary = hasErrors
                ? "Intake processing failed. Contact support with the receipt id."
                : null;

            return Results.Ok(new IntakeStatusResponse(
                receipt.Id,
                receipt.Status.ToString(),
                receipt.CaseId,
                receipt.CaseNumber,
                hasErrors,
                errorSummary));
        }).WithTags("Intake");

        return app;
    }
}
