using Application.Reminders;
using Application.Settings.DTOs;
using Application.Settings.Support;
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Notification Settings follow the Working Week: a reminder's time is read on
/// the Organization time zone's clock, nothing is sent on a weekend or public
/// holiday, and a weekly reminder goes out on the first working day of the week.
/// The scheduler used to fire on the server's own clock every day of the year.
/// </summary>
public class ReminderScheduleTests
{
    // Wednesday 23 September 2026 and the days around it.
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Tuesday = new(2026, 9, 22);
    private static readonly DateOnly Wednesday = new(2026, 9, 23);
    private static readonly DateOnly Saturday = new(2026, 9, 26);

    private static ReminderSettingDto Daily(string time = "09:00", bool enabled = true) =>
        new() { Id = "check-in", Enabled = enabled, Time = time, Frequency = "daily" };

    private static ReminderSettingDto WeeklyReminder(string time = "16:00") =>
        new() { Id = "late-submissions", Enabled = true, Time = time, Frequency = "weekly" };

    [Fact]
    public void Daily_reminder_is_due_once_its_time_has_passed_on_a_working_day()
    {
        var state = ReminderSchedule.Evaluate(Daily("09:00"), new TimeOnly(9, 0), Wednesday,
            todayIsWorkingDay: true, firstWorkingDayOfWeek: Monday, lastRun: null);

        Assert.Equal(ReminderDueState.Due, state);
    }

    [Fact]
    public void Daily_reminder_is_not_due_before_its_time()
    {
        var state = ReminderSchedule.Evaluate(Daily("09:00"), new TimeOnly(8, 59), Wednesday,
            todayIsWorkingDay: true, firstWorkingDayOfWeek: Monday, lastRun: null);

        Assert.Equal(ReminderDueState.NotYet, state);
    }

    [Fact]
    public void Nothing_is_due_on_a_non_working_day()
    {
        var daily = ReminderSchedule.Evaluate(Daily("09:00"), new TimeOnly(12, 0), Saturday,
            todayIsWorkingDay: false, firstWorkingDayOfWeek: Monday, lastRun: null);
        var weekly = ReminderSchedule.Evaluate(WeeklyReminder("09:00"), new TimeOnly(12, 0), Saturday,
            todayIsWorkingDay: false, firstWorkingDayOfWeek: Monday, lastRun: null);

        Assert.Equal(ReminderDueState.NotWorkingDay, daily);
        Assert.Equal(ReminderDueState.NotWorkingDay, weekly);
    }

    [Fact]
    public void Weekly_reminder_is_due_on_the_first_working_day_of_the_week_only()
    {
        var onMonday = ReminderSchedule.Evaluate(WeeklyReminder("16:00"), new TimeOnly(16, 0), Monday,
            todayIsWorkingDay: true, firstWorkingDayOfWeek: Monday, lastRun: null);
        var onWednesday = ReminderSchedule.Evaluate(WeeklyReminder("16:00"), new TimeOnly(16, 0), Wednesday,
            todayIsWorkingDay: true, firstWorkingDayOfWeek: Monday, lastRun: null);

        Assert.Equal(ReminderDueState.Due, onMonday);
        Assert.Equal(ReminderDueState.NotFirstWorkingDayOfWeek, onWednesday);
    }

    [Fact]
    public void Weekly_reminder_moves_to_Tuesday_when_Monday_is_a_holiday()
    {
        var state = ReminderSchedule.Evaluate(WeeklyReminder("16:00"), new TimeOnly(16, 0), Tuesday,
            todayIsWorkingDay: true, firstWorkingDayOfWeek: Tuesday, lastRun: null);

        Assert.Equal(ReminderDueState.Due, state);
    }

    [Fact]
    public void A_reminder_already_sent_today_is_not_sent_again()
    {
        var state = ReminderSchedule.Evaluate(Daily("09:00"), new TimeOnly(9, 30), Wednesday,
            todayIsWorkingDay: true, firstWorkingDayOfWeek: Monday, lastRun: Wednesday);

        Assert.Equal(ReminderDueState.AlreadyRanToday, state);
    }

    // ── Catching up a missed slot ───────────────────────────────────────────

    [Fact]
    public void A_missed_reminder_is_caught_up_later_the_same_day()
    {
        var state = ReminderSchedule.Evaluate(Daily("09:00"), new TimeOnly(14, 0), Wednesday,
            todayIsWorkingDay: true, firstWorkingDayOfWeek: Monday, lastRun: null);

        Assert.Equal(ReminderDueState.Due, state);
    }

