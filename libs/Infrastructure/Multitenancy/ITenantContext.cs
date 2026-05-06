namespace Conduct.Infrastructure.Multitenancy;

// Per-scope ambient resolver for the active tenant. Every code path that opens a DbContext
// connection consults this via TenantConnectionInterceptor to set Postgres' app.tenant_id GUC
// before any tenanted SQL runs.
//
// AsyncLocal-backed under the hood (see TenantContext) — ambient propagation lets the
// singleton interceptor read the tenant set by the per-request middleware (or by background
// jobs / the seeder via `using var _ = tenant.BeginScope(id);`).
//
// POC: TenantContextMiddleware always assigns SeedConstants.DemoTenantId. Future
// implementations (Keycloak claim, header-based admin override, etc.) swap the assignment
// without touching the interceptor.
public interface ITenantContext
{
    // The tenant for the current logical scope, or null if not set (background work,
    // unauthenticated paths). Interceptor MUST treat null as "do not issue SET" — RLS will
    // then deny all rows on tenanted tables, which is the correct fail-closed behaviour.
    Guid? TenantId { get; }

    // Set the tenant for the current logical scope. Disposing the returned IDisposable
    // restores the previous value (lets background jobs / seeders set + clear without
    // leaking into ambient context of unrelated work).
    IDisposable BeginScope(Guid tenantId);
}
