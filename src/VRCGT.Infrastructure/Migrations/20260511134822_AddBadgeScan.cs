using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCGT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBadgeScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BadgeScans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalMembers = table.Column<int>(type: "INTEGER", nullable: false),
                    ScannedMembers = table.Column<int>(type: "INTEGER", nullable: false),
                    VerifiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnverifiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnknownCount = table.Column<int>(type: "INTEGER", nullable: false),
                    KickedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    JobId = table.Column<long>(type: "INTEGER", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadgeScans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BadgeScanMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScanId = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsAgeVerified = table.Column<bool>(type: "INTEGER", nullable: true),
                    WasKicked = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadgeScanMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadgeScanMembers_BadgeScans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "BadgeScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BadgeScanMembers_ScanId",
                table: "BadgeScanMembers",
                column: "ScanId");

            migrationBuilder.CreateIndex(
                name: "IX_BadgeScanMembers_ScanId_UserId",
                table: "BadgeScanMembers",
                columns: new[] { "ScanId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BadgeScans_CreatedAt",
                table: "BadgeScans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BadgeScans_GroupId",
                table: "BadgeScans",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BadgeScanMembers");

            migrationBuilder.DropTable(
                name: "BadgeScans");
        }
    }
}
