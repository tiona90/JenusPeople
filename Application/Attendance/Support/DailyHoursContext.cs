using Application.Settings.Support;
using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Attendance.Support;

/// <summary>
/// Everything <see cref="DailyHoursRule"/> needs for a set of people over a date
/// range, loaded in four queries: the settings row, the public holidays in the
/// range, the approved leave overlapping it, and each person's employment start
/// date and whether their account is active. Leave is matched on
/// <see cref="AnnualLeave.EmployeeProfileId"/>, the key attendance is recorded
/// against (see <see cref="AttendanceDay.LoadOnLeaveProfileIdsAsync"/>).
/// </summary>
public sealed class DailyHoursContext
{
    private readonly AppSettings? _settings;
    private readonly IReadOnlySet<DateOnly> _holidays;
    private readonly Dictionary<string, List<(DateOnly Start, DateOnly End, LeaveDuration Duration)>> _leaves;
    private readonly Dictionary<string, (DateOnly? StartDate, bool IsActive)> _people;

    private DailyHoursContext(
        AppSettings? settings,
        IReadOnlySet<DateOnly> holidays,
        Dictionary<string, List<(DateOnly, DateOnly, LeaveDuration)>> leaves,
        Dictionary<string, (DateOnly?, bool)> people,
        DateOnly todayUtc)
    {
        _settings = settings;
        _holidays = holidays;
        _leaves = leaves;
        _people = people;
        Schedule = WorkingDaySchedule.From(settings);
        TodayUtc = todayUtc;
    }

    public WorkingDaySchedule Schedule { get; }
    public int ScheduledMinutes => Schedule.ScheduledMinutes;

    /// <summary>The UTC calendar date of the clock the context was loaded at.</summary>
    public DateOnly TodayUtc { get; }

    public static async Task<DailyHoursContext> LoadAsync(
        AppDbContext context,
        IReadOnlyCollection<string> employeeProfileIds,
        DateOnly from,
        DateOnly to,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var holidays = await WorkingWeek.LoadHolidaysAsync(context, settings, from, to, cancellationToken);

        var start = from.ToDateTime(TimeOnly.MinValue);
        var endExclusive = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
        var ids = employeeProfileIds.ToList();

        var rows = await context.AnnualLeaves.AsNoTracking()
            .Where(l => l.EmployeeProfileId != null
                && ids.Contains(l.EmployeeProfileId)
                && l.Status == AnnualLeaveStatus.Approved
                && l.StartDate < endExclusive && l.EndDate >= start)
            .Select(l => new { ProfileId = l.EmployeeProfileId!, l.StartDate, l.EndDate, l.Duration })
            .ToListAsync(cancellationToken);

        var leaves = rows
            .GroupBy(r => r.ProfileId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => (DateOnly.FromDateTime(r.StartDate), DateOnly.FromDateTime(r.EndDate), r.Duration)).ToList());

        var people = await context.EmployeeProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.EmploymentStartDate, IsActive = p.User == null || p.User.IsActive })
            .ToDictionaryAsync(p => p.Id, p => (p.EmploymentStartDate, p.IsActive), cancellationToken);

        return new DailyHoursContext(settings, holidays, leaves, people, DateOnly.FromDateTime(AttendanceDay.UtcDayStart(nowUtc)));
    }

    public bool IsWorkingDay(DateOnly day) => WorkingWeek.IsWorkingDay(_settings, day, _holidays);

    public LeaveOnDay LeaveOn(string employeeProfileId, DateOnly day) =>
        _leaves.TryGetValue(employeeProfileId, out var rows)
            ? DailyHoursRule.CombineLeave(rows.Where(r => r.Start <= day && r.End >= day).Select(r => r.Duration))
            : LeaveOnDay.None;

    /// <summary>A past date is over; today is over once checked out of; a future date is not.</summary>
    public bool IsClosed(DateOnly day, AttendanceDayState? state) =>
        day < TodayUtc || (day == TodayUtc && state?.Status == AttendanceDayStatus.Done);

    /// <summary>
    /// Whether the day asks anything of this person at all. A deactivated account is
    /// a leaver, who records nothing and would read as short by the whole day every
    /// day; a day before the employment start date was not theirs to work. A null
    /// start date is no restriction, the same reading as <c>MinimumServiceRule</c>:
    /// nobody entered it, which is not "started today". A profile the context did
    /// not load is judged as usual.
    /// </summary>
    private bool IsExpectedToWork(string employeeProfileId, DateOnly day) =>
        !_people.TryGetValue(employeeProfileId, out var person)
        || (person.IsActive && (person.StartDate is not { } start || day >= start));

    /// <summary>
    /// The day's verdict. Somebody not expected to work that day gets its kind and
    /// nothing else — no target, never short — so the boards keep listing them but
    /// call nothing. The timesheet comparison reads only the kind, so it is unaffected.
    /// </summary>
    public DailyHoursVerdict Judge(string employeeProfileId, DateOnly day, AttendanceDayState? state)
    {
        var verdict = DailyHoursRule.Judge(
            IsWorkingDay(day),
            LeaveOn(employeeProfileId, day),
            ScheduledMinutes,
            state?.WorkedMinutes ?? 0,
            IsClosed(day, state));
        return IsExpectedToWork(employeeProfileId, day) ? verdict : verdict with { TargetMinutes = null, ShortByMinutes = null };
    }
}
