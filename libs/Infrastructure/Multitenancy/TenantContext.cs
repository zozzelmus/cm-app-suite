namespace Conduct.Infrastructure.Multitenancy;

// AsyncLocal-backed default impl — registered as a singleton; per-scope state flows via the
// async execution context (works across awaits, survives Task.Run, propagates to EF
// interceptor calls). The interceptor itself must be a singleton (Aspire pools DbContexts and
// their interceptor list), so AsyncLocal is the only way to surface per-request tenant state
// to a singleton-lifetime consumer.
public sealed class TenantContext : ITenantContext
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public Guid? TenantId => Current.Value;

    public IDisposable BeginScope(Guid tenantId)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        return new Restorer(previous);
    }

    private sealed class Restorer(Guid? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            Current.Value = previous;
            _disposed = true;
        }
    }
}
