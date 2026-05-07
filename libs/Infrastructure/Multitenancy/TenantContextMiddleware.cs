using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Conduct.Infrastructure.Multitenancy;

// Resolves the current tenant for a request from the authenticated principal's `tenant_id`
// claim and pushes it into the ambient ITenantContext before any DbContext is touched.
//
// Pipeline contract (as of F10):
//   UseAuthentication → UseTenantContext → UserMirrorMiddleware → UseAuthorization → endpoint
//
// We run BEFORE UseAuthorization so that ITenantContext is set before the JIT user mirror
// (and its DbContext access) executes. Two consequences:
//   * AllowAnonymous endpoints pass through here (single source of truth via metadata).
//   * Unauthenticated requests to auth-required endpoints also pass through — UseAuthorization
//     downstream issues the 401 via FallbackPolicy. This middleware only fail-closes when
//     the user IS authenticated but the JWT lacks a usable `tenant_id` claim, which is the
//     actual concern this middleware speaks to.
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string TenantClaimType = "tenant_id";

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenant)
    {
        if (ctx.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(ctx);
            return;
        }

        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            // Not our concern; UseAuthorization's FallbackPolicy issues the standard 401.
            await next(ctx);
            return;
        }

        var claim = ctx.User.FindFirst(TenantClaimType)?.Value;
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var tenantId))
        {
            await Write401Async(ctx);
            return;
        }

        using var _ = tenant.BeginScope(tenantId);
        await next(ctx);
    }

    private static async Task Write401Async(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.ContentType = "application/problem+json";
        var payload = JsonSerializer.Serialize(new
        {
            type = "https://conduct.local/problems/tenant-unknown",
            title = "tenant_unknown",
            status = 401,
            detail = "Authenticated principal is missing a usable tenant_id claim.",
        });
        await ctx.Response.WriteAsync(payload);
    }
}
