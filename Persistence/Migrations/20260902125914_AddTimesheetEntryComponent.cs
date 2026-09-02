using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheetEntryComponent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectComponentId",
                table: "TimesheetEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_ProjectComponentId",
                table: "TimesheetEntries",
                column: "ProjectComponentId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimesheetEntries_ProjectComponents_ProjectComponentId",
                table: "TimesheetEntries",
                column: "ProjectComponentId",
                principalTable: "ProjectComponents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimesheetEntries_ProjectComponents_ProjectComponentId",
                table: "TimesheetEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_ProjectComponentId",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "ProjectComponentId",
                table: "TimesheetEntries");
        }
    }
}
