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
            // Pre-existing drift, not per-child leave: commit 930160f (Remove Slack
            // notification integration) dropped this column from the model but never
            // got its own migration. `dotnet ef migrations add` diffs the whole model
            // against the last snapshot, so the first migration generated after that
            // commit was always going to sweep this in alongside whatever it was
            // actually written for.
            //
            // Guarded, because it is no longer the only migration that drops it.
            // feature/user-gender's AddUserGender carries the same incidental drop
            // (it was the first migration generated on that branch, for the same
            // reason), so on any database where that ran first the column is
            // already gone and a plain DropColumn would fail — taking the whole
            // startup with it, since Program.cs exits when migration fails.
            //
            // The default constraint has to go first, and by name: the column is
            // bit NOT NULL DEFAULT 0, so SQL Server has an auto-named
            // DF__AppSettings__… constraint bound to it and refuses to drop the
            // column while it exists (error 5074). DropColumn emitted that for us;
            // hand-written SQL has to do it explicitly, and cannot hard-code the
            // name because SQL Server generated it per database.
            migrationBuilder.Sql(@"
                IF COL_LENGTH('AppSettings', 'SlackEnabled') IS NOT NULL
                BEGIN
                    DECLARE @constraint sysname;

                    SELECT @constraint = dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.object_id = dc.parent_object_id
                        AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'[AppSettings]')
                        AND c.name = N'SlackEnabled';

                    IF @constraint IS NOT NULL
                        EXEC(N'ALTER TABLE [AppSettings] DROP CONSTRAINT [' + @constraint + N']');

                    ALTER TABLE [AppSettings] DROP COLUMN [SlackEnabled];
                END
            ");

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

            // Reverses the incidental drop above by recreating a column no code reads
            // any more — kept only so Down mirrors Up exactly. Guarded for the same
            // reason as Up: AddUserGender's Down re-adds it too, and adding a column
            // that is already there fails.
            migrationBuilder.Sql(@"
                IF COL_LENGTH('AppSettings', 'SlackEnabled') IS NULL
                    ALTER TABLE [AppSettings] ADD [SlackEnabled] bit NOT NULL DEFAULT 0;
            ");
        }
    }
}
