using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// Enforces a per-child leave entitlement — paternity leave in practice: so many
/// weeks per child, only while that child is under a given age, and only so many of
/// them in any one leave year.
///
/// A sibling of <see cref="AnnualLeaveBalanceCalculator"/> and deliberately the same
/// shape: it loads its inputs from the DbContext, delegates every calculation to the
/// domain services, and returns a human-readable message rather than throwing, so a
/// handler can map it straight to <c>Result&lt;T&gt;.Failure</c>.
///
/// This is a second ledger, distinct from the pooled <c>EmployeeProfile.LeaveBalance</c>
/// the other calculator keeps. The two must never both apply to one leave type —
/// <c>UpsertLeaveTypeRequestValidator</c> refuses a type that sets both
/// <c>PerChildEntitlement</c> and <c>AffectsBalance</c> — or one day of leave would
/// be charged twice.
///
/// Nothing is stored: the ledger is a projection over approved leave rows, which is
/// why a child aging out needs no recalculation step. Their usage stays in history
/// and their remaining entitlement is simply gone.
/// </summary>
internal static class PerChildLeaveBalanceCalculator
{
    /// <summary>
    /// Returns a human-readable error when the request breaks the per-child rules,
    /// or <c>null</c> when it is allowed — including when the leave type has no
    /// per-child entitlement, in which case there is nothing to enforce.
    /// </summary>
    public static async Task<string?> CheckPerChildEntitlementAsync(
        AppDbContext context,
        AnnualLeave annualLeave,
        EmployeeProfile employeeProfile,
        string? excludeLeaveId,
        CancellationToken cancellationToken)
    {
        var leaveType = annualLeave.LeaveTypeId.HasValue
            ? await context.LeaveTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(lt => lt.Id == annualLeave.LeaveTypeId.Value, cancellationToken)
            : null;

        if (leaveType is null || !leaveType.PerChildEntitlement)
            return null;

        if (string.IsNullOrWhiteSpace(annualLeave.ChildId))
            return $"Select the child this {leaveType.Name} is for.";

        var childId = annualLeave.ChildId;
        var child = await context.Children
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == childId, cancellationToken);

        // One message for "no such child" and "not yours" on purpose: telling the
        // caller which of the two it was would confirm the existence of another
        // employee's record.
        if (child is null || child.EmployeeProfileId != employeeProfile.Id)
            return "The selected child is not on your profile.";

        var lastEligibleDate = PerChildLeaveCalculationService.LastEligibleDate(
            child.DateOfBirth, leaveType.ChildEligibleUntilAge);

        // Asked of the END date, so no request is ever part-eligible. This single
        // check covers a request that starts after the birthday and one that
        // straddles it.
        if (DateOnly.FromDateTime(annualLeave.EndDate.Date) > lastEligibleDate)
        {
            var birthday = lastEligibleDate.AddDays(1);
            return $"{child.Name} turns {leaveType.ChildEligibleUntilAge} on {birthday:dd MMM yyyy} — " +
                $"{leaveType.Name} for this child must end on or before {lastEligibleDate:dd MMM yyyy}.";
        }

        var startMonth = await LeaveYearQueries.GetLeaveYearStartMonthAsync(context, cancellationToken);
        var requestHolidays = await LeaveYearQueries.GetHolidaySetAsync(
            context, annualLeave.StartDate, annualLeave.EndDate, cancellationToken);

        var requestedDays = LeaveCalculationService.CalculateBusinessDays(
            annualLeave.StartDate, annualLeave.EndDate, requestHolidays);

        // A range made entirely of weekends and holidays charges nothing, so there
        // is no cap left to break.
        if (requestedDays <= 0)
            return null;

        var approved = await ApprovedLeaveForChildAsync(context, childId, excludeLeaveId, cancellationToken);

        // ── Lifetime cap ───────────────────────────────────────────────────────
        var totalDays = PerChildLeaveCalculationService.WeeksToBusinessDays(leaveType.PerChildTotalWeeks);
        var usedDays = await UsedBusinessDaysAsync(context, approved, cancellationToken);
        var remainingDays = PerChildLeaveCalculationService.RemainingDays(totalDays, usedDays);

        if (remainingDays < requestedDays)
        {
            return $"{child.Name} has {remainingDays} day(s) " +
                $"({PerChildLeaveCalculationService.BusinessDaysToWeeks(remainingDays)} week(s)) of " +
                $"{leaveType.Name} remaining in total. This request is {requestedDays} day(s).";
        }

        // ── Yearly cap, per leave year the request touches ─────────────────────
        // Checked year by year rather than in total: that is what stops ten weeks
        // arriving as one request straddling new year.
        var yearCapDays = PerChildLeaveCalculationService.WeeksToBusinessDays(leaveType.PerChildWeeksPerYear);

        foreach (var leaveYearKey in LeaveCalculationService.GetCoveredLeaveYears(
                     annualLeave.StartDate, annualLeave.EndDate, startMonth))
        {
            var requestedInYear = LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
                annualLeave.StartDate, annualLeave.EndDate, leaveYearKey, startMonth, requestHolidays);
            if (requestedInYear <= 0)
                continue;

            var (lyStart, lyEnd) = LeaveCalculationService.GetLeaveYearBounds(leaveYearKey, startMonth);
            var yearHolidays = await LeaveYearQueries.GetHolidaySetAsync(context, lyStart, lyEnd, cancellationToken);

            var usedInYear = approved.Sum(leave => LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
                leave.StartDate, leave.EndDate, leaveYearKey, startMonth, yearHolidays));

            var remainingInYear = PerChildLeaveCalculationService.RemainingDays(yearCapDays, usedInYear);
            if (remainingInYear < requestedInYear)
            {
                return $"{child.Name} has {remainingInYear} day(s) " +
                    $"({PerChildLeaveCalculationService.BusinessDaysToWeeks(remainingInYear)} week(s)) of " +
                    $"{leaveType.Name} left for the leave year {lyStart:dd MMM yyyy} – {lyEnd:dd MMM yyyy}. " +
                    $"This request uses {requestedInYear} day(s) in that year.";
            }
        }

        return null;
    }

    // ── DB helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Approved leave for one child. Only Approved counts — the same rule the pooled
    /// balance applies — and rows with a null ChildId are excluded by construction,
    /// since this is queried by child.
    /// </summary>
    private static Task<List<AnnualLeave>> ApprovedLeaveForChildAsync(
        AppDbContext context,
        string childId,
        string? excludeLeaveId,
        CancellationToken cancellationToken)
        => context.AnnualLeaves
            .AsNoTracking()
            .Where(leave =>
                leave.ChildId == childId
                && leave.Status == AnnualLeaveStatus.Approved
                && (excludeLeaveId == null || leave.Id != excludeLeaveId))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Business days across every approved leave, holiday-aware. One holiday query
    /// spanning the whole set rather than one per row: the set is small and the
    /// dates are sparse, so the range read costs less than N round trips.
    /// </summary>
    private static async Task<int> UsedBusinessDaysAsync(
        AppDbContext context,
        List<AnnualLeave> approved,
        CancellationToken cancellationToken)
    {
        if (approved.Count == 0)
            return 0;

        var rangeStart = approved.Min(leave => leave.StartDate);
        var rangeEnd = approved.Max(leave => leave.EndDate);
        var holidays = await LeaveYearQueries.GetHolidaySetAsync(context, rangeStart, rangeEnd, cancellationToken);

        return approved.Sum(leave => LeaveCalculationService.CalculateBusinessDays(
            leave.StartDate, leave.EndDate, holidays));
    }
}
