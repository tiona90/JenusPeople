using Application.Attendance.Support;
using Application.Settings.DTOs;
using Domain;

namespace Application.Reminders;

/// <summary>Why a reminder is, or is not, due on this tick.</summary>
public enum ReminderDueState
{
    Due,
    Disabled,
    InvalidTime,
    NotWorkingDay,
    NotFirstWorkingDayOfWeek,
    NotYet,
    AlreadyRanToday,
    TooLate,
}

/// <summary>
/// The pure part of the reminder schedule: given one reminder's settings and what
/// the org's calendar says about today, is it time to send it? The clock and the
/// calendar are inputs, so the rule is testable without a timer.
///
/// Every reminder — daily or weekly — is sent on working days only, as the
/// Organization settings' Working Week defines them (weekday preset plus public
/// holidays), and its time is read on the org's own clock
/// (<c>AppSettings.TimeZoneId</c>). A weekly reminder goes out on the first working
/// day of the week, so a Monday bank holiday moves it to Tuesday rather than
/// skipping the week. The scheduler used to fire on the server's local clock
/// every day of the year, and on Monday alone for weekly ones.
///
/// A reminder that missed its minute — the API was down or asleep — is caught up
/// later the same day, with one exception: a reminder whose point has passed is
/// not sent at all (<see cref="ReminderDueState.TooLate"/>, decided by
/// <see cref="CatchUpCutoff"/>). "Don't forget to check in" after the working day
/// has ended is noise, not a reminder.
/// </summary>
public static class ReminderSchedule
{
    public const string Weekly = "weekly";

    /// <param name="reminder">The reminder as configured.</param>
    /// <param name="localNow">The time of day on the org's clock.</param>
    /// <param name="today">Today's date on the org's clock.</param>
    /// <param name="todayIsWorkingDay">Whether <paramref name="today"/> is a working day for the org.</param>
    /// <param name="firstWorkingDayOfWeek">The first working day of this week, or null if there has been none yet.</param>
    /// <param name="lastRun">The local date the reminder was last handled, if it has been.</param>
    /// <param name="notAfter">
    /// The time of day after which a late send is pointless (<see cref="CatchUpCutoff"/>);
    /// null when the reminder is worth sending any time that day. It only ever
    /// applies to a catch-up: a reminder scheduled at or after its own cutoff is
    /// sent at its time as configured, not refused forever.
    /// </param>
    public static ReminderDueState Evaluate(
        ReminderSettingDto reminder,
        TimeOnly localNow,
        DateOnly today,
        bool todayIsWorkingDay,
        DateOnly? firstWorkingDayOfWeek,
        DateOnly? lastRun,
        TimeOnly? notAfter = null)
    {
        if (!reminder.Enabled) return ReminderDueState.Disabled;
        if (!TimeOnly.TryParse(reminder.Time, out var scheduled)) return ReminderDueState.InvalidTime;
        if (!todayIsWorkingDay) return ReminderDueState.NotWorkingDay;
        if (reminder.Frequency == Weekly && firstWorkingDayOfWeek != today) return ReminderDueState.NotFirstWorkingDayOfWeek;
        if (localNow < scheduled) return ReminderDueState.NotYet;
        if (lastRun == today) return ReminderDueState.AlreadyRanToday;
        if (notAfter is { } cutoff && scheduled < cutoff && localNow >= cutoff) return ReminderDueState.TooLate;
        return ReminderDueState.Due;
    }

    /// <summary>
    /// The time of day after which a missed send of this reminder is dropped rather
    /// than caught up. Only the check-in reminder has one — the end of the working
    /// day (<c>AppSettings.WorkingHoursEnd</c>): whoever has not checked in by then
    /// is not going to, and the email would arrive after they went home. Every
    /// other reminder is a digest or a nudge that is still worth reading later the
    /// same day (the attendance report, a manager's queue, "you are still checked
    /// in"), so it is sent whenever the API next gets to it.
    /// </summary>
    public static TimeOnly? CatchUpCutoff(string reminderId, AppSettings? settings) =>
        reminderId == ReminderDispatcher.CheckInReminder ? WorkingDaySchedule.From(settings).End : null;
}