    [Fact]
    public void A_missed_check_in_reminder_is_dropped_once_the_working_day_is_over()
    {
        var cutoff = new TimeOnly(17, 0);

        var beforeTheEnd = ReminderSchedule.Evaluate(Daily("09:00"), new TimeOnly(16, 59), Wednesday, true, Monday, null, notAfter: cutoff);
        var afterTheEnd = ReminderSchedule.Evaluate(Daily("09:00"), new TimeOnly(17, 0), Wednesday, true, Monday, null, notAfter: cutoff);

        Assert.Equal(ReminderDueState.Due, beforeTheEnd);
        Assert.Equal(ReminderDueState.TooLate, afterTheEnd);
    }

    [Fact]
    public void The_cutoff_never_refuses_a_reminder_scheduled_after_it()
    {
        // Check-in configured for 19:00 on a 09:00–17:00 day: odd, but it is what
        // the admin asked for, and it goes out at 19:00 rather than never.
        var state = ReminderSchedule.Evaluate(Daily("19:00"), new TimeOnly(19, 0), Wednesday, true, Monday, null, notAfter: new TimeOnly(17, 0));

        Assert.Equal(ReminderDueState.Due, state);
    }

    [Fact]
    public void Only_the_check_in_reminder_has_a_cutoff_and_it_is_the_end_of_the_working_day()
    {
        var settings = new AppSettings { WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00" };

        Assert.Equal(new TimeOnly(17, 0), ReminderSchedule.CatchUpCutoff(ReminderDispatcher.CheckInReminder, settings));
        foreach (var id in ReminderDefaults.Create().Select(r => r.Id).Where(id => id != ReminderDispatcher.CheckInReminder))
            Assert.Null(ReminderSchedule.CatchUpCutoff(id, settings));
    }

    [Fact]
    public void A_disabled_or_malformed_reminder_is_never_due()
    {
        Assert.Equal(ReminderDueState.Disabled, ReminderSchedule.Evaluate(Daily(enabled: false), new TimeOnly(12, 0), Wednesday, true, Monday, null));
        Assert.Equal(ReminderDueState.InvalidTime, ReminderSchedule.Evaluate(Daily("noon"), new TimeOnly(12, 0), Wednesday, true, Monday, null));
    }

    // ── The org's clock ─────────────────────────────────────────────────────

    [Fact]
    public void The_org_clock_is_the_configured_time_zone_not_the_server_or_utc()
    {
        var settings = new AppSettings { TimeZoneId = "Europe/Athens" };
        var utc = new DateTime(2026, 9, 23, 6, 0, 0, DateTimeKind.Utc); // 09:00 in Athens (UTC+3 in September)

        var local = WorkingWeek.LocalNow(settings, utc);

        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(local));
        Assert.Equal(Wednesday, WorkingWeek.TodayLocal(settings, utc));
    }

    [Fact]
    public void Today_is_the_date_on_the_org_clock_even_when_utc_has_not_reached_it()
    {
        var settings = new AppSettings { TimeZoneId = "Pacific/Auckland" };
        var utc = new DateTime(2026, 9, 22, 13, 0, 0, DateTimeKind.Utc); // 01:00 on the 23rd in Auckland (UTC+12)

        Assert.Equal(Wednesday, WorkingWeek.TodayLocal(settings, utc));
    }

    [Fact]
    public void An_unknown_or_blank_time_zone_reads_as_utc()
    {
        var utc = new DateTime(2026, 9, 23, 6, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new TimeOnly(6, 0), TimeOnly.FromDateTime(WorkingWeek.LocalNow(new AppSettings { TimeZoneId = "Mars/Olympus" }, utc)));
        Assert.Equal(new TimeOnly(6, 0), TimeOnly.FromDateTime(WorkingWeek.LocalNow(new AppSettings { TimeZoneId = "" }, utc)));
        Assert.Equal(new TimeOnly(6, 0), TimeOnly.FromDateTime(WorkingWeek.LocalNow(null, utc)));
    }

    // ── The working week ────────────────────────────────────────────────────

