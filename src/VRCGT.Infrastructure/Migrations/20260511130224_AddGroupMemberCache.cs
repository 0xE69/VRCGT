using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCGT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMemberCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    JoinedAt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RoleIds = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    RoleNames = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsRepresenting = table.Column<bool>(type: "INTEGER", nullable: false),
                    CachedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => new { x.GroupId, x.UserId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId",
                table: "GroupMembers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId_DisplayName",
                table: "GroupMembers",
                columns: new[] { "GroupId", "DisplayName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupMembers");
        }
    }
}
