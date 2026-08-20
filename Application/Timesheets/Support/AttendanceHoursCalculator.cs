using Domain;

namespace Application.Timesheets.Support;

// Pure calculator that turns a sequence of AttendanceEvents into worked
// hours per UTC day. Mirrors the pairing logic in AttendanceController's
// ComputeDayState — kept separate here so it can return decimal hours
// (instead of integer minutes) and so the generate-draft flow can be unit
// tested without touching EF.
public static class AttendanceHoursCalculator
{
    public sealed record DailyHours(DateTime Date, decimal Hours);

    public static List<DailyHours> Calculate(IEnumerable<AttendanceEvent> events)
    {
        // Group by UTC day-of-event so cross-midnight sessions get attributed
        // to the day the work started in — same convention as AttendanceController.
        var byDay = events
            .GroupBy(e => UtcDayStart(e.At))
            .OrderBy(g => g.Key);

        var result = new List<DailyHours>();
        foreach (var day in byDay)
        {
            var hours = HoursForDay(day);
            if (hours > 0m)
            {
                result.Add(new DailyHours(day.Key, hours));
            }
        }
        return result;
    }

    private static decimal HoursForDay(IEnumerable<AttendanceEvent> dayEvents)
    {
        var ordered = dayEvents.OrderBy(e => e.At).ToList();

        DateTime? checkInAt = null;
        DateTime? breakStartAt = null;
        decimal breakSecondsInSession = 0m;
        decimal workedSeconds = 0m;

        foreach (var e in ordered)
        {
            switch (e.Type)
            {
                case AttendanceEventType.CheckIn when checkInAt is null:
                    checkInAt = e.At;
                    breakSecondsInSession = 0m;
                    breakStartAt = null;
                    break;

                case AttendanceEventType.CheckOut when checkInAt.HasValue:
                    // Close any still-open break against the checkout time.
                    if (breakStartAt.HasValue)
                    {
                        breakSecondsInSession += (decimal)(e.At - breakStartAt.Value).TotalSeconds;
                        breakStartAt = null;
                    }
                    var sessionSeconds = (decimal)(e.At - checkInAt.Value).TotalSeconds;
                    workedSeconds += Math.Max(0m, sessionSeconds - breakSecondsInSession);
                    checkInAt = null;
                    breakSecondsInSession = 0m;
                    break;

                case AttendanceEventType.BreakStart when checkInAt.HasValue && breakStartAt is null:
                    breakStartAt = e.At;
                    break;

                case AttendanceEventType.BreakEnd when breakStartAt.HasValue:
                    breakSecondsInSession += (decimal)(e.At - breakStartAt.Value).TotalSeconds;
                    breakStartAt = null;
                    break;
            }
        }

        // Unclosed sessions (no matching CheckOut) are intentionally dropped —
        // we can't trust a duration without an explicit close. The user can
        // edit the generated draft to add it if needed.
        return Math.Round(workedSeconds / 3600m, 2, MidpointRounding.AwayFromZero);
    }

    private static DateTime UtcDayStart(DateTime at) =>
        new(at.Year, at.Month, at.Day, 0, 0, 0, DateTimeKind.Utc);
}