    [Theory]
    [InlineData("mon-fri", DayOfWeek.Friday, true)]
    [InlineData("mon-fri", DayOfWeek.Saturday, false)]
    [InlineData("mon-fri", DayOfWeek.Sunday, false)]
    [InlineData("mon-sat", DayOfWeek.Saturday, true)]
    [InlineData("mon-sat", DayOfWeek.Sunday, false)]
    [InlineData("sun-fri", DayOfWeek.Sunday, true)]
    [InlineData("sun-fri", DayOfWeek.Saturday, false)]
    [InlineData("nonsense", DayOfWeek.Saturday, false)]
    public void Working_days_follow_the_configured_preset(string preset, DayOfWeek day, bool expected)
    {
        Assert.Equal(expected, WorkingWeek.IsConfiguredWorkingDay(new AppSettings { WorkingDays = preset }, day));
    }

    [Fact]
    public void A_custom_week_reads_its_own_day_tokens()
    {
        var settings = new AppSettings { WorkingDays = "custom", WorkingDaysCustom = "tue, Wed,thu,fri,sat" };

        Assert.False(WorkingWeek.IsConfiguredWorkingDay(settings, DayOfWeek.Monday));
        Assert.True(WorkingWeek.IsConfiguredWorkingDay(settings, DayOfWeek.Tuesday));
        Assert.True(WorkingWeek.IsConfiguredWorkingDay(settings, DayOfWeek.Wednesday));
        Assert.True(WorkingWeek.IsConfiguredWorkingDay(settings, DayOfWeek.Saturday));
        Assert.False(WorkingWeek.IsConfiguredWorkingDay(settings, DayOfWeek.Sunday));
    }

    [Fact]
    public async Task A_public_holiday_for_the_configured_country_is_not_a_working_day()
    {
        using var db = TestDb.Create();
        db.PublicHolidays.Add(new PublicHoliday { CountryCode = "CY", Year = 2026, Date = Wednesday.ToDateTime(TimeOnly.MinValue), LocalName = "Test", EnglishName = "Test" });
        await db.SaveChangesAsync();

        var cyprus = new AppSettings { WorkingDays = "mon-fri", HolidayCountryCode = "CY" };
        var elsewhere = new AppSettings { WorkingDays = "mon-fri", HolidayCountryCode = "GB" };
        var none = new AppSettings { WorkingDays = "mon-fri", HolidayCountryCode = null };

        Assert.False(await WorkingWeek.IsWorkingDayAsync(db, cyprus, Wednesday, CancellationToken.None));
        Assert.True(await WorkingWeek.IsWorkingDayAsync(db, elsewhere, Wednesday, CancellationToken.None));
        Assert.True(await WorkingWeek.IsWorkingDayAsync(db, none, Wednesday, CancellationToken.None));
        Assert.False(await WorkingWeek.IsWorkingDayAsync(db, none, Saturday, CancellationToken.None));
    }

    [Fact]
    public async Task First_working_day_of_the_week_skips_a_Monday_holiday_and_a_non_working_Monday()
    {
        using var db = TestDb.Create();
        db.PublicHolidays.Add(new PublicHoliday { CountryCode = "CY", Year = 2026, Date = Monday.ToDateTime(TimeOnly.MinValue), LocalName = "Test", EnglishName = "Test" });
        await db.SaveChangesAsync();

        var ordinary = new AppSettings { WorkingDays = "mon-fri", HolidayCountryCode = null };
        var holidayMonday = new AppSettings { WorkingDays = "mon-fri", HolidayCountryCode = "CY" };
        var tuesdayToSaturday = new AppSettings { WorkingDays = "custom", WorkingDaysCustom = "tue,wed,thu,fri,sat" };
        var nothingWorked = new AppSettings { WorkingDays = "custom", WorkingDaysCustom = "" };

        Assert.Equal(Monday, await WorkingWeek.FirstWorkingDayOfWeekAsync(db, ordinary, Wednesday, CancellationToken.None));
        Assert.Equal(Tuesday, await WorkingWeek.FirstWorkingDayOfWeekAsync(db, holidayMonday, Wednesday, CancellationToken.None));
        Assert.Equal(Tuesday, await WorkingWeek.FirstWorkingDayOfWeekAsync(db, tuesdayToSaturday, Wednesday, CancellationToken.None));
        Assert.Null(await WorkingWeek.FirstWorkingDayOfWeekAsync(db, nothingWorked, Wednesday, CancellationToken.None));
        // Asked on the Monday itself, before the rest of the week exists.
        Assert.Equal(Monday, await WorkingWeek.FirstWorkingDayOfWeekAsync(db, ordinary, Monday, CancellationToken.None));
        Assert.Null(await WorkingWeek.FirstWorkingDayOfWeekAsync(db, holidayMonday, Monday, CancellationToken.None));
    }
}
