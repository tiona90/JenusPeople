using Domain;
using Domain.Services;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// ComputeDayState was a private method on AttendanceController, reachable only
/// through an HTTP endpoint and a database, and so covered by nothing. It decides
/// worked hours, lateness, presence and the whole company dashboard, and the raw
/// event stream it reads is not guaranteed to be well-formed: breaks are left
/// open, check-outs go missing, buttons get double-tapped, and shifts cross
/// midnight into a day bucket that holds only half of them.
///
/// These tests pin each of those. "Now" is injected, so the clock-dependent
/// branches — an open break, a day still running — are deterministic.
/// </summary>
public class AttendanceDayStateCalculatorTests
{
    private static readonly DateTime Day = new(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>10:30 on the day under test — a fixed "now" for open-ended days.</summary>
    private static DateTime Now => At(10, 30);

    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    private static AttendanceEvent Event(AttendanceEventType type, DateTime at) =>
        new() { Id = Guid.NewGuid().ToString(), EmployeeProfileId = "p1", At = at, Type = type };

    private static AttendanceEvent CheckIn(DateTime at) => Event(AttendanceEventType.CheckIn, at);
    private static AttendanceEvent CheckOut(DateTime at) => Event(AttendanceEventType.CheckOut, at);
    private static AttendanceEvent BreakStart(DateTime at) => Event(AttendanceEventType.BreakStart, at);
    private static AttendanceEvent BreakEnd(DateTime at) => Event(AttendanceEventType.BreakEnd, at);

    private static AttendanceDayState Calculate(params AttendanceEvent[] events) =>
        AttendanceDayStateCalculator.Calculate(events, Now);

    [Fact]
    public void A_day_with_no_events_is_out()
    {
        var state = Calculate();

        Assert.Equal(AttendanceDayStatus.Out, state.Status);
        Assert.Null(state.CheckInAt);
        Assert.Null(state.CheckOutAt);
        Assert.Null(state.OnBreakSince);
        Assert.Equal(0, state.TotalBreakMinutes);
        Assert.Equal(0, state.WorkedMinutes);
    }

    [Fact]
    public void A_completed_day_reports_worked_time_net_of_breaks()
    {
        var state = Calculate(
            CheckIn(At(9)),
            BreakStart(At(12)),
            BreakEnd(At(12, 30)),
            CheckOut(At(17)));

        Assert.Equal(AttendanceDayStatus.Done, state.Status);
        Assert.Equal(At(9), state.CheckInAt);
        Assert.Equal(At(17), state.CheckOutAt);
        Assert.Null(state.OnBreakSince);
        Assert.Equal(30, state.TotalBreakMinutes);
        Assert.Equal(450, state.WorkedMinutes); // 8h elapsed − 30m break
    }

    [Fact]
    public void Events_do_not_have_to_arrive_in_order()
    {
        var inOrder = Calculate(CheckIn(At(9)), BreakStart(At(12)), BreakEnd(At(12, 30)), CheckOut(At(17)));
        var shuffled = Calculate(CheckOut(At(17)), BreakEnd(At(12, 30)), CheckIn(At(9)), BreakStart(At(12)));

        Assert.Equal(inOrder, shuffled);
    }

    /* ── open break ──────────────────────────────────────────────────────── */

    /// <summary>
    /// A break still running counts against worked time from the moment it
    /// started, but is deliberately absent from TotalBreakMinutes: that figure
    /// reports completed breaks, and this one's length is not final.
    /// </summary>
    [Fact]
    public void An_open_break_is_deducted_from_work_but_not_yet_totalled()
    {
        var state = Calculate(CheckIn(At(9)), BreakStart(At(10)));

        Assert.Equal(AttendanceDayStatus.Break, state.Status);
        Assert.Equal(At(10), state.OnBreakSince);
        Assert.Equal(0, state.TotalBreakMinutes);
        Assert.Equal(60, state.WorkedMinutes); // 09:00→10:30 elapsed, less 30m still on break
    }

    [Fact]
    public void An_open_break_grows_as_now_advances()
    {
        var events = new[] { CheckIn(At(9)), BreakStart(At(10)) };

        var atHalfPast = AttendanceDayStateCalculator.Calculate(events, At(10, 30));
        var anHourLater = AttendanceDayStateCalculator.Calculate(events, At(11, 30));

        Assert.Equal(60, atHalfPast.WorkedMinutes);
        Assert.Equal(60, anHourLater.WorkedMinutes); // the extra hour was all break
        Assert.Equal(AttendanceDayStatus.Break, anHourLater.Status);
    }

    /// <summary>
    /// Forgetting to end a break must not bill it as work: checking out closes it,
    /// and only then does it land in the completed total.
    /// </summary>
    [Fact]
    public void Checking_out_closes_a_break_that_was_never_ended()
    {
        var state = Calculate(CheckIn(At(9)), BreakStart(At(12)), CheckOut(At(17)));

        Assert.Equal(AttendanceDayStatus.Done, state.Status);
        Assert.Null(state.OnBreakSince);
        Assert.Equal(300, state.TotalBreakMinutes); // 12:00→17:00 all treated as break
        Assert.Equal(180, state.WorkedMinutes);     // only 09:00→12:00 counts
    }

    [Fact]
    public void Multiple_completed_breaks_are_summed()
    {
        var state = Calculate(
            CheckIn(At(9)),
            BreakStart(At(10)), BreakEnd(At(10, 15)),
            BreakStart(At(12)), BreakEnd(At(12, 45)),
            CheckOut(At(17)));

        Assert.Equal(60, state.TotalBreakMinutes);
        Assert.Equal(420, state.WorkedMinutes); // 8h − 1h
    }

    [Fact]
    public void A_second_break_start_while_already_on_break_is_ignored()
    {
        var state = Calculate(CheckIn(At(9)), BreakStart(At(10)), BreakStart(At(10, 20)));

        Assert.Equal(At(10), state.OnBreakSince); // the first one still governs
        Assert.Equal(60, state.WorkedMinutes);
    }

    [Fact]
    public void A_break_end_with_no_open_break_is_ignored()
    {
        var state = Calculate(CheckIn(At(9)), BreakEnd(At(10)), CheckOut(At(11)));

        Assert.Equal(AttendanceDayStatus.Done, state.Status);
        Assert.Equal(0, state.TotalBreakMinutes);
        Assert.Equal(120, state.WorkedMinutes);
    }

    [Fact]
    public void A_break_start_before_any_check_in_is_ignored()
    {
        var state = Calculate(BreakStart(At(8)), CheckIn(At(9)));

        Assert.Equal(AttendanceDayStatus.In, state.Status);
        Assert.Null(state.OnBreakSince);
        Assert.Equal(90, state.WorkedMinutes); // 09:00→10:30, no break
    }

    [Fact]
    public void A_break_started_after_checking_out_is_ignored()
    {
        var state = Calculate(CheckIn(At(9)), CheckOut(At(11)), BreakStart(At(12)));

        Assert.Equal(AttendanceDayStatus.Done, state.Status);
        Assert.Null(state.OnBreakSince);
        Assert.Equal(120, state.WorkedMinutes);
    }

    /* ── missing check-out ───────────────────────────────────────────────── */

    [Fact]
    public void A_day_with_no_check_out_is_still_in_progress_and_accrues_time()
    {
        var state = Calculate(CheckIn(At(9)));

        Assert.Equal(AttendanceDayStatus.In, state.Status);
        Assert.Equal(At(9), state.CheckInAt);
        Assert.Null(state.CheckOutAt);
        Assert.Equal(90, state.WorkedMinutes); // 09:00→now (10:30)
    }

    /// <summary>
    /// The consequence of never checking out: the day keeps accruing. Nothing
    /// caps it at a working day, which is what the company dashboard's
    /// "over 10 hours" check is really detecting.
    /// </summary>
    [Fact]
    public void An_abandoned_check_in_keeps_accruing_without_limit()
    {
        var state = AttendanceDayStateCalculator.Calculate([CheckIn(At(9))], At(23, 59));

        Assert.Equal(AttendanceDayStatus.In, state.Status);
        Assert.Equal(899, state.WorkedMinutes); // just under 15 hours
    }

    /* ── multiple check-ins ──────────────────────────────────────────────── */

    /// <summary>
    /// The first check-in wins, so a double-tap on the button cannot shorten the
    /// day by moving its start later.
    /// </summary>
    [Fact]
    public void The_first_check_in_of_the_day_wins()
    {
        var state = Calculate(CheckIn(At(9)), CheckIn(At(9, 5)), CheckIn(At(10)));

        Assert.Equal(At(9), state.CheckInAt);
        Assert.Equal(90, state.WorkedMinutes);
    }

    /// <summary>
    /// The last check-out wins, so someone who checks out, is called back, and
    /// checks out again is credited to the later departure.
    /// </summary>
    [Fact]
    public void The_last_check_out_of_the_day_wins()
    {
        var state = Calculate(CheckIn(At(9)), CheckOut(At(13)), CheckOut(At(17)));

        Assert.Equal(At(17), state.CheckOutAt);
        Assert.Equal(480, state.WorkedMinutes);
    }

    /// <summary>
    /// A full out-and-back-in day still reports one span. Re-checking in after
    /// checking out does not reopen the day, because the first check-in already
    /// won and the last check-out governs — the gap in the middle is counted as
    /// worked. Pinned as the current rule rather than endorsed as the ideal one.
    /// </summary>
    [Fact]
    public void Checking_back_in_after_checking_out_does_not_reopen_the_day()
    {
        var state = Calculate(
            CheckIn(At(9)), CheckOut(At(12)),
            CheckIn(At(13)), CheckOut(At(17)));

        Assert.Equal(AttendanceDayStatus.Done, state.Status);
        Assert.Equal(At(9), state.CheckInAt);
        Assert.Equal(At(17), state.CheckOutAt);
        Assert.Equal(0, state.TotalBreakMinutes);
        Assert.Equal(480, state.WorkedMinutes); // the 12:00–13:00 gap is not a break
    }

    /* ── overnight ───────────────────────────────────────────────────────── */

    /// <summary>
    /// Callers bucket events by UTC day, so a shift starting at 22:00 and ending
    /// at 02:00 is split. The evening bucket holds only the check-in and reads as
    /// a day still in progress.
    /// </summary>
    [Fact]
    public void The_evening_half_of_an_overnight_shift_reads_as_still_working()
    {
        var state = AttendanceDayStateCalculator.Calculate([CheckIn(At(22))], At(23, 30));

        Assert.Equal(AttendanceDayStatus.In, state.Status);
        Assert.Equal(At(22), state.CheckInAt);
        Assert.Equal(90, state.WorkedMinutes);
    }

    /// <summary>
    /// The morning bucket holds the check-out alone. It reports Out with no worked
    /// time — the hours belong to the previous day's bucket, and this calculator
    /// only ever sees one day. Note CheckOutAt is populated while the status is
    /// Out, which is the one combination callers have to be ready for: a naive
    /// "CheckOutAt means they finished a shift today" read would be wrong.
    /// </summary>
    [Fact]
    public void The_morning_half_of_an_overnight_shift_reports_out_with_no_hours()
    {
        var state = AttendanceDayStateCalculator.Calculate([CheckOut(At(2))], At(9));

        Assert.Equal(AttendanceDayStatus.Out, state.Status);
        Assert.Null(state.CheckInAt);
        Assert.Equal(At(2), state.CheckOutAt);
        Assert.Equal(0, state.WorkedMinutes);
    }

    /// <summary>
    /// Someone who finishes an overnight shift and starts a normal day is credited
    /// only from the new check-in, because the stray check-out is timestamped
    /// before it and the negative span is clamped away rather than subtracted.
    /// </summary>
    [Fact]
    public void A_check_out_before_the_check_in_never_reports_negative_work()
    {
        var state = Calculate(CheckOut(At(2)), CheckIn(At(9)));

        Assert.Equal(AttendanceDayStatus.Done, state.Status);
        Assert.Equal(At(9), state.CheckInAt);
        Assert.Equal(At(2), state.CheckOutAt);
        Assert.Equal(0, state.WorkedMinutes);
    }

    [Fact]
    public void Calculate_rejects_a_null_event_sequence()
    {
        Assert.Throws<ArgumentNullException>(
            () => AttendanceDayStateCalculator.Calculate(null!, Now));
    }
}
