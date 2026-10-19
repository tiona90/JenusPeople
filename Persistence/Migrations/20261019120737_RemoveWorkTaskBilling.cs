using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWorkTaskBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillableRequirement",
                table: "WorkTaskSettings");

            migrationBuilder.DropColumn(
                name: "IsBillable",
                table: "WorkTasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillableRequirement",
                table: "WorkTaskSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsBillable",
                table: "WorkTasks",
                type: "bit",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "WorkTaskSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "BillableRequirement",
                value: 1);
        }
    }
}
