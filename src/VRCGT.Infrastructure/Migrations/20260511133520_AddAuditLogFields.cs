using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCGT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorDisplayName",
                table: "AuditLog",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AuditLog",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AuditLog",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TargetDisplayName",
                table: "AuditLog",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_GroupId_Action",
                table: "AuditLog",
                columns: new[] { "GroupId", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_GroupId_Timestamp",
                table: "AuditLog",
                columns: new[] { "GroupId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_GroupId_Timestamp_Action_ActorId",
                table: "AuditLog",
                columns: new[] { "GroupId", "Timestamp", "Action", "ActorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLog_GroupId_Action",
                table: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_GroupId_Timestamp",
                table: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_GroupId_Timestamp_Action_ActorId",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "ActorDisplayName",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "TargetDisplayName",
                table: "AuditLog");
        }
    }
}
