using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

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

        // Entitlement of 0 means it has not been configured yet â€” skip the check.
        if (employeeProfile.AnnualLeaveEntitlement <= 0)
            return;

        var startMonth = await GetLeaveYearStartMonthAsync(context, cancellationToken);
        var holidays = await GetHolidaySetAsync(context, annualLeave.StartDate, annualLeave.EndDate, cancellationToken);

        foreach (var leaveYearKey in GetCoveredLeaveYears(annualLeave, startMonth))
        {
            var requestedDays = GetBusinessDaysInLeaveYear(annualLeave, leaveYearKey, startMonth, holidays);
            if (requestedDays <= 0)
                continue;

            var usedDays = await GetApprovedDaysForLeaveYearAsync(
                context, annualLeave.EmployeeId, leaveYearKey, startMonth, excludeLeaveId, cancellationToken);

            var remainingBalance = Math.Max(0, employeeProfile.AnnualLeaveEntitlement - usedDays);
            if (remainingBalance < requestedDays)
            {
                var (lyStart, lyEnd) = GetLeaveYearBounds(leaveYearKey, startMonth);
                throw new InvalidOperationException(
                    $"Insufficient leave balance for the leave year {lyStart:dd MMM yyyy} â€“ {lyEnd:dd MMM yyyy}. " +
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
        var currentLeaveYearKey = GetLeaveYearKey(DateTime.UtcNow, startMonth);

        var usedDays = await GetApprovedDaysForLeaveYearAsync(
            context, employeeProfile.UserId, currentLeaveYearKey, startMonth,
            excludeLeaveId: null, cancellationToken);

        employeeProfile.LeaveBalance = Math.Max(0, employeeProfile.AnnualLeaveEntitlement - usedDays);
    }

    // â”€â”€ Leave year helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Returns the start year of the leave year that contains <paramref name="date"/>.
    /// E.g. if startMonth=4 (April): Janâ€“Mar 2026 â†’ key 2025; Aprâ€“Dec 2026 â†’ key 2026.
    /// </summary>
    private static int GetLeaveYearKey(DateTime date, int startMonth) =>
        date.Month >= startMonth ? date.Year : date.Year - 1;

    private static (DateTime Start, DateTime End) GetLeaveYearBounds(int leaveYearKey, int startMonth)
    {
        var start = new DateTime(leaveYearKey, startMonth, 1);
        var end = start.AddYears(1).AddDays(-1);
        return (start, end);
    }

    private static IEnumerable<int> GetCoveredLeaveYears(AnnualLeave annualLeave, int startMonth)
    {
        var startKey = GetLeaveYearKey(annualLeave.StartDate, startMonth);
        var endKey = GetLeaveYearKey(annualLeave.EndDate, startMonth);
        for (var key = startKey; key <= endKey; key++)
            yield return key;
    }

    private static int GetBusinessDaysInLeaveYear(AnnualLeave annualLeave, int leaveYearKey, int startMonth, HashSet<DateTime>? holidays = null)
    {
        var (lyStart, lyEnd) = GetLeaveYearBounds(leaveYearKey, startMonth);

        var start = annualLeave.StartDate.Date > lyStart ? annualLeave.StartDate.Date : lyStart;
        var end = annualLeave.EndDate.Date < lyEnd ? annualLeave.EndDate.Date : lyEnd;

        if (end < start)
            return 0;

        var days = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (holidays is not null && holidays.Contains(date)) continue;
            days++;
        }
        return days;
    }

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

    // â”€â”€ DB helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        var (lyStart, lyEnd) = GetLeaveYearBounds(leaveYearKey, startMonth);

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
        return approvedLeaves.Sum(l => GetBusinessDaysInLeaveYear(l, leaveYearKey, startMonth, holidays));
    }
}
