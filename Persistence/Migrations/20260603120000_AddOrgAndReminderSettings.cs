using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgAndReminderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkingHoursStart",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "09:00");

            migrationBuilder.AddColumn<string>(
                name: "WorkingHoursEnd",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "18:00");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<int>(
                name: "FinancialYearStartMonth",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailDailyDigest",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailUrgentOnly",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SlackEnabled",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RemindersJson",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "WorkingHoursStart", table: "AppSettings");
            migrationBuilder.DropColumn(name: "WorkingHoursEnd", table: "AppSettings");
            migrationBuilder.DropColumn(name: "TimeZoneId", table: "AppSettings");
            migrationBuilder.DropColumn(name: "FinancialYearStartMonth", table: "AppSettings");
            migrationBuilder.DropColumn(name: "EmailNotificationsEnabled", table: "AppSettings");
            migrationBuilder.DropColumn(name: "EmailDailyDigest", table: "AppSettings");
            migrationBuilder.DropColumn(name: "EmailUrgentOnly", table: "AppSettings");
            migrationBuilder.DropColumn(name: "SlackEnabled", table: "AppSettings");
            migrationBuilder.DropColumn(name: "RemindersJson", table: "AppSettings");
        }
    }
}
