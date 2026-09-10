using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// The database reads both leave-balance calculators need: the configured leave-year
/// start month, and the public holidays covering a date range.
///
/// Extracted rather than copied. Two calculators enforce leave budgets now — the
/// pooled annual-leave balance and the per-child entitlement — and a second copy of
/// the holiday query would diverge the first time the country-code handling changes,
/// with the two answers differing by exactly the days that matter.
/// </summary>
internal static class LeaveYearQueries
{
    /// <summary>
    /// Public holidays for the configured country falling inside the range, as
    /// dates. Empty when no country is configured, which makes every weekday a
    /// business day.
    /// </summary>
    public static async Task<HashSet<DateTime>> GetHolidaySetAsync(
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

    /// <summary>The month a leave year starts in. 1 (January) when unset.</summary>
    public static async Task<int> GetLeaveYearStartMonthAsync(
        AppDbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        return settings?.LeaveYearStartMonth ?? 1;
    }
}
