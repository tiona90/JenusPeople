namespace Domain.Services;

/// <summary>
/// Where an employee stands on a single day, derived from that day's events.
/// </summary>
public enum AttendanceDayStatus
{
    /// <summary>No check-in on this day.</summary>
    Out = 0,

    /// <summary>Checked in and still working.</summary>
    In = 1,

    /// <summary>Checked in, currently on an unfinished break.</summary>
    Break = 2,

    /// <summary>Checked in and checked out.</summary>
    Done = 3,
}

/// <summary>
/// The outcome of replaying one day's attendance events.
/// </summary>
/// <param name="CheckInAt">First check-in of the day, or null if there was none.</param>
/// <param name="CheckOutAt">
/// Last check-out of the day. Can be set while <see cref="Status"/> is
/// <see cref="AttendanceDayStatus.Out"/>: a shift that began the previous evening
/// puts its check-out in this day's events with no matching check-in.
/// </param>
/// <param name="OnBreakSince">
/// Start of a break that has not ended, or null. Only set when
/// <see cref="Status"/> is <see cref="AttendanceDayStatus.Break"/>.
/// </param>
/// <param name="TotalBreakMinutes">
/// Completed break time. A break still running is excluded here — it is already
/// reflected in <see cref="WorkedMinutes"/>, and its length is not final.
/// </param>
/// <param name="WorkedMinutes">
/// Time between check-in and check-out (or now, while still checked in), less all
/// break time including a break still running. Never negative.
/// </param>
public sealed record AttendanceDayState(
    AttendanceDayStatus Status,
    DateTime? CheckInAt,
    DateTime? CheckOutAt,
    DateTime? OnBreakSince,
    int TotalBreakMinutes,
    int WorkedMinutes);

/// <summary>
/// Single source of truth for turning a day's raw <see cref="AttendanceEvent"/>
/// rows into a day state. Pure: no DbContext, no ambient clock — the caller
/// supplies both the events and "now", which is what makes every branch here
/// reachable from a test.
///
/// This lived as a private method on AttendanceController, where the only way to
/// reach it was through an HTTP endpoint and a database, so none of its edge
/// cases — an open break, a missing check-out, repeated check-ins, a shift
/// crossing midnight — were covered by anything.
///
/// Callers bucket events into UTC days before calling in. That boundary is what
/// makes overnight shifts asymmetric, and the rules below are written to degrade
/// predictably rather than to reconstruct the shift: this calculator only ever
/// sees one day at a time.
/// </summary>
public static class AttendanceDayStateCalculator
{
    /// <summary>
    /// Replays <paramref name="dayEvents"/> in chronological order and reports the
    /// resulting state. Events need not arrive sorted.
    ///
    /// The rules, each of which exists because the raw event stream is not
    /// guaranteed to be well-formed:
    ///
    /// <list type="bullet">
    ///   <item>The <b>first</b> check-in of the day wins; later ones are ignored,
    ///     so a double-tap on the button cannot shorten the day.</item>
    ///   <item>The <b>last</b> check-out wins.</item>
    ///   <item>A break only opens while checked in and not yet checked out, and
    ///     only one can be open at a time — a stray break-start is ignored.</item>
    ///   <item>A break-end with no open break is ignored.</item>
    ///   <item>Checking out closes an open break, so forgetting to end a break
    ///     does not silently bill it as work.</item>
    /// </list>
    /// </summary>
    /// <param name="dayEvents">One UTC day's events for one employee.</param>
    /// <param name="nowUtc">
    /// The current instant, used to measure work and break time that is still
    /// running. Only consulted while the day is open.
    /// </param>
    public static AttendanceDayState Calculate(IEnumerable<AttendanceEvent> dayEvents, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(dayEvents);

        var ordered = dayEvents.OrderBy(e => e.At).ToList();

        DateTime? checkIn = null;
        DateTime? checkOut = null;
        DateTime? breakStart = null;
        var totalBreakSeconds = 0;

        foreach (var e in ordered)
        {
            switch (e.Type)
            {
                case AttendanceEventType.CheckIn:
                    checkIn ??= e.At;
                    break;

                case AttendanceEventType.CheckOut:
                    checkOut = e.At;
                    if (breakStart is not null)
                    {
                        totalBreakSeconds += (int)(e.At - breakStart.Value).TotalSeconds;
                        breakStart = null;
                    }
                    break;

                case AttendanceEventType.BreakStart:
                    if (checkIn is not null && checkOut is null && breakStart is null)
                    {
                        breakStart = e.At;
                    }
                    break;

                case AttendanceEventType.BreakEnd:
                    if (breakStart is not null)
                    {
                        totalBreakSeconds += (int)(e.At - breakStart.Value).TotalSeconds;
                        breakStart = null;
                    }
                    break;
            }
        }

        var status = checkIn is null ? AttendanceDayStatus.Out
            : checkOut is not null ? AttendanceDayStatus.Done
            : breakStart is not null ? AttendanceDayStatus.Break
            : AttendanceDayStatus.In;

        var workedMinutes = 0;
        if (checkIn is not null)
        {
            var end = checkOut ?? nowUtc;
            var totalSeconds = (int)(end - checkIn.Value).TotalSeconds;

            // A break that never ended still has to come off the total, and its
            // length is only known relative to now.
            var openBreakSeconds = breakStart is not null && checkOut is null
                ? (int)(nowUtc - breakStart.Value).TotalSeconds
                : 0;

            // Clamped: a check-out timestamped before its check-in would
            // otherwise report negative work.
            workedMinutes = Math.Max(0, (totalSeconds - totalBreakSeconds - openBreakSeconds) / 60);
        }

        return new AttendanceDayState(
            status,
            checkIn,
            checkOut,
            breakStart,
            totalBreakSeconds / 60,
            workedMinutes);
    }
}
