using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkTimesheetEntriesToTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkTaskId",
                table: "TimesheetEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_WorkTaskId",
                table: "TimesheetEntries",
                column: "WorkTaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimesheetEntries_WorkTasks_WorkTaskId",
                table: "TimesheetEntries",
                column: "WorkTaskId",
                principalTable: "WorkTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimesheetEntries_WorkTasks_WorkTaskId",
                table: "TimesheetEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_WorkTaskId",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "WorkTaskId",
                table: "TimesheetEntries");
        }
    }
}
