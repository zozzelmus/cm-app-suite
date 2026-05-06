using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Conduct.Infrastructure.Multitenancy;

public static class MultitenancyServiceCollectionExtensions
{
    // Both are singletons — see TenantConnectionInterceptor.cs for the rationale (Aspire's
    // pooled DbContext captures interceptors at pool-fill time, so per-request state must
    // flow via AsyncLocal, not DI scope).
    //
    // Returns the interceptor instance so the caller can pass it into the DbContext options
    // callback (Aspire's AddNpgsqlDbContext lambda has no IServiceProvider parameter, so we
    // need the actual instance at registration time).
    public static (IServiceCollection Services, TenantConnectionInterceptor Interceptor)
        AddTenantContext(this IServiceCollection services)
    {
        var tenant = new TenantContext();
        var interceptor = new TenantConnectionInterceptor(tenant);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton(interceptor);
        return (services, interceptor);
    }

    // Pipeline registration. Place EARLY (before MVC/Endpoints) so every DbContext access
    // already has a resolved tenant.
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
        => app.UseMiddleware<TenantContextMiddleware>();
}
