using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Conduct.Infrastructure.Multitenancy;

// Resolves the current tenant for a request from the authenticated principal's `tenant_id`
// claim and pushes it into the ambient ITenantContext before any DbContext is touched.
//
// Fails closed: if no claim is present or it can't be parsed as a Guid, returns 401 with a
// stable error code. The downstream pipeline never runs without a resolved tenant — so RLS
// policies (which deny all rows when `app.tenant_id` is unset) can never accidentally serve
// a request under no tenant.
//
// Pipeline placement: this middleware runs AFTER UseAuthorization, so endpoint routing has
// resolved by the time we look at metadata. Endpoints that opt into anonymous via
// `.AllowAnonymous()` (health probes, dev forwarders, OpenAPI) get a free pass — single
// source of truth for "this endpoint is public" lives on the endpoint itself, not in a
// duplicate path-prefix list maintained here.
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
