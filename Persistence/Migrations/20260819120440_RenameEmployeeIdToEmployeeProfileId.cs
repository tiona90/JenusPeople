using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameEmployeeIdToEmployeeProfileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceEvents_EmployeeProfiles_EmployeeId",
                table: "AttendanceEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_Timesheets_EmployeeProfiles_EmployeeId",
                table: "Timesheets");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "Timesheets",
                newName: "EmployeeProfileId");

            migrationBuilder.RenameIndex(
                name: "IX_Timesheets_EmployeeId_PeriodStart_Unique",
                table: "Timesheets",
                newName: "IX_Timesheets_EmployeeProfileId_PeriodStart_Unique");

            migrationBuilder.RenameIndex(
                name: "IX_Timesheets_EmployeeId",
                table: "Timesheets",
                newName: "IX_Timesheets_EmployeeProfileId");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "AttendanceEvents",
                newName: "EmployeeProfileId");

            migrationBuilder.RenameIndex(
                name: "IX_AttendanceEvents_EmployeeId_At",
                table: "AttendanceEvents",
                newName: "IX_AttendanceEvents_EmployeeProfileId_At");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceEvents_EmployeeProfiles_EmployeeProfileId",
                table: "AttendanceEvents",
                column: "EmployeeProfileId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Timesheets_EmployeeProfiles_EmployeeProfileId",
                table: "Timesheets",
                column: "EmployeeProfileId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceEvents_EmployeeProfiles_EmployeeProfileId",
                table: "AttendanceEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_Timesheets_EmployeeProfiles_EmployeeProfileId",
                table: "Timesheets");

            migrationBuilder.RenameColumn(
                name: "EmployeeProfileId",
                table: "Timesheets",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_Timesheets_EmployeeProfileId_PeriodStart_Unique",
                table: "Timesheets",
                newName: "IX_Timesheets_EmployeeId_PeriodStart_Unique");

            migrationBuilder.RenameIndex(
                name: "IX_Timesheets_EmployeeProfileId",
                table: "Timesheets",
                newName: "IX_Timesheets_EmployeeId");

            migrationBuilder.RenameColumn(
                name: "EmployeeProfileId",
                table: "AttendanceEvents",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_AttendanceEvents_EmployeeProfileId_At",
                table: "AttendanceEvents",
                newName: "IX_AttendanceEvents_EmployeeId_At");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceEvents_EmployeeProfiles_EmployeeId",
                table: "AttendanceEvents",
                column: "EmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Timesheets_EmployeeProfiles_EmployeeId",
                table: "Timesheets",
                column: "EmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
