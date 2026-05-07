using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Cases.Intake;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Conduct.Api.Endpoints;

public static class IntakeEndpoints
{
    public static IEndpointRouteBuilder MapIntakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cases").WithTags("Intake");

        // Bind to "" not "/" — the latter requires a trailing slash on the request URL,
        // and POST + 308 redirect is brittle for some clients.
        group.MapPost("", async (
            IntakeRequest body,
            IntakeService service,
            IntakeProcessor processor,
            ConductDbContext db,
            IConfiguration config,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var outcome = await service.SubmitAsync(body, ct);
            if (outcome.IsAccepted && outcome.ReceiptId is { } receiptId)
            {
                // Optional sync-fallback path: the canonical async pipeline routes via Kafka
                // (outbox → relay → consumer). When `Intake:SyncProcess=true` (default true in
                // Dev environment) we ALSO invoke the processor inline so the receipt finishes
                // in-request and the demo flow doesn't depend on Kafka availability. The
                // outbox row is still written so the prod async pipeline stays intact.
                if (config.GetValue("Intake:SyncProcess", true))
                {
                    await TryProcessInlineAsync(processor, db, receiptId, ct);
                }

                var statusUrl = $"/api/cases/intake/{receiptId}";
                ctx.Response.Headers.Append("Location", statusUrl);
                return Results.Accepted(statusUrl, new IntakeAcceptedResponse(receiptId, statusUrl));
            }

            var error = outcome.Error!;
            var body_ = new IntakeErrorResponse(error.Code, error.Message, error.FieldErrors);
            return error.Kind switch
            {
                IntakeErrorKind.CaseTypeNotFound => Results.NotFound(body_),
                IntakeErrorKind.LobNotFound => Results.NotFound(body_),
                IntakeErrorKind.ValidationFailed => Results.BadRequest(body_),
                // Tenant context unresolved is a request-level auth gap, not an internal bug.
                IntakeErrorKind.TenantUnknown => Results.Json(body_, statusCode: StatusCodes.Status401Unauthorized),
                _ => Results.Problem("Unhandled intake error", statusCode: 500),
            };
        });

        return app;
    }

    // Reads the just-written outbox row and invokes the processor with the deserialised
    // CreateCaseCommand. Failure here is swallowed — the receipt stays Queued and the
    // (eventually-recovering) Kafka consumer will pick it up later.
    private static async Task TryProcessInlineAsync(
        IntakeProcessor processor, ConductDbContext db, Guid receiptId, CancellationToken ct)
    {
        try
        {
            var pending = await db.Outbox.AsNoTracking()
                .Where(o => o.Key == receiptId.ToString())
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (pending is null) return;

            var command = JsonSerializer.Deserialize<Domain.Cases.Intake.CreateCaseCommand>(
                pending.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (command is null) return;

            await processor.ProcessAsync(command, ct);
        }
        catch
        {
            // Intentional swallow — the async pipeline retries via Kafka redelivery once
            // brokers recover. Synchronous fallback is a nice-to-have, not the source of truth.
        }
    }
}
