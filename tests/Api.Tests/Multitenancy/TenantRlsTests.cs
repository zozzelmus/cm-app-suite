using AwesomeAssertions;
using Conduct.Api.Tests.Seed;
using Conduct.Domain.Lobs;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Conduct.Api.Tests.Multitenancy;

// Proves the airtight tenant boundary: even raw SQL can't cross tenants once RLS is on.
//
// Each test runs in a freshly migrated DB (RLS policies applied via EnableTenantRls) and
// switches Postgres' app.tenant_id GUC manually on a raw connection — bypassing any C#-side
// tenant context — so we test the database-side enforcement directly.
[Collection("postgres")]
public class TenantRlsTests(PostgresFixture pg)
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SelectScopedToTenant_ReturnsOnlyOwnTenantRows()
    {
        var (_, connStr) = await pg.CreateFreshMigratedDbAsync();

        var aLob = await InsertLobAsync(connStr, TenantA, "A-LOB", "Tenant A LOB");
        var bLob = await InsertLobAsync(connStr, TenantB, "B-LOB", "Tenant B LOB");

        // Read as Tenant A — sees only A's row
        var aRows = await ReadAllLobsAsync(connStr, TenantA);
        aRows.Should().ContainSingle(x => x.Id == aLob).And.NotContain(x => x.Id == bLob);

        // Read as Tenant B — sees only B's row
        var bRows = await ReadAllLobsAsync(connStr, TenantB);
        bRows.Should().ContainSingle(x => x.Id == bLob).And.NotContain(x => x.Id == aLob);
    }

    [Fact]
    public async Task ExplicitCrossTenantPredicate_StillBlockedByRls()
    {
        var (_, connStr) = await pg.CreateFreshMigratedDbAsync();

        var aLob = await InsertLobAsync(connStr, TenantA, "A-LOB", "Tenant A LOB");
        _ = await InsertLobAsync(connStr, TenantB, "B-LOB", "Tenant B LOB");

        // Connect as Tenant A but try to SELECT B's row by predicate — RLS should still
        // filter it out (USING runs even when the user's WHERE matches).
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = $"SET app.tenant_id = '{TenantA:D}'";
            await setCmd.ExecuteNonQueryAsync();
        }

        await using var sel = conn.CreateCommand();
        sel.CommandText = $@"SELECT COUNT(*) FROM ""Lobs"" WHERE ""TenantId"" = '{TenantB:D}'";
        var count = (long)(await sel.ExecuteScalarAsync())!;
        count.Should().Be(0, "RLS USING clause filters the row before the WHERE predicate sees it");

        // Sanity: Tenant A can still see its own
        await using var ownSel = conn.CreateCommand();
        ownSel.CommandText = $@"SELECT COUNT(*) FROM ""Lobs"" WHERE ""Id"" = '{aLob:D}'";
        ((long)(await ownSel.ExecuteScalarAsync())!).Should().Be(1);
    }

    [Fact]
    public async Task InsertWithMismatchedTenantId_RejectedByWithCheckPolicy()
    {
        var (_, connStr) = await pg.CreateFreshMigratedDbAsync();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = $"SET app.tenant_id = '{TenantA:D}'";
            await setCmd.ExecuteNonQueryAsync();
        }

        // Try to insert a row claiming Tenant B's id while connected as Tenant A.
        // WITH CHECK clause must reject.
        var act = async () =>
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = $@"
                INSERT INTO ""Lobs"" (""Id"", ""TenantId"", ""Name"", ""ShortCode"",
                                      ""ApprovalQuorum"", ""QuorumSpecificUserIds"",
                                      ""CreatedAt"", ""UpdatedAt"")
                VALUES ('{Guid.NewGuid():D}', '{TenantB:D}', 'cross-tenant insert', 'XX',
                        {(int)ApprovalQuorum.AnyOneManager}, ARRAY[]::uuid[], now(), now())";
            await ins.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == "42501"); // insufficient_privilege — RLS WITH CHECK violation
    }

    [Fact]
    public async Task UnsetTenant_FailsClosed_ReturnsZeroRowsOnTenantedTable()
    {
        var (_, connStr) = await pg.CreateFreshMigratedDbAsync();

        _ = await InsertLobAsync(connStr, TenantA, "A-LOB", "Tenant A LOB");

        // Open a connection without setting app.tenant_id. The RLS policy uses
        // current_setting('app.tenant_id', true) which returns NULL; the predicate
        // `TenantId = NULL::uuid` evaluates to UNKNOWN and the row is excluded.
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        // Defensive: ensure no leftover GUC bleed-through from pooled session
        await using (var reset = conn.CreateCommand())
        {
            reset.CommandText = "RESET app.tenant_id";
            await reset.ExecuteNonQueryAsync();
        }

        await using var sel = conn.CreateCommand();
        sel.CommandText = @"SELECT COUNT(*) FROM ""Lobs""";
        var count = (long)(await sel.ExecuteScalarAsync())!;
        count.Should().Be(0, "no app.tenant_id set => RLS denies all rows (fail-closed)");
    }

    [Fact]
    public async Task EfQueryThroughInterceptor_AlsoScopesByAmbientTenantContext()
    {
        // Higher-level integration: prove the TenantConnectionInterceptor wired into a
        // ConductDbContext yields the same isolation as the raw-SQL path above.
        var (_, connStr) = await pg.CreateFreshMigratedDbAsync();
        await InsertLobAsync(connStr, TenantA, "A-LOB", "Tenant A LOB");
        await InsertLobAsync(connStr, TenantB, "B-LOB", "Tenant B LOB");

        var tenant = new TenantContext();
        var interceptor = new TenantConnectionInterceptor(tenant);

        var opts = new DbContextOptionsBuilder<ConductDbContext>()
            .UseNpgsql(connStr, o => o.UseVector())
            .AddInterceptors(interceptor)
            .Options;

        await using (var dbA = new ConductDbContext(opts))
        using (var _ = tenant.BeginScope(TenantA))
        {
            var lobs = await dbA.Lobs.AsNoTracking().ToListAsync();
            lobs.Should().HaveCount(1);
            lobs.Single().ShortCode.Should().Be("A-LOB");
        }

        await using (var dbB = new ConductDbContext(opts))
        using (var _ = tenant.BeginScope(TenantB))
        {
            var lobs = await dbB.Lobs.AsNoTracking().ToListAsync();
            lobs.Should().HaveCount(1);
            lobs.Single().ShortCode.Should().Be("B-LOB");
        }
    }

    // ────────── helpers ──────────

    // Insert a Lob using a raw connection with app.tenant_id set — bypasses any C#-side
    // context so the test is exercising the database boundary directly.
    private static async Task<Guid> InsertLobAsync(string connStr, Guid tenantId, string code, string name)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = $"SET app.tenant_id = '{tenantId:D}'";
            await setCmd.ExecuteNonQueryAsync();
        }
        var id = Guid.NewGuid();
        await using var ins = conn.CreateCommand();
        ins.CommandText = $@"
            INSERT INTO ""Lobs"" (""Id"", ""TenantId"", ""Name"", ""ShortCode"",
                                  ""ApprovalQuorum"", ""QuorumSpecificUserIds"",
                                  ""CreatedAt"", ""UpdatedAt"")
            VALUES ('{id:D}', '{tenantId:D}', '{name.Replace("'", "''")}', '{code}',
                    {(int)ApprovalQuorum.AnyOneManager}, ARRAY[]::uuid[], now(), now())";
        await ins.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<List<(Guid Id, Guid TenantId, string ShortCode)>>
        ReadAllLobsAsync(string connStr, Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = $"SET app.tenant_id = '{tenantId:D}'";
            await setCmd.ExecuteNonQueryAsync();
        }
        await using var sel = conn.CreateCommand();
        sel.CommandText = @"SELECT ""Id"", ""TenantId"", ""ShortCode"" FROM ""Lobs""";
        var rows = new List<(Guid, Guid, string)>();
        await using var rdr = await sel.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            rows.Add((rdr.GetGuid(0), rdr.GetGuid(1), rdr.GetString(2)));
        }
        return rows;
    }
}
