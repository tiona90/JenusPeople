namespace Domain.Services;

/// <summary>
/// Single source of truth for leave duration and balance arithmetic.
/// All members are pure: no DbContext, no clock, no I/O. Application
/// services are responsible for loading the inputs (holiday set, leave year
/// configuration, prior approved leaves) and calling into this service.
///
/// Keeping the rules in the Domain layer means a refactor of the data
/// access tier — or a future read-side projection — cannot accidentally
/// diverge from the canonical business-day calculation.
/// </summary>
public static class LeaveCalculationService
{
    /// <summary>
    /// Counts business days between <paramref name="start"/> and
    /// <paramref name="end"/> (both inclusive), skipping Saturdays, Sundays,
    /// and any date present in <paramref name="holidays"/>.
    /// </summary>
    public static int CalculateBusinessDays(
        DateTime start,
        DateTime end,
        IReadOnlySet<DateTime>? holidays = null)
    {
        start = start.Date;
        end = end.Date;
        if (end < start) return 0;

        var days = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (IsBusinessDay(date, holidays)) days++;
        }
        return days;
    }

    /// <summary>
    /// Chargeable days for a leave request. A half-day request consumes
    /// half of one business day regardless of the calendar span — the caller
    /// is expected to enforce that half-day windows are single-day requests.
    /// </summary>
    public static decimal CalculateChargeableDays(
        DateTime start,
        DateTime end,
        LeaveDuration duration,
        IReadOnlySet<DateTime>? holidays = null)
    {
        var businessDays = CalculateBusinessDays(start, end, holidays);
        return duration == LeaveDuration.HalfDay && businessDays > 0
            ? businessDays * 0.5m
            : businessDays;
    }

    /// <summary>
    /// Returns the start-year key of the leave year that contains <paramref name="date"/>.
    /// Example: if leave years run April→March, a date in February 2026 returns 2025.
    /// </summary>
    public static int GetLeaveYearKey(DateTime date, int startMonth)
        => date.Month >= startMonth ? date.Year : date.Year - 1;

    /// <summary>
    /// Inclusive start/end dates for the leave year identified by <paramref name="leaveYearKey"/>.
    /// </summary>
    public static (DateTime Start, DateTime End) GetLeaveYearBounds(int leaveYearKey, int startMonth)
    {
        var start = new DateTime(leaveYearKey, startMonth, 1);
        var end = start.AddYears(1).AddDays(-1);
        return (start, end);
    }

    /// <summary>
    /// The set of leave-year keys touched by a leave that spans
    /// <paramref name="leaveStart"/>..<paramref name="leaveEnd"/>. Usually one,
    /// two if the request crosses the leave-year boundary.
    /// </summary>
    public static IEnumerable<int> GetCoveredLeaveYears(
        DateTime leaveStart,
        DateTime leaveEnd,
        int startMonth)
    {
        var startKey = GetLeaveYearKey(leaveStart, startMonth);
        var endKey = GetLeaveYearKey(leaveEnd, startMonth);
        for (var key = startKey; key <= endKey; key++)
            yield return key;
    }

    /// <summary>
    /// Business days from a leave request that fall inside a specific leave year.
    /// Clips the request's range to the leave-year window, then runs the standard
    /// business-day count over the intersection.
    /// </summary>
    public static int CalculateBusinessDaysInLeaveYear(
        DateTime leaveStart,
        DateTime leaveEnd,
        int leaveYearKey,
        int startMonth,
        IReadOnlySet<DateTime>? holidays = null)
    {
        var (lyStart, lyEnd) = GetLeaveYearBounds(leaveYearKey, startMonth);
        var clippedStart = leaveStart.Date > lyStart ? leaveStart.Date : lyStart;
        var clippedEnd = leaveEnd.Date < lyEnd ? leaveEnd.Date : lyEnd;
        return CalculateBusinessDays(clippedStart, clippedEnd, holidays);
    }

    /// <summary>
    /// Remaining balance, floored at zero. Used when an employee's entitlement
    /// is less than days already taken (e.g. mid-year hire adjustments).
    /// </summary>
    public static int CalculateRemainingBalance(int entitlement, int usedDays)
        => Math.Max(0, entitlement - usedDays);

    private static bool IsBusinessDay(DateTime date, IReadOnlySet<DateTime>? holidays)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (holidays is not null && holidays.Contains(date.Date)) return false;
        return true;
    }
}

public enum LeaveDuration
{
    Full,
    HalfDay,
}
