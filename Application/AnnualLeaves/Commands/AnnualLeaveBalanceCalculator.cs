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
    public static async Task EnsureSufficientBalanceAsync(
        AppDbContext context,
        EmployeeProfile employeeProfile,
        AnnualLeave annualLeave,
        string? excludeLeaveId,
        CancellationToken cancellationToken)
    {
        if (!await AffectsBalanceAsync(context, annualLeave.LeaveTypeId, cancellationToken))
            return;

        // Entitlement of 0 means it has not been configured yet — skip the check.
        if (employeeProfile.AnnualLeaveEntitlement <= 0)
            return;

        var startMonth = await GetLeaveYearStartMonthAsync(context, cancellationToken);
        var holidays = await GetHolidaySetAsync(context, annualLeave.StartDate, annualLeave.EndDate, cancellationToken);

        foreach (var leaveYearKey in LeaveCalculationService.GetCoveredLeaveYears(
                     annualLeave.StartDate, annualLeave.EndDate, startMonth))
        {
            var requestedDays = LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
                annualLeave.StartDate, annualLeave.EndDate, leaveYearKey, startMonth, holidays);
            if (requestedDays <= 0)
                continue;

            var usedDays = await GetApprovedDaysForLeaveYearAsync(
                context, annualLeave.EmployeeId, leaveYearKey, startMonth, excludeLeaveId, cancellationToken);

            var remainingBalance = LeaveCalculationService.CalculateRemainingBalance(
                employeeProfile.AnnualLeaveEntitlement, usedDays);
            if (remainingBalance < requestedDays)
            {
                var (lyStart, lyEnd) = LeaveCalculationService.GetLeaveYearBounds(leaveYearKey, startMonth);
                throw new InvalidOperationException(
                    $"Insufficient leave balance for the leave year {lyStart:dd MMM yyyy} – {lyEnd:dd MMM yyyy}. " +
                    $"Remaining balance: {remainingBalance} day(s).");
            }
        }
    }

    public static async Task SyncCurrentYearBalanceAsync(
        AppDbContext context,
        EmployeeProfile employeeProfile,
        CancellationToken cancellationToken)
    {
        var startMonth = await GetLeaveYearStartMonthAsync(context, cancellationToken);
        var currentLeaveYearKey = LeaveCalculationService.GetLeaveYearKey(DateTime.UtcNow, startMonth);

        var usedDays = await GetApprovedDaysForLeaveYearAsync(
            context, employeeProfile.UserId, currentLeaveYearKey, startMonth,
            excludeLeaveId: null, cancellationToken);

        employeeProfile.LeaveBalance = LeaveCalculationService.CalculateRemainingBalance(
            employeeProfile.AnnualLeaveEntitlement, usedDays);
    }

    // ── DB helpers ─────────────────────────────────────────────────────────────

    private static async Task<HashSet<DateTime>> GetHolidaySetAsync(
        AppDbContext context, DateTime rangeStart, DateTime rangeEnd, CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var code = settings?.HolidayCountryCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            return [];

        var startDate = rangeStart.Date;
        var endDate = rangeEnd.Date;

        var dates = await context.PublicHolidays
            .AsNoTracking()
            .Where(h => h.CountryCode == code && h.Date >= startDate && h.Date <= endDate)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);

        return dates.Select(d => d.Date).ToHashSet();
    }

    private static async Task<int> GetLeaveYearStartMonthAsync(
        AppDbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        return settings?.LeaveYearStartMonth ?? 1;
    }

    private static async Task<bool> AffectsBalanceAsync(
        AppDbContext context, int? leaveTypeId, CancellationToken cancellationToken)
    {
        if (!leaveTypeId.HasValue)
            return false;
        return await context.LeaveTypes
            .AsNoTracking()
            .AnyAsync(lt => lt.Id == leaveTypeId.Value && lt.AffectsBalance, cancellationToken);
    }

    private static async Task<int> GetApprovedDaysForLeaveYearAsync(
        AppDbContext context,
        string employeeId,
        int leaveYearKey,
        int startMonth,
        string? excludeLeaveId,
        CancellationToken cancellationToken)
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

        if (approvedLeaves.Count == 0) return 0;

        var holidays = await GetHolidaySetAsync(context, lyStart, lyEnd, cancellationToken);
        return approvedLeaves.Sum(l => LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
            l.StartDate, l.EndDate, leaveYearKey, startMonth, holidays));
    }
}
