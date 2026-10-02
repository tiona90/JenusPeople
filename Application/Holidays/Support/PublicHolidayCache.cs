using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Holidays.Support;

/// <summary>
/// Fills the <see cref="AppDbContext.PublicHolidays"/> cache for one country and
/// year from date.nager.at when it holds nothing for them yet.
///
/// The table is the only calendar every working-day rule reads
/// (<c>WorkingWeek.LoadHolidaysAsync</c>), but it used to be filled only by
/// <see cref="Queries.GetHolidays"/> — that is, when somebody happened to open a
/// page asking for the year's holidays. The reminder scheduler never does, and
/// saving a new holiday country empties the whole table, so a host nobody had
/// browsed since read a public holiday as an ordinary weekday and sent every
/// check-in, check-out and timesheet reminder on it. Callers that decide
/// something from the calendar call this first.
/// </summary>
public static class PublicHolidayCache
{
    /// <summary>
    /// Ensures <paramref name="year"/>'s holidays for <paramref name="countryCode"/>
    /// are cached. A year already holding any row is left alone; a country the
    /// provider has nothing for stores nothing. Network failures surface as
    /// <see cref="HttpRequestException"/>, so the caller chooses whether to fail.
    /// Returns whether the year is cached afterwards — false when the provider
    /// answered with nothing, which a caller on a timer should not re-ask every tick.
    /// </summary>
    public static async Task<bool> EnsureYearAsync(
        AppDbContext context, NagerHolidayClient client, string countryCode, int year, CancellationToken cancellationToken)
    {
        var code = countryCode.Trim().ToUpperInvariant();
        if (await context.PublicHolidays.AnyAsync(h => h.CountryCode == code && h.Year == year, cancellationToken))
            return true;

        var fetched = await client.GetPublicHolidaysAsync(year, code, cancellationToken);
        if (fetched.Count == 0) return false;

        var existingDates = await context.PublicHolidays
            .Where(h => h.CountryCode == code)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);
        var existingSet = existingDates.Select(d => d.Date).ToHashSet();

        var entities = fetched
            .GroupBy(h => h.Date.Date)
            .Where(g => !existingSet.Contains(g.Key))
            .Select(g => g.First())
            .Select(h => new PublicHoliday
            {
                CountryCode = code,
                Year = year,
                Date = h.Date.Date,
                LocalName = h.LocalName,
                EnglishName = h.EnglishName,
                CachedAt = DateTime.UtcNow,
            }).ToList();

        // Every date already held under another year's rows: nothing to add, and
        // nothing for this year either.
        if (entities.Count == 0) return false;
        context.PublicHolidays.AddRange(entities);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
