using Conduct.Domain.Authorization;
using Conduct.Domain.Cases.Intake;
using Conduct.Domain.Lobs;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Authorization;
using Conduct.Infrastructure.Cases.Intake;
using Conduct.Infrastructure.Identity;
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
            IConductAuthorization auth,
            ConductDbContext db,
            IConfiguration config,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            // F10: imperative permission check.
            // Authorization is body-derived (target LOB comes from the request), so the
            // per-endpoint policy attribute can't express it. We resolve the LOB id from
            // the request's `lobShortCode`, confirm the authenticated User has
            // `case.create` for that LOB (with parent-LOB inheritance), and 403 if not.
            //
            // 404 vs 403 on unknown LOB: we ALWAYS 403 here rather than leaking that the
            // LOB is unknown. IntakeService re-validates and returns the 404 path for
            // authenticated callers who DID pass the permission gate (i.e. only after we
            // confirm they had a permission to start with for some valid LOB).
            var userIdClaim = ctx.User.FindFirst(UserMirrorMiddleware.AppUserIdClaim)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return ProblemJson(ctx, 401, "user_unresolved",
                    "Authenticated principal did not resolve to a known app user.");
            }

            var lob = await db.Lobs.AsNoTracking()
                .Where(l => l.ShortCode == body.LobShortCode)
                .Select(l => new { l.Id })
                .FirstOrDefaultAsync(ct);
            if (lob is null || !await auth.HasPermissionAsync(userId, Permissions.CaseCreate, new AuthScope.Lob(lob.Id), ct))
            {
                return ProblemJson(ctx, 403, "permission_denied",
                    $"Caller is not permitted to create cases for LOB '{body.LobShortCode}'.");
            }

            var outcome = await service.SubmitAsync(body, ct);
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

    private static IResult ProblemJson(HttpContext ctx, int statusCode, string title, string detail)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/problem+json";
        var payload = JsonSerializer.Serialize(new
        {
            type = $"https://conduct.local/problems/{title.Replace('_', '-')}",
            title,
            status = statusCode,
            detail,
        });
        return Results.Content(payload, "application/problem+json", statusCode: statusCode);
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
