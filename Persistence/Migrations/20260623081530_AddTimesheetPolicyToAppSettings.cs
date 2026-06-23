using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheetPolicyToAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing settings rows with the historical hardcoded defaults
            // (Friday 18:00 UTC deadline, 40h/week target) so behaviour is unchanged.
            migrationBuilder.AddColumn<string>(
                name: "TimesheetSubmissionDeadlineDay",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "fri");

            migrationBuilder.AddColumn<string>(
                name: "TimesheetSubmissionDeadlineTime",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "18:00");

            migrationBuilder.AddColumn<int>(
                name: "WeeklyHoursTarget",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 40);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimesheetSubmissionDeadlineDay",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "TimesheetSubmissionDeadlineTime",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "WeeklyHoursTarget",
                table: "AppSettings");
        }
    }
}
