using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurePaternityPerChildEntitlement : Migration
    {
        /// <summary>
        /// Paternity Leave was seeded as a flat 14 days/event, which cannot express
        /// an entitlement that is per child and bounded by the child's age. This
        /// moves the deployed row onto the per-child policy.
        ///
        /// A migration rather than a DbInitializer change on purpose: Seed:Enabled
        /// is false in Production, so the seeder never runs on the deployed host
        /// and a seeder-only fix would silently skip it.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [LeaveTypes]
                SET [PerChildEntitlement]   = 1,
                    [PerChildTotalWeeks]    = 18,
                    [PerChildWeeksPerYear]  = 5,
                    [ChildEligibleUntilAge] = 15,
                    [AllowanceUnit]         = 'weeks/child',
                    -- 0 so no surface quotes the dead flat allowance.
                    [DefaultAllowance]      = 0,
                    -- Display-only, but 14 contradicted the 5-week annual cap.
                    [MaxConsecutiveDays]    = 25,
                    [AccrualNotes]          = '18 weeks per child · Max 5 weeks per child per year · Until the child turns 15',
                    [EligibilityNotes]      = 'Employees with children under 15',
                    [Description]           = 'Time off for a father around the birth of a child, and while that child is young.'
                WHERE [Name] = 'Paternity Leave';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [LeaveTypes]
                SET [PerChildEntitlement]   = 0,
                    [PerChildTotalWeeks]    = 0,
                    [PerChildWeeksPerYear]  = 0,
                    [ChildEligibleUntilAge] = 0,
                    [AllowanceUnit]         = 'days/event',
                    [DefaultAllowance]      = 14,
                    [MaxConsecutiveDays]    = 14,
                    [AccrualNotes]          = 'Granted per event · Once per child',
                    [EligibilityNotes]      = 'Male employees',
                    [Description]           = 'Time off for new fathers around the birth of a child.'
                WHERE [Name] = 'Paternity Leave';
                """);
        }
    }
}
