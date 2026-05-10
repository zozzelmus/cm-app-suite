using Conduct.Api.Application.Routing;
using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Cases.Intake;
using Conduct.Infrastructure.Multitenancy;
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
            ICaseRoutingService routing,
            ITenantContext tenant,
            ConductDbContext db,
            IConfiguration config,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            // Resolve the initial LOB before handing off to the intake pipeline. If the
            // client supplied one we still ask the routing service — the service decides
            // whether to honor or override. Today it always returns SUI; future versions
            // will respect explicit client values for admin-tool paths.
            if (tenant.TenantId is null)
            {
                return Results.Json(
                    new IntakeErrorResponse("tenant_unknown", "No tenant context resolved for request"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            var decision = await routing.ResolveAsync(
                new RoutingContext(tenant.TenantId.Value, body.CaseTypeKey, body), ct);
            var routed = body with { LobShortCode = decision.LobShortCode };

            var outcome = await service.SubmitAsync(routed, ct);
            if (outcome.IsAccepted && outcome.ReceiptId is { } receiptId)
            {
                // Optional sync-fallback path: the canonical async pipeline routes via Kafka
                // (outbox → relay → consumer). When `Intake:SyncProcess=true` we ALSO invoke
                // the processor inline so the receipt finishes in-request — useful for the
                // dev demo while the Aspire-Kafka port-drift issue is open.
                //
                // SAFETY: default to true ONLY in Development. Architect-review flagged the
                // global `true` default as a "ticking trap" — in Production it would double-
                // process every intake (once inline + once via the consumer once Kafka
                // recovers), defeating async load-shedding. Idempotency saves correctness
                // but ops cost is real. Set `Intake:SyncProcess=true` explicitly per env.
                var defaultSyncProcess = ctx.RequestServices
                    .GetRequiredService<IHostEnvironment>()
                    .IsDevelopment();
                if (config.GetValue("Intake:SyncProcess", defaultSyncProcess))
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
