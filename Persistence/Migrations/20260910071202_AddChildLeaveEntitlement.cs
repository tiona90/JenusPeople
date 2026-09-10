using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChildLeaveEntitlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SlackEnabled",
                table: "AppSettings");

            migrationBuilder.AddColumn<int>(
                name: "ChildEligibleUntilAge",
                table: "LeaveTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PerChildEntitlement",
                table: "LeaveTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PerChildTotalWeeks",
                table: "LeaveTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PerChildWeeksPerYear",
                table: "LeaveTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasChildren",
                table: "EmployeeProfiles",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChildId",
                table: "AnnualLeaves",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Children",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EmployeeProfileId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Children", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Children_EmployeeProfiles_EmployeeProfileId",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnnualLeaves_ChildId_Status_StartDate_EndDate",
                table: "AnnualLeaves",
                columns: new[] { "ChildId", "Status", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Children_EmployeeProfileId",
                table: "Children",
                column: "EmployeeProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_AnnualLeaves_Children_ChildId",
                table: "AnnualLeaves",
                column: "ChildId",
                principalTable: "Children",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnnualLeaves_Children_ChildId",
                table: "AnnualLeaves");

            migrationBuilder.DropTable(
                name: "Children");

            migrationBuilder.DropIndex(
                name: "IX_AnnualLeaves_ChildId_Status_StartDate_EndDate",
                table: "AnnualLeaves");

            migrationBuilder.DropColumn(
                name: "ChildEligibleUntilAge",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "PerChildEntitlement",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "PerChildTotalWeeks",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "PerChildWeeksPerYear",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "HasChildren",
                table: "EmployeeProfiles");

            migrationBuilder.DropColumn(
                name: "ChildId",
                table: "AnnualLeaves");

            migrationBuilder.AddColumn<bool>(
                name: "SlackEnabled",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
