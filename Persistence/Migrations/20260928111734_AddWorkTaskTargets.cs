using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkTaskTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetHours",
                table: "WorkTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetWeeks",
                table: "WorkTasks",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetHours",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "TargetWeeks",
                table: "WorkTasks");
        }
    }
}
