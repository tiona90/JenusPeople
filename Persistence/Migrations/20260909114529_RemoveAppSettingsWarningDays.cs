using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Drops AppSettings.YearEndWarningDays and FinalWarningDays. They were editable
    /// on Leave Settings and read by exactly one thing: the schedule preview on that
    /// same page. Nothing schedules a warning email from them, so an admin who changed
    /// 30 to 45 changed a caption and a preview date, and no employee was ever warned
    /// any earlier. The two lead times are stated by the client now
    /// (AppSettingsPanel's YEAR_END_WARNING_DAYS / FINAL_WARNING_DAYS), and belong on
    /// whatever ends up sending the emails rather than in a settings row.
    ///
    /// Nothing is copied out first, unlike MoveCarryoverCapToLeaveType: the values had
    /// no second home to move to and no behaviour depending on them.
    /// </summary>
    public partial class RemoveAppSettingsWarningDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalWarningDays",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "YearEndWarningDays",
                table: "AppSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinalWarningDays",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "YearEndWarningDays",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);
        }
    }
}
