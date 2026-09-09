using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Leave Settings carried an org-wide annual entitlement that was free to disagree
    /// with the Annual Leave type's own allowance on Leave Types — and by default did:
    /// 20 against 25. Leave Types is now the single place the figure is set, and
    /// CreateAdminUser reads the AffectsBalance type's allowance for a new joiner.
    ///
    /// Dropping the column loses only that duplicate. Per-employee entitlements live in
    /// EmployeeProfile.AnnualLeaveEntitlement and are untouched, as is the leave type's
    /// DefaultAllowance — no employee's balance changes.
    /// </summary>
    public partial class RemoveAppSettingsDefaultAnnualEntitlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultAnnualEntitlement",
                table: "AppSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 20 was the property's default, so a rollback restores the value the code
            // that reads this column shipped with rather than a 0 it would reject.
            migrationBuilder.AddColumn<int>(
                name: "DefaultAnnualEntitlement",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 20);
        }
    }
}
