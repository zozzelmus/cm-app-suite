using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conduct.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseNumberSeq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseNumberSeqs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LobShortCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseNumberSeqs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseNumberSeqs_TenantId_Year_LobShortCode",
                table: "CaseNumberSeqs",
                columns: new[] { "TenantId", "Year", "LobShortCode" },
                unique: true);

            // RLS: tenanted table → enforce isolation alongside the application code that
            // only uses the table inside a tenant-scoped consumer scope. Lesson from F4 review:
            // every new tenanted table needs this in the same PR or a guaranteed follow-up.
            migrationBuilder.Sql("""
                ALTER TABLE "CaseNumberSeqs" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "CaseNumberSeqs" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "CaseNumberSeqs"
                    USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON "CaseNumberSeqs";
                ALTER TABLE "CaseNumberSeqs" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "CaseNumberSeqs" DISABLE ROW LEVEL SECURITY;
                """);
            migrationBuilder.DropTable(name: "CaseNumberSeqs");
        }
    }
}
