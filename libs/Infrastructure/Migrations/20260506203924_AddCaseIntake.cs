using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conduct.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseIntakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CaseTypeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LobShortCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CaseNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorsJson = table.Column<string>(type: "jsonb", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseIntakes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseIntakes_CaseId",
                table: "CaseIntakes",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseIntakes_TenantId_Status",
                table: "CaseIntakes",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseIntakes");
        }
    }
}
