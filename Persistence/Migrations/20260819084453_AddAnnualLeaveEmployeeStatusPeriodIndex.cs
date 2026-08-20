using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualLeaveEmployeeStatusPeriodIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnnualLeaves_EmployeeId",
                table: "AnnualLeaves");

            migrationBuilder.CreateIndex(
                name: "IX_AnnualLeaves_EmployeeId_Status_StartDate_EndDate",
                table: "AnnualLeaves",
                columns: new[] { "EmployeeId", "Status", "StartDate", "EndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnnualLeaves_EmployeeId_Status_StartDate_EndDate",
                table: "AnnualLeaves");

            migrationBuilder.CreateIndex(
                name: "IX_AnnualLeaves_EmployeeId",
                table: "AnnualLeaves",
                column: "EmployeeId");
        }
    }
}
