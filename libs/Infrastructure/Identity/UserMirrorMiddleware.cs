using System.Security.Claims;
using System.Text.Json;
using Conduct.Domain.Identity;
using Conduct.Infrastructure.Multitenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Infrastructure.Identity;

// Just-in-time User mirror. Runs after TenantContextMiddleware (so the tenant scope is set
// for any DbContext access) and before any endpoint that needs to resolve the User PK from
// the principal's `sub` claim.
//
// Behaviour:
//   * For endpoints flagged `[AllowAnonymous]` — pass through.
//   * For an authenticated principal:
//       - Look up `User` by (TenantId, KeycloakSub).
//       - If the row exists and IsActive=false → 403 `user_deactivated`.
//       - If missing → INSERT a fresh row from claims; race-safe via
//         INSERT ... ON CONFLICT DO NOTHING on the (TenantId, KeycloakSub) unique index.
//       - Append the resulting `app_user_id` claim to the principal so endpoints that
//         need the PK don't re-query.
//   * For a request that lacks a `sub` claim — 401 (the JWT bearer pipeline shouldn't have
//     let this through; defensive).
public sealed class UserMirrorMiddleware(RequestDelegate next)
{
    public const string AppUserIdClaim = "app_user_id";

    public async Task InvokeAsync(HttpContext ctx, ConductDbContext db, ITenantContext tenant)
    {
        // Anonymous endpoints opt out — same single-source-of-truth as TenantContextMiddleware.
        if (ctx.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(ctx);
            return;
        }

        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            // Pipeline contract: this middleware runs BEFORE UseAuthorization, so unauth'd
            // requests to non-anonymous endpoints reach here and we just pass through —
            // UseAuthorization downstream will 401 via FallbackPolicy.
            await next(ctx);
            return;
        }

        var sub = ctx.User.FindFirst("sub")?.Value
               ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var tenantId = tenant.TenantId
            ?? throw new InvalidOperationException(
                "UserMirrorMiddleware ran without an ambient tenant — pipeline order regression.");

        var existing = await db.Users
            .AsNoTracking()
            .Where(u => u.KeycloakSub == sub) // RLS scopes by tenant
            .Select(u => new { u.Id, u.IsActive })
            .FirstOrDefaultAsync(ctx.RequestAborted);

        UserStub user;
        if (existing is not null)
        {
            user = new UserStub(existing.Id, existing.IsActive);
        }
        else
        {
            // First login — JIT-create. ON CONFLICT keeps us race-safe under burst.
            var newId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var username = ctx.User.FindFirst("preferred_username")?.Value ?? sub;
            var email = ctx.User.FindFirst("email")?.Value ?? string.Empty;
            var first = ctx.User.FindFirst("given_name")?.Value;
            var last = ctx.User.FindFirst("family_name")?.Value;

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Users" (
                    "Id", "TenantId", "KeycloakSub", "Username", "Email",
                    "FirstName", "LastName", "PartyId", "ExternalUserId",
                    "IsActive", "LastLoginAt", "CreatedAt", "UpdatedAt"
                ) VALUES (
                    {newId}, {tenantId}, {sub}, {username}, {email},
                    {first}, {last}, NULL, NULL,
                    TRUE, {now}, {now}, {now}
                )
                ON CONFLICT ("TenantId", "KeycloakSub") DO NOTHING
                """, ctx.RequestAborted);

            var settled = await db.Users
                .AsNoTracking()
                .Where(u => u.KeycloakSub == sub)
                .Select(u => new { u.Id, u.IsActive })
                .FirstAsync(ctx.RequestAborted); // exists now; either our insert or another worker's
            user = new UserStub(settled.Id, settled.IsActive);
        }

        if (!user.IsActive)
        {
            await Write403Async(ctx, "user_deactivated",
                "Authenticated user is marked inactive in the application directory.");
            return;
        }

        // Materialise the User PK as a claim so endpoints don't re-resolve.
        var identity = (ClaimsIdentity)ctx.User.Identity!;
        if (identity.FindFirst(AppUserIdClaim) is null)
        {
            identity.AddClaim(new Claim(AppUserIdClaim, user.Id.ToString()));
        }

        await next(ctx);
    }

    private static async Task Write403Async(HttpContext ctx, string title, string detail)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        ctx.Response.ContentType = "application/problem+json";
        var payload = JsonSerializer.Serialize(new
        {
            type = $"https://conduct.local/problems/{title.Replace('_', '-')}",
            title,
            status = 403,
            detail,
        });
        await ctx.Response.WriteAsync(payload);
    }

    private readonly record struct UserStub(Guid Id, bool IsActive);
}
