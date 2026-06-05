using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityTypeToTimesheetEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActivityTypeId",
                table: "TimesheetEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_ActivityTypeId",
                table: "TimesheetEntries",
                column: "ActivityTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimesheetEntries_ProjectActivityTypes_ActivityTypeId",
                table: "TimesheetEntries",
                column: "ActivityTypeId",
                principalTable: "ProjectActivityTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimesheetEntries_ProjectActivityTypes_ActivityTypeId",
                table: "TimesheetEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_ActivityTypeId",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "ActivityTypeId",
                table: "TimesheetEntries");
        }
    }
}
