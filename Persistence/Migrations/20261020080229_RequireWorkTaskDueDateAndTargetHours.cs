using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireWorkTaskDueDateAndTargetHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "WorkTaskSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DueDateRequirement", "TargetHoursRequirement" },
                values: new object[] { 1, 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "WorkTaskSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DueDateRequirement", "TargetHoursRequirement" },
                values: new object[] { 0, 0 });
        }
    }
}
