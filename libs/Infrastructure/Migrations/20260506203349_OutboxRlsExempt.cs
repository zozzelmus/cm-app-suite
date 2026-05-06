using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conduct.Infrastructure.Migrations
{
    /// <summary>
    /// Exempt the <c>Outbox</c> table from Row-Level Security.
    ///
    /// The outbox is INFRASTRUCTURE — a transactional queue that the relay polls without a
    /// tenant scope (the relay isn't acting "as" any tenant; it just publishes whatever rows
    /// were written by per-tenant handlers). With RLS enabled (from <see cref="EnableTenantRls"/>)
    /// the relay's SELECT would be filtered to zero rows because the relay's connection has
    /// no <c>app.tenant_id</c> set, defeating the entire pipeline.
    ///
    /// Tenant isolation is preserved by:
    ///   - The <c>TenantId</c> column on every outbox row (set at write time inside the
    ///     tenant-scoped DB transaction)
    ///   - The <c>tenant-id</c> Kafka header propagated from row to message
    ///   - Downstream consumers re-establishing tenant context from that header before any
    ///     domain writes
    ///
    /// Outbox rows are not user-visible and are append-only with INSERT-only grants for the
    /// app role (per <c>project_data_access.md</c>), so the surface area for cross-tenant
    /// leakage at the outbox layer is operational visibility only — the data still flows
    /// through tenant-scoped consumers downstream.
    /// </summary>
    public partial class OutboxRlsExempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON "Outbox";
                ALTER TABLE "Outbox" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "Outbox" DISABLE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Outbox" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "Outbox" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "Outbox"
                    USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }
    }
}
