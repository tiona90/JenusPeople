using Application.Attendance.DTOs;
using Domain;
using Persistence;

namespace Application.Attendance.Support;

/// <summary>
/// Who was short on the previous working day, for the dashboards. Today is not
/// judged — it is not over — so the line always speaks about a finished day: on a
/// Monday that is Friday, after a Tuesday holiday it is Monday.
/// </summary>
public static class ShortDayDigest
{
    /// <summary>How far back a previous working day is looked for. The loaded context must cover it.</summary>
    public const int LookBackDays = 14;

    public sealed record Digest(DateOnly Day, List<ShortDayDto> People);

    public static DateOnly? PreviousWorkingDay(DailyHoursContext hours, DateOnly today)
    {
        for (var back = 1; back <= LookBackDays; back++)
        {
            var candidate = today.AddDays(-back);
            if (hours.IsWorkingDay(candidate)) return candidate;
        }
        return null;
    }

    public static async Task<Digest?> BuildAsync(
        AppDbContext context,
        IReadOnlyList<EmployeeProfile> profiles,
        DailyHoursContext hours,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (PreviousWorkingDay(hours, hours.TodayUtc) is not { } day) return null;

        var dayStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ids = profiles.Select(p => p.Id).ToList();
        var byEmployee = await AttendanceDay.LoadDayEventsByEmployeeAsync(context, ids, dayStart, cancellationToken);

        var people = new List<ShortDayDto>();
        foreach (var profile in profiles)
        {
            var state = AttendanceDay.StateFor(byEmployee, profile.Id, nowUtc);
            if (hours.Judge(profile.Id, day, state).ShortByMinutes is not { } shortBy) continue;

            people.Add(new ShortDayDto(
                profile.Id,
                AttendanceDay.DisplayNameOf(profile),
                profile.Department?.Name ?? "Unassigned",
                state.WorkedMinutes,
                shortBy));
        }

        return new Digest(day, people);
    }
}
