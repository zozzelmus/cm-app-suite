using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conduct.Infrastructure.Migrations
{
    /// <summary>
    /// Enable Postgres Row-Level Security on every tenanted table. The app sets
    /// <c>app.tenant_id</c> per connection (TenantConnectionInterceptor) and a
    /// <c>tenant_isolation</c> policy on each table filters by that GUC.
    ///
    /// FORCE ROW LEVEL SECURITY makes the policy apply to the table owner too — without it
    /// the connecting role (which IS the owner in dev/test) would silently bypass RLS, and
    /// our isolation tests would show false-green.
    ///
    /// Policy applies to ALL operations (USING + WITH CHECK) so INSERTs and UPDATEs that
    /// would set a foreign tenant_id are rejected, not just SELECT/UPDATE/DELETE filtered.
    /// </summary>
    public partial class EnableTenantRls : Migration
    {
        // Every tenanted table in the model. Sourced from ConductDbContext mappings; if a
        // future entity adds TenantId, add it here and create a follow-up migration.
        private static readonly string[] TenantedTables =
        {
            "Lobs",
            "CaseTypes",
            "Cases",
            "CaseParties",
            "CaseNotes",
            "Parties",
            "EmployeeProfiles",
            "CustomerProfiles",
            "VendorProfiles",
            "ExternalProfiles",
            "Users",
            "Roles",
            "Groups",
            "GroupMemberships",
            "Assignments",
            "AuditEvents",
            "Outbox",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantedTables)
            {
                // current_setting('app.tenant_id', true) returns:
                //   - NULL if the GUC was never set on this session
                //   - '' (empty string) if the GUC was SET then RESET
                //   - the literal value otherwise
                // NULLIF(..., '')::uuid normalises both "unset" and "reset" to NULL → the
                // predicate `TenantId = NULL` is UNKNOWN → row excluded (fail-closed).
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON "{table}"
                        USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantedTables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS tenant_isolation ON "{table}";
                    ALTER TABLE "{table}" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }
        }
    }
}
