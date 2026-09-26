using Application.Attendance.Support;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Settings.Support;

/// <summary>
/// The organisation's working week as the Organization settings describe it:
/// which weekdays are worked (<see cref="AppSettings.WorkingDays"/>, plus
/// <see cref="AppSettings.WorkingDaysCustom"/> for a custom set), which dates are
/// public holidays (<see cref="AppSettings.HolidayCountryCode"/>), and what day it
/// is right now in <see cref="AppSettings.TimeZoneId"/>.
///
/// This is the one reading of "is today a working day" for the reminder
/// schedule and every reminder that asks. The dispatcher used to carry a private
/// copy read against the UTC calendar date, and the scheduler read none at all
/// (it fired every reminder every day on the server's own clock), so a
/// Saturday at UTC+3 got the pending-approvals digest and a public holiday got
/// the check-in reminder. Weekday tokens are the same three-letter ones the
/// settings page stores.
/// </summary>
public static class WorkingWeek
{
    // DayOfWeek is Sunday=0 .. Saturday=6 — index straight into this token table.
    private static readonly string[] DayTokens = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    /// <summary>The current instant on the org's wall clock.</summary>
    public static DateTime LocalNow(AppSettings? settings, DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(AttendanceDay.AsUtc(utcNow), WorkingDaySchedule.ResolveTimeZone(settings?.TimeZoneId));

    /// <summary>Today's date on the org's wall clock — the date a public holiday is a date on.</summary>
    public static DateOnly TodayLocal(AppSettings? settings, DateTime utcNow) =>
        DateOnly.FromDateTime(LocalNow(settings, utcNow));

    /// <summary>
    /// Whether <paramref name="day"/> is a worked weekday under the configured
    /// working-days preset. Public holidays are not consulted here; see
    /// <see cref="IsWorkingDayAsync"/>.
    /// </summary>
    public static bool IsConfiguredWorkingDay(AppSettings? settings, DayOfWeek day)
    {
        if (settings?.WorkingDays == "custom")
        {
            var working = (settings.WorkingDaysCustom ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant());
            return working.Contains(DayTokens[(int)day]);
        }

        return settings?.WorkingDays switch
        {
            "mon-sat" => day != DayOfWeek.Sunday,
            "sun-fri" => day != DayOfWeek.Saturday,
            _ => day != DayOfWeek.Saturday && day != DayOfWeek.Sunday, // mon-fri (default)
        };
    }

    /// <summary>
    /// Whether <paramref name="day"/> is a working day for the org: a configured
    /// weekday that is not in <paramref name="holidays"/>. The pure reading every
    /// range query uses, with the holidays loaded once by <see cref="LoadHolidaysAsync"/>.
    /// </summary>
    public static bool IsWorkingDay(AppSettings? settings, DateOnly day, IReadOnlySet<DateOnly> holidays) =>
        IsConfiguredWorkingDay(settings, day.DayOfWeek) && !holidays.Contains(day);

    /// <summary>
    /// Public holidays for the configured holiday country between
    /// <paramref name="from"/> and <paramref name="to"/>, inclusive. No holiday
    /// country means no holidays.
    /// </summary>
    public static async Task<HashSet<DateOnly>> LoadHolidaysAsync(
        AppDbContext context, AppSettings? settings, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var country = settings?.HolidayCountryCode;
        if (string.IsNullOrWhiteSpace(country)) return [];

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
        var dates = await context.PublicHolidays
            .Where(h => h.CountryCode == country && h.Date >= start && h.Date < end)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);

        return [.. dates.Select(DateOnly.FromDateTime)];
    }

    /// <summary>
    /// Whether <paramref name="day"/> is a working day for the org: a configured
    /// weekday that is not a public holiday for the configured holiday country.
    /// No holiday country means no holidays.
    /// </summary>
    public static async Task<bool> IsWorkingDayAsync(AppDbContext context, AppSettings? settings, DateOnly day, CancellationToken cancellationToken) =>
        IsConfiguredWorkingDay(settings, day.DayOfWeek)
        && IsWorkingDay(settings, day, await LoadHolidaysAsync(context, settings, day, day, cancellationToken));

    /// <summary>
    /// The first working day of the week <paramref name="today"/> falls in, looking
    /// from that week's Monday up to and including <paramref name="today"/>. Null
    /// when none of those days is a working day. A weekly reminder fires on this
    /// day, so a Monday bank holiday moves it to the Tuesday and a Tuesday–Saturday
    /// workspace gets it on the Tuesday. Weeks run Monday to Sunday, as the
    /// timesheet week does; a Sunday–Friday workspace therefore hears from a weekly
    /// reminder on its Monday, not its Sunday.
    /// </summary>
    public static async Task<DateOnly?> FirstWorkingDayOfWeekAsync(AppDbContext context, AppSettings? settings, DateOnly today, CancellationToken cancellationToken)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        for (var day = monday; day <= today; day = day.AddDays(1))
        {
            if (await IsWorkingDayAsync(context, settings, day, cancellationToken)) return day;
        }
        return null;
    }
}
