using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// Database-aware orchestrator around <see cref="LeaveCalculationService"/>.
/// This class loads inputs from the DbContext (holidays, leave-year configuration,
/// prior approved leaves) and delegates every pure calculation to the domain service.
/// New business rules belong in the service, not here.
/// </summary>
internal static class AnnualLeaveBalanceCalculator
{
    /// <summary>
    /// Throwing wrapper kept for callers that surface insufficient balance as an
    /// unexpected exception (e.g. status-change flows). Handlers that return
    /// <see cref="Application.Core.Result{T}"/> should call
    /// <see cref="CheckSufficientBalanceAsync"/> and map the message to a Failure instead.
    /// </summary>
    public static async Task EnsureSufficientBalanceAsync(
        AppDbContext context,
        EmployeeProfile employeeProfile,
        AnnualLeave annualLeave,
        string? excludeLeaveId,
        CancellationToken cancellationToken)
    {
        var error = await CheckSufficientBalanceAsync(
            context, employeeProfile, annualLeave, excludeLeaveId, cancellationToken);
        if (error is not null)
            throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Returns a human-readable error message when the requested leave would exceed
    /// the employee's remaining balance, or <c>null</c> when the balance is sufficient
    /// (or the check does not apply). Never throws for the business rule.
    /// </summary>
    /// <param name="alsoApproved">
    /// Leave approved earlier in the same unit of work and not yet saved — the
    /// database cannot see it, so it is counted here. The leave-type sweep passes
    /// what it has approved so far; without it, one employee's several pending
    /// requests each passed on their own and together overspent the balance.
    /// </param>
    public static async Task<string?> CheckSufficientBalanceAsync(
        AppDbContext context,
        EmployeeProfile employeeProfile,
        AnnualLeave annualLeave,
        string? excludeLeaveId,
        CancellationToken cancellationToken,
        IReadOnlyCollection<AnnualLeave>? alsoApproved = null)
    {
        if (!await AffectsBalanceAsync(context, annualLeave.LeaveTypeId, cancellationToken))
            return null;

        // Entitlement of 0 means it has not been configured yet — skip the check.
        if (employeeProfile.AnnualLeaveEntitlement <= 0)
            return null;

        var startMonth = await LeaveYearQueries.GetLeaveYearStartMonthAsync(context, cancellationToken);
        var proRateFirstYear = await ProRatesFirstYearAsync(context, cancellationToken);
        var holidays = await LeaveYearQueries.GetHolidaySetAsync(context, annualLeave.StartDate, annualLeave.EndDate, cancellationToken);

        foreach (var leaveYearKey in LeaveCalculationService.GetCoveredLeaveYears(
                     annualLeave.StartDate, annualLeave.EndDate, startMonth))
        {
            var requestedDays = LeaveCalculationService.CalculateChargeableDaysInLeaveYear(
                annualLeave.StartDate, annualLeave.EndDate, annualLeave.Duration,
                leaveYearKey, startMonth, holidays);
            if (requestedDays <= 0)
                continue;

            var usedDays = await GetApprovedDaysForLeaveYearAsync(
                context, annualLeave.EmployeeId, leaveYearKey, startMonth, excludeLeaveId, cancellationToken, alsoApproved);

            var remainingBalance = LeaveCalculationService.CalculateRemainingBalance(
                EntitlementForLeaveYear(employeeProfile, leaveYearKey, startMonth, proRateFirstYear), usedDays);
            if (remainingBalance < requestedDays)
            {
                var (lyStart, lyEnd) = LeaveCalculationService.GetLeaveYearBounds(leaveYearKey, startMonth);
                return $"Insufficient leave balance for the leave year {lyStart:dd MMM yyyy} – {lyEnd:dd MMM yyyy}. " +
                    $"Remaining balance: {remainingBalance} day(s).";
            }
        }

        return null;
    }

    public static async Task SyncCurrentYearBalanceAsync(
        AppDbContext context,
        EmployeeProfile employeeProfile,
        CancellationToken cancellationToken)
    {
        var startMonth = await LeaveYearQueries.GetLeaveYearStartMonthAsync(context, cancellationToken);
        var currentLeaveYearKey = LeaveCalculationService.GetLeaveYearKey(DateTime.UtcNow, startMonth);
        var proRateFirstYear = await ProRatesFirstYearAsync(context, cancellationToken);

        var usedDays = await GetApprovedDaysForLeaveYearAsync(
            context, employeeProfile.UserId, currentLeaveYearKey, startMonth,
            excludeLeaveId: null, cancellationToken);

        employeeProfile.LeaveBalance = LeaveCalculationService.CalculateRemainingBalance(
            EntitlementForLeaveYear(employeeProfile, currentLeaveYearKey, startMonth, proRateFirstYear), usedDays);
    }

    /// <summary>
    /// What one employee may take in one leave year: the stored entitlement, or —
    /// when the balance type pro-rates the first year — that entitlement scaled to
    /// the months they were here for. The stored figure is never changed; a leave
    /// year the start date does not fall in gets it whole.
    /// </summary>
    public static decimal EntitlementForLeaveYear(
        EmployeeProfile employeeProfile, int leaveYearKey, int startMonth, bool proRateFirstYear)
    {
        if (!proRateFirstYear)
            return employeeProfile.AnnualLeaveEntitlement;
        return LeaveCalculationService.ProRateFirstYearEntitlement(
            employeeProfile.AnnualLeaveEntitlement, employeeProfile.EmploymentStartDate, leaveYearKey, startMonth);
    }

    /// <summary>
    /// The switch on the type the pooled balance is a budget for. There is one such
    /// type in practice; any of them asking is enough, since a second one is not a
    /// configuration the validator lets an admin express deliberately.
    /// </summary>
    public static async Task<bool> ProRatesFirstYearAsync(AppDbContext context, CancellationToken cancellationToken)
        => await context.LeaveTypes
            .AsNoTracking()
            .AnyAsync(lt => lt.AffectsBalance && lt.ProRateFirstYear, cancellationToken);

    // ── DB helpers ─────────────────────────────────────────────────────────────

    private static async Task<bool> AffectsBalanceAsync(
        AppDbContext context, int? leaveTypeId, CancellationToken cancellationToken)
    {
        if (!leaveTypeId.HasValue)
            return false;
        return await context.LeaveTypes
            .AsNoTracking()
            .AnyAsync(lt => lt.Id == leaveTypeId.Value && lt.AffectsBalance, cancellationToken);
    }

    private static async Task<decimal> GetApprovedDaysForLeaveYearAsync(
        AppDbContext context,
        string employeeId,
        int leaveYearKey,
        int startMonth,
        string? excludeLeaveId,
        CancellationToken cancellationToken,
        IReadOnlyCollection<AnnualLeave>? alsoApproved = null)
    {
        var balanceTypeIds = await context.LeaveTypes
            .AsNoTracking()
            .Where(lt => lt.AffectsBalance)
            .Select(lt => lt.Id)
            .ToListAsync(cancellationToken);

        if (balanceTypeIds.Count == 0)
            return 0;

        var (lyStart, lyEnd) = LeaveCalculationService.GetLeaveYearBounds(leaveYearKey, startMonth);

        var approvedLeaves = await context.AnnualLeaves
            .AsNoTracking()
            .Where(l =>
                l.EmployeeId == employeeId
                && l.Status == AnnualLeaveStatus.Approved
                && l.StartDate <= lyEnd
                && l.EndDate >= lyStart
                && l.LeaveTypeId.HasValue
                && balanceTypeIds.Contains(l.LeaveTypeId.Value)
                && (excludeLeaveId == null || l.Id != excludeLeaveId))
            .ToListAsync(cancellationToken);

        if (alsoApproved is not null)
        {
            approvedLeaves.AddRange(alsoApproved.Where(l =>
                l.EmployeeId == employeeId
                && l.StartDate <= lyEnd
                && l.EndDate >= lyStart
                && l.LeaveTypeId.HasValue
                && balanceTypeIds.Contains(l.LeaveTypeId.Value)
                && l.Id != excludeLeaveId
                && approvedLeaves.All(saved => saved.Id != l.Id)));
        }

        if (approvedLeaves.Count == 0) return 0;

        var holidays = await LeaveYearQueries.GetHolidaySetAsync(context, lyStart, lyEnd, cancellationToken);
        return approvedLeaves.Sum(l => LeaveCalculationService.CalculateChargeableDaysInLeaveYear(
            l.StartDate, l.EndDate, l.Duration, leaveYearKey, startMonth, holidays));
    }
}
