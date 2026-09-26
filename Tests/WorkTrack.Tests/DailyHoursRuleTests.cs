using Application.Attendance.Support;
using Application.Settings.Support;
using Domain;
using Domain.Services;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The one rule for "worked less than the day asks for" and "the timesheet says
/// something attendance does not". Pure: every input is passed in.
/// </summary>
public class DailyHoursRuleTests
{
    private const int Eight = 480;

    [Fact]
    public void Full_leave_has_no_target_and_is_never_short()
    {
        var v = DailyHoursRule.Judge(isWorkingDay: true, LeaveOnDay.Full, Eight, workedMinutes: 0, dayClosed: true);
        Assert.Equal(DayKind.Leave, v.Kind);
        Assert.Null(v.TargetMinutes);
        Assert.Null(v.ShortByMinutes);
    }

    [Fact]
    public void Leave_outranks_a_non_working_day()
    {
        var v = DailyHoursRule.Judge(isWorkingDay: false, LeaveOnDay.Full, Eight, 0, true);
        Assert.Equal(DayKind.Leave, v.Kind);
    }

    [Fact]
    public void A_non_working_day_has_no_target()
    {
        var v = DailyHoursRule.Judge(isWorkingDay: false, LeaveOnDay.None, Eight, 0, true);
        Assert.Equal(DayKind.NonWorking, v.Kind);
        Assert.Null(v.ShortByMinutes);
    }

    [Fact]
    public void A_half_day_leave_halves_the_target()
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.HalfDay, Eight, workedMinutes: 200, dayClosed: true);
        Assert.Equal(DayKind.Working, v.Kind);
        Assert.Equal(240, v.TargetMinutes);
        Assert.Equal(40, v.ShortByMinutes);
    }

    [Fact]
    public void An_open_day_says_nothing_yet()
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.None, Eight, workedMinutes: 60, dayClosed: false);
        Assert.Equal(Eight, v.TargetMinutes);
        Assert.Null(v.ShortByMinutes);
    }

    [Theory]
    [InlineData(465, null)]  // exactly 15 short: within the grace
    [InlineData(464, 16)]    // 16 short: flagged
    [InlineData(480, null)]
    [InlineData(600, null)]  // over is not short
    public void The_grace_is_fifteen_minutes(int worked, int? expected)
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.None, Eight, worked, dayClosed: true);
        Assert.Equal(expected, v.ShortByMinutes);
    }

    [Fact]
    public void No_attendance_on_a_closed_working_day_is_short_by_the_whole_target()
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.None, Eight, 0, true);
        Assert.Equal(Eight, v.ShortByMinutes);
    }

    [Fact]
    public void No_schedule_means_nothing_is_ever_short()
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.None, scheduledMinutes: 0, 0, true);
        Assert.Null(v.TargetMinutes);
        Assert.Null(v.ShortByMinutes);
    }

    [Theory]
    [InlineData(8.0, 480, null)]
    [InlineData(8.0, 465, null)]    // 15 apart: within the grace
    [InlineData(8.0, 390, 90)]      // logged more than attended
    [InlineData(6.0, 480, -120)]    // attended more than logged
    [InlineData(8.0, 0, 480)]       // logged with no attendance at all
    public void Mismatch_is_logged_minus_attended_beyond_the_grace(double logged, int attended, int? expected)
    {
        Assert.Equal(expected, DailyHoursRule.MismatchMinutes(DayKind.Working, (decimal)logged, attended));
    }

    [Fact]
    public void A_leave_day_is_never_a_mismatch()
    {
        Assert.Null(DailyHoursRule.MismatchMinutes(DayKind.Leave, 8m, 0));
    }

    [Fact]
    public void A_weekend_is_compared_when_either_side_has_hours()
    {
        Assert.Equal(240, DailyHoursRule.MismatchMinutes(DayKind.NonWorking, 4m, 0));
        Assert.Null(DailyHoursRule.MismatchMinutes(DayKind.NonWorking, 0m, 0));
    }

    [Fact]
    public void Two_half_days_on_one_date_are_a_full_day()
    {
        Assert.Equal(LeaveOnDay.None, DailyHoursRule.CombineLeave([]));
        Assert.Equal(LeaveOnDay.HalfDay, DailyHoursRule.CombineLeave([LeaveDuration.HalfDayMorning]));
        Assert.Equal(LeaveOnDay.Full, DailyHoursRule.CombineLeave([LeaveDuration.HalfDayMorning, LeaveDuration.HalfDayAfternoon]));
        Assert.Equal(LeaveOnDay.Full, DailyHoursRule.CombineLeave([LeaveDuration.Full]));
    }

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    public void Describe_words_minutes(int minutes, string expected)
    {
        Assert.Equal(expected, DailyHoursRule.Describe(minutes));
    }

    [Fact]
    public void Working_week_reads_holidays_from_the_set()
    {
        var settings = new AppSettings { WorkingDays = "mon-fri" };
        var holiday = new DateOnly(2026, 9, 24); // Thursday
        var holidays = new HashSet<DateOnly> { holiday };

        Assert.False(WorkingWeek.IsWorkingDay(settings, holiday, holidays));
        Assert.True(WorkingWeek.IsWorkingDay(settings, new DateOnly(2026, 9, 23), holidays));
        Assert.False(WorkingWeek.IsWorkingDay(settings, new DateOnly(2026, 9, 26), holidays)); // Saturday
    }
}
