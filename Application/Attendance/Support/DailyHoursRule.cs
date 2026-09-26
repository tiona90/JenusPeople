using Domain.Services;

namespace Application.Attendance.Support;

/// <summary>What a calendar day is, for the purpose of asking how long it should have been.</summary>
public enum DayKind { Working, NonWorking, Leave }

/// <summary>How much of a day approved leave covers.</summary>
public enum LeaveOnDay { None, HalfDay, Full }

/// <param name="TargetMinutes">The minutes the day asks for; null unless <see cref="DayKind.Working"/> with a schedule.</param>
/// <param name="ShortByMinutes">Minutes under the target, beyond the grace; null when there is nothing to say.</param>
public sealed record DailyHoursVerdict(DayKind Kind, int? TargetMinutes, int? ShortByMinutes);

/// <summary>
/// The one rule for a short day and for a timesheet that disagrees with
/// attendance. The target is the working day net of the configured break
/// (<see cref="WorkingDaySchedule.ScheduledMinutes"/>); approved leave removes it,
/// or halves it for a half day; a non-working day has none. A day still open says
/// nothing, the same reading as <see cref="WorkingDaySchedule.BreakVariance"/>:
/// the afternoon may still come. Pure, so every caller loads its own inputs
/// (usually through <see cref="DailyHoursContext"/>) and they all agree.
/// </summary>
public static class DailyHoursRule
{
    /// <summary>
    /// Leaving two minutes early is not news. Shared by the short-day and the
    /// mismatch checks, so a day within the grace of its target is also within the
    /// grace of a timesheet that logs the full target.
    /// </summary>
    public const int GraceMinutes = 15;

    public static DailyHoursVerdict Judge(
        bool isWorkingDay, LeaveOnDay leave, int scheduledMinutes, int workedMinutes, bool dayClosed)
    {
        if (leave == LeaveOnDay.Full) return new(DayKind.Leave, null, null);
        if (!isWorkingDay) return new(DayKind.NonWorking, null, null);
        if (scheduledMinutes <= 0) return new(DayKind.Working, null, null);

        var target = leave == LeaveOnDay.HalfDay ? scheduledMinutes / 2 : scheduledMinutes;
        var shortBy = target - workedMinutes;
        return new(DayKind.Working, target, dayClosed && shortBy > GraceMinutes ? shortBy : null);
    }

    /// <summary>
    /// Logged minus attended, in minutes, when the two differ by more than the
    /// grace; null otherwise and always null on a leave day. A non-working day is
    /// still compared: weekend work logged but never clocked is exactly the kind
    /// of disagreement a reviewer wants to see.
    /// </summary>
    public static int? MismatchMinutes(DayKind kind, decimal loggedHours, int attendedMinutes)
    {
        if (kind == DayKind.Leave) return null;
        var logged = (int)Math.Round(loggedHours * 60m, MidpointRounding.AwayFromZero);
        var diff = logged - attendedMinutes;
        return Math.Abs(diff) > GraceMinutes ? diff : null;
    }

    /// <summary>The approved leave rows covering one date, folded into one reading. Two halves are a whole.</summary>
    public static LeaveOnDay CombineLeave(IEnumerable<LeaveDuration> durations)
    {
        var halves = 0;
        foreach (var duration in durations)
        {
            if (duration == LeaveDuration.Full) return LeaveOnDay.Full;
            halves++;
        }
        return halves switch { 0 => LeaveOnDay.None, 1 => LeaveOnDay.HalfDay, _ => LeaveOnDay.Full };
    }

    /// <summary>"45 min", "1h", "1h 30m" — the wording the client's formatBreakMinutes uses.</summary>
    public static string Describe(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0) return $"{rest} min";
        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }
}
