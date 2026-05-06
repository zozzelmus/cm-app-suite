using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure.Cases.Intake;

namespace Conduct.Api.Endpoints;

public static class IntakeEndpoints
{
    public static IEndpointRouteBuilder MapIntakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cases").WithTags("Intake");

        // Bind to "" not "/" — the latter requires a trailing slash on the request URL,
        // and POST + 308 redirect is brittle for some clients.
        group.MapPost("", async (IntakeRequest body, IntakeService service, HttpContext ctx, CancellationToken ct) =>
        {
            var outcome = await service.SubmitAsync(body, ct);
            if (outcome.IsAccepted && outcome.ReceiptId is { } receiptId)
            {
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
}
