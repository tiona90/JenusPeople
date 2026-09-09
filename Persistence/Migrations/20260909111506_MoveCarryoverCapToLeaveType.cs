using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// The carryover cap was org-wide on AppSettings, edited on Leave Settings, while the
    /// allowance it caps lives on the leave type. One number bounding another it could not
    /// see, and no way to say that sick leave carries nothing while annual leave carries
    /// five. It moves to LeaveType, beside DefaultAllowance, the way
    /// RemoveAppSettingsDefaultAnnualEntitlement moved the allowance itself.
    ///
    /// The stored cap is copied onto the AffectsBalance type (annual leave, in practice)
    /// before the column goes, so the figure an admin configured survives. Every other type
    /// starts at 0 -- no carryover -- which is what the app assumed for them anyway.
    /// The backfill runs here rather than in DbInitializer because seeding is off in
    /// Production, so a deployed host would never see it.
    /// </summary>
    public partial class MoveCarryoverCapToLeaveType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxCarryoverDays",
                table: "LeaveTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Carry the configured cap over before the column it lives in is dropped.
            // No AppSettings row means a fresh database: every type keeps its 0.
            migrationBuilder.Sql(@"
                UPDATE lt
                SET lt.MaxCarryoverDays = s.MaxCarryoverDays
                FROM LeaveTypes lt
                CROSS JOIN (SELECT TOP 1 MaxCarryoverDays FROM AppSettings ORDER BY Id) s
                WHERE lt.AffectsBalance = 1;");

            migrationBuilder.DropColumn(
                name: "MaxCarryoverDays",
                table: "AppSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 5 was the property's default, so a rollback restores the value the code that
            // reads this column shipped with rather than a 0 meaning "nothing carries over".
            migrationBuilder.AddColumn<int>(
                name: "MaxCarryoverDays",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.Sql(@"
                UPDATE s
                SET s.MaxCarryoverDays = lt.MaxCarryoverDays
                FROM AppSettings s
                CROSS JOIN (SELECT TOP 1 MaxCarryoverDays FROM LeaveTypes WHERE AffectsBalance = 1 ORDER BY Id) lt;");

            migrationBuilder.DropColumn(
                name: "MaxCarryoverDays",
                table: "LeaveTypes");
        }
    }
}
