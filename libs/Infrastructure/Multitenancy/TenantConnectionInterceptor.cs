using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Conduct.Infrastructure.Multitenancy;

// On every connection-open, issue `SET app.tenant_id = '<uuid>'` so RLS policies on tenanted
// tables filter all subsequent SQL on this connection. Runs even for raw FromSqlInterpolated
// paths (interceptor lives below LINQ).
//
// Lifetime — registered as a SINGLETON. Aspire's AddNpgsqlDbContext uses AddDbContextPool by
// default; pooled DbContexts capture their interceptor list at pool-fill time, so a scoped
// interceptor would leak the *first* tenant across requests. ITenantContext is also a
// singleton internally backed by AsyncLocal — per-request tenant state propagates via the
// async execution context, not via DI scope.
//
// SET vs SET LOCAL — chose SET (session-scoped) over SET LOCAL (transaction-scoped):
//   * Most reads run outside an explicit transaction; SET LOCAL there errors with
//     "SET LOCAL can only be used in transaction blocks".
//   * Npgsql connection pooling DOES preserve session state across borrowers, so we MUST
//     re-issue the SET on every open. ConnectionOpenedAsync fires every time — pooled or
//     fresh — so re-setting on each open is the correct contract.
//   * If no tenant is set on the ambient context (background work, anonymous probe), we
//     issue `RESET app.tenant_id` so any leftover value from a previous borrower can't bleed
//     through. RLS policies then deny all rows on tenanted tables — fail-closed.
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ResolveSetCommand();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = ResolveSetCommand();
        cmd.ExecuteNonQuery();
    }

    private string ResolveSetCommand()
    {
        if (tenant.TenantId is { } id)
        {
            // Parameterised values can't be used with SET; format the literal ourselves.
            // GUID `ToString("D")` is safe (hex + dashes only) — no SQL-injection surface.
            return $"SET app.tenant_id = '{id:D}'";
        }
        return "RESET app.tenant_id";
    }
}
