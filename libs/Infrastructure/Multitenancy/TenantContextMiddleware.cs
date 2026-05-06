using Conduct.Infrastructure.Seed;
using Microsoft.AspNetCore.Http;

namespace Conduct.Infrastructure.Multitenancy;

// Resolves the current tenant for a request and pushes it into the ambient ITenantContext
// before any DbContext is touched. Register early in the pipeline (before MVC/Endpoints).
//
// POC: every request resolves to SeedConstants.DemoTenantId. Real impl will read a Keycloak
// claim (e.g. `tenant_id`) and validate the user is allowed to act in that tenant.
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenant)
    {
        // POC resolution. Replace with claim-based lookup post-MVP.
        using var _ = tenant.BeginScope(SeedConstants.DemoTenantId);
        await next(ctx);
    }
}
