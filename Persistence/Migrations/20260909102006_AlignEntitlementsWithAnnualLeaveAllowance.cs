using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>
    /// Leave is configured once, for everyone: the allowance on the balance-affecting
    /// leave type. Existing profiles were stamped with a per-person entitlement that
    /// no longer has a screen to set it — and by default disagreed with the allowance
    /// (20 against annual leave's 25). This brings every profile onto the allowance so
    /// what the screens quote is what <c>AnnualLeaveBalanceCalculator</c> enforces.
    ///
    /// **This grants days.** Anyone below the allowance gains the difference (5 days on
    /// the default seed). That is the intended effect, not a side effect.
    ///
    /// Days already taken are preserved rather than recomputed: `taken` is the old
    /// entitlement minus the old balance, so the new balance is the new allowance minus
    /// the same `taken`. That needs no date arithmetic in SQL and cannot disagree with
    /// the approved-leave history the way a re-derived figure could. It is floored at 0
    /// for anyone who had somehow overdrawn.
    ///
    /// A database with no active balance-affecting allowance updates nothing — the join
    /// yields no rows — so this is safe on a fresh database that migrates before it seeds.
    /// </summary>
    public partial class AlignEntitlementsWithAnnualLeaveAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every right-hand expression is evaluated against the pre-update row, so
            // the LeaveBalance arithmetic still sees the OLD AnnualLeaveEntitlement even
            // though the same statement replaces it.
            migrationBuilder.Sql("""
                UPDATE ep
                SET ep.LeaveBalance =
                        CASE
                            WHEN lt.DefaultAllowance - (ep.AnnualLeaveEntitlement - ep.LeaveBalance) < 0 THEN 0
                            ELSE lt.DefaultAllowance - (ep.AnnualLeaveEntitlement - ep.LeaveBalance)
                        END,
                    ep.AnnualLeaveEntitlement = lt.DefaultAllowance
                FROM EmployeeProfiles ep
                CROSS JOIN (
                    SELECT TOP 1 DefaultAllowance
                    FROM LeaveTypes
                    WHERE AffectsBalance = 1 AND IsActive = 1 AND DefaultAllowance > 0
                    ORDER BY Id
                ) lt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The per-person entitlements this replaced are not recoverable — they were
            // overwritten in place, and nothing recorded what each one had been. Rolling
            // back restores the schema, not the numbers; every profile keeps the
            // allowance it was aligned to.
        }
    }
}
