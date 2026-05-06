using AwesomeAssertions;
using Conduct.Domain.Cases.Intake;
using Npgsql;

namespace Conduct.Api.Tests.Intake;

// Per project_data_access.md: every tenanted entity must have an isolation test that proves
// Tenant A cannot see Tenant B's rows even with raw SQL. Verifies the RLS policy added in
// EnableRlsForCaseIntakes is actually enforced end-to-end. Mirrors the raw-connection pattern
// used in TenantRlsTests so connection-pool GUC bleed-through can't false-green the test.
[Collection("postgres")]
public class CaseIntakeRlsTests(Conduct.Api.Tests.Seed.PostgresFixture pg)
{
    [Fact]
    public async Task TenantA_cannot_see_TenantB_intake_receipts()
    {
        var (_, connStr) = await pg.CreateFreshMigratedDbAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var aId = await InsertReceiptAsync(connStr, tenantA);
        var bId = await InsertReceiptAsync(connStr, tenantB);

        var asA = await ReadAllReceiptIdsAsync(connStr, tenantA);
        asA.Should().ContainSingle().Which.Should().Be(aId);

        var asB = await ReadAllReceiptIdsAsync(connStr, tenantB);
        asB.Should().ContainSingle().Which.Should().Be(bId);

        var withoutTenant = await ReadAllReceiptIdsAsync(connStr, tenantId: null);
        withoutTenant.Should().BeEmpty("no app.tenant_id => RLS denies all rows (fail-closed)");
    }

    [Fact]
    public async Task TenantA_cannot_INSERT_a_row_targeting_TenantB()
    {
        var (_, connStr) = await pg.CreateFreshMigratedDbAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var act = async () => await InsertReceiptAsync(connStr, sessionTenant: tenantA, rowTenant: tenantB);
        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == "42501",
                "RLS WITH CHECK clause must reject foreign-tenant inserts (insufficient_privilege)");
    }

    // ────────── helpers ──────────

    private static Task<Guid> InsertReceiptAsync(string connStr, Guid tenantId)
        => InsertReceiptAsync(connStr, sessionTenant: tenantId, rowTenant: tenantId);

    private static async Task<Guid> InsertReceiptAsync(string connStr, Guid sessionTenant, Guid rowTenant)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = $"SET app.tenant_id = '{sessionTenant:D}'";
            await setCmd.ExecuteNonQueryAsync();
        }
        var id = Guid.NewGuid();
        await using var ins = conn.CreateCommand();
        ins.CommandText = $$"""
            INSERT INTO "CaseIntakes"
                ("Id", "TenantId", "Status", "CaseTypeKey", "LobShortCode", "CreatedAt", "UpdatedAt")
            VALUES ('{{id:D}}', '{{rowTenant:D}}', {{(int)IntakeStatus.Queued}}, 'default', 'INV-APAC', now(), now())
            """;
        await ins.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<List<Guid>> ReadAllReceiptIdsAsync(string connStr, Guid? tenantId)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.CommandText = tenantId is null
                ? "RESET app.tenant_id"
                : $"SET app.tenant_id = '{tenantId:D}'";
            await setCmd.ExecuteNonQueryAsync();
        }
        var ids = new List<Guid>();
        await using var sel = conn.CreateCommand();
        sel.CommandText = @"SELECT ""Id"" FROM ""CaseIntakes""";
        await using var rdr = await sel.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) ids.Add(rdr.GetGuid(0));
        return ids;
    }
}
