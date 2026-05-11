using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCGT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCurrentItemLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentItemLabel",
                table: "BackgroundJobs",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentItemLabel",
                table: "BackgroundJobs");
        }
    }
}
