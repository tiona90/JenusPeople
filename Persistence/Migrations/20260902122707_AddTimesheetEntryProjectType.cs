using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheetEntryProjectType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectTypeId",
                table: "TimesheetEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_ProjectTypeId",
                table: "TimesheetEntries",
                column: "ProjectTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimesheetEntries_ProjectTypes_ProjectTypeId",
                table: "TimesheetEntries",
                column: "ProjectTypeId",
                principalTable: "ProjectTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimesheetEntries_ProjectTypes_ProjectTypeId",
                table: "TimesheetEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_ProjectTypeId",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "ProjectTypeId",
                table: "TimesheetEntries");
        }
    }
}
