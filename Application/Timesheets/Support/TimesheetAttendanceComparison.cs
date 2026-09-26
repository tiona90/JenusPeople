using Application.Attendance.Support;
using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Support;

/// <summary>
/// Each weekday of a timesheet against the time attendance recorded for it, judged
/// by <see cref="DailyHoursRule.MismatchMinutes"/>. Only sheets whose week starts
/// within <see cref="WindowWeeks"/> are compared: the list endpoint is unpaged by
/// default, and loading every attendance event behind every sheet ever filed would
/// make it pay for history nobody is reviewing any more.
/// </summary>
public sealed class TimesheetAttendanceComparison
{
    public const int WindowWeeks = 12;

    public sealed record Week(List<int> AttendanceMinutes, List<int?> MismatchMinutes, List<bool> OnLeave, int MismatchDayCount);

    private readonly Dictionary<string, Week> _byTimesheetId;

    private TimesheetAttendanceComparison(Dictionary<string, Week> byTimesheetId) => _byTimesheetId = byTimesheetId;

    /// <summary>No sheet compared: what a list that did not ask for the comparison gets.</summary>
    public static TimesheetAttendanceComparison None { get; } = new([]);

    public Week? For(string timesheetId) => _byTimesheetId.GetValueOrDefault(timesheetId);

    public static async Task<TimesheetAttendanceComparison> LoadAsync(
        AppDbContext context, IReadOnlyList<Timesheet> timesheets, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = AttendanceDay.UtcDayStart(nowUtc).AddDays(-7 * WindowWeeks);
        var inWindow = timesheets.Where(t => AttendanceDay.UtcDayStart(t.PeriodStart) >= cutoff).ToList();
        if (inWindow.Count == 0) return new([]);

        var profileIds = inWindow.Select(t => t.EmployeeProfileId).Distinct().ToList();
        var from = inWindow.Min(t => AttendanceDay.UtcDayStart(t.PeriodStart));
        var toExclusive = inWindow.Max(t => AttendanceDay.UtcDayStart(t.PeriodStart)).AddDays(5);

        // One events query per week that has sheets, covering only the people with a
        // sheet that week — at most WindowWeeks queries. A single query from the
        // earliest week to the latest would pull twelve weeks of events for someone
        // whose only sheet is this week's.
        var events = new List<AttendanceEvent>();
        foreach (var week in inWindow.GroupBy(t => AttendanceDay.UtcDayStart(t.PeriodStart)))
        {
            var weekIds = week.Select(t => t.EmployeeProfileId).Distinct().ToList();
            var weekEnd = week.Key.AddDays(5);
            events.AddRange(await context.AttendanceEvents.AsNoTracking()
                .Where(e => weekIds.Contains(e.EmployeeProfileId) && e.At >= week.Key && e.At < weekEnd)
                .ToListAsync(cancellationToken));
        }
        var byDay = events
            .GroupBy(e => (e.EmployeeProfileId, Day: AttendanceDay.UtcDayStart(e.At)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var hours = await DailyHoursContext.LoadAsync(
            context, profileIds, DateOnly.FromDateTime(from), DateOnly.FromDateTime(toExclusive.AddDays(-1)), nowUtc, cancellationToken);

        var result = new Dictionary<string, Week>();
        foreach (var sheet in inWindow)
        {
            var monday = AttendanceDay.UtcDayStart(sheet.PeriodStart);
            var attended = new List<int>(5);
            var mismatch = new List<int?>(5);
            var onLeave = new List<bool>(5);

            for (var i = 0; i < 5; i++)
            {
                var dayStart = monday.AddDays(i);
                var day = DateOnly.FromDateTime(dayStart);
                byDay.TryGetValue((sheet.EmployeeProfileId, dayStart), out var dayEvents);
                var state = AttendanceDayStateCalculator.Calculate(dayEvents ?? [], nowUtc);
                var verdict = hours.Judge(sheet.EmployeeProfileId, day, state);
                // Entry dates are calendar dates stored at midnight with no zone.
                var logged = sheet.Entries.Where(e => e.Date.Date == dayStart.Date).Sum(e => e.HoursWorked);

                attended.Add(state.WorkedMinutes);
                onLeave.Add(verdict.Kind == DayKind.Leave);
                mismatch.Add(hours.IsClosed(day, state)
                    ? DailyHoursRule.MismatchMinutes(verdict.Kind, logged, state.WorkedMinutes)
                    : null);
            }

            result[sheet.Id] = new Week(attended, mismatch, onLeave, mismatch.Count(m => m is not null));
        }

        return new(result);
    }
}
