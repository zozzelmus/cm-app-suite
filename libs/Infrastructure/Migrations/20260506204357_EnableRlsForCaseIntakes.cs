using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conduct.Infrastructure.Migrations
{
    /// <summary>
    /// Extend the tenant-isolation RLS policy from <see cref="EnableTenantRls"/> to the
    /// <c>CaseIntakes</c> table introduced in <see cref="AddCaseIntake"/>.
    ///
    /// Without this, a connection without <c>app.tenant_id</c> set (or with a forged tenant id)
    /// could SELECT every tenant's intake receipts. EF query filters alone are insufficient
    /// per the locked architecture in <c>project_data_access.md</c> (Layer 1 must be airtight).
    ///
    /// Lesson learned: every new tenanted table needs the same RLS DDL. New tenanted entities
    /// going forward should ship a per-table follow-up migration in the same PR.
    /// </summary>
    public partial class EnableRlsForCaseIntakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CaseIntakes" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "CaseIntakes" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "CaseIntakes"
                    USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON "CaseIntakes";
                ALTER TABLE "CaseIntakes" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "CaseIntakes" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
