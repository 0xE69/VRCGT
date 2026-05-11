using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCGT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastSyncedAt",
                table: "ManagedGroups",
                newName: "LastRefreshedAt");

            migrationBuilder.AddColumn<string>(
                name: "BannerUrl",
                table: "ManagedGroups",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ManagedGroups",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MemberCount",
                table: "ManagedGroups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OnlineCount",
                table: "ManagedGroups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ShortCode",
                table: "ManagedGroups",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BannerUrl",
                table: "ManagedGroups");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ManagedGroups");

            migrationBuilder.DropColumn(
                name: "MemberCount",
                table: "ManagedGroups");

            migrationBuilder.DropColumn(
                name: "OnlineCount",
                table: "ManagedGroups");

            migrationBuilder.DropColumn(
                name: "ShortCode",
                table: "ManagedGroups");

            migrationBuilder.RenameColumn(
                name: "LastRefreshedAt",
                table: "ManagedGroups",
                newName: "LastSyncedAt");
        }
    }
}
