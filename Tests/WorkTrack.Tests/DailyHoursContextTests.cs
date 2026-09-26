using Application.Attendance.Support;
using Domain;
using Domain.Services;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The loader every short-day and mismatch surface shares: settings, holidays and
/// approved leave for a set of people over a range, in three queries.
/// </summary>
public class DailyHoursContextTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc); // Thursday
    private static readonly DateOnly Mon = new(2026, 9, 21);

    private static AppDbContext Seed()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC", WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00",
            BreakMode = "flexible", BreakMinutes = 60, WorkingDays = "mon-fri", HolidayCountryCode = "CY",
        });
        db.PublicHolidays.Add(new PublicHoliday { CountryCode = "CY", Year = 2026, Date = new DateTime(2026, 9, 23), EnglishName = "Test Day" });
        db.PublicHolidays.Add(new PublicHoliday { CountryCode = "GR", Year = 2026, Date = new DateTime(2026, 9, 22), EnglishName = "Elsewhere" });
        Leave(db, "p-ann", new DateTime(2026, 9, 21), new DateTime(2026, 9, 21), AnnualLeaveStatus.Approved, LeaveDuration.Full);
        Leave(db, "p-ann", new DateTime(2026, 9, 22), new DateTime(2026, 9, 22), AnnualLeaveStatus.Pending, LeaveDuration.Full);
        Leave(db, "p-ann", new DateTime(2026, 9, 24), new DateTime(2026, 9, 24), AnnualLeaveStatus.Approved, LeaveDuration.HalfDayMorning);
        Leave(db, "p-bob", new DateTime(2026, 9, 24), new DateTime(2026, 9, 24), AnnualLeaveStatus.Approved, LeaveDuration.HalfDayMorning);
        Leave(db, "p-bob", new DateTime(2026, 9, 24), new DateTime(2026, 9, 24), AnnualLeaveStatus.Approved, LeaveDuration.HalfDayAfternoon);
        db.SaveChanges();
        return db;
    }

    private static void Leave(AppDbContext db, string profileId, DateTime start, DateTime end, AnnualLeaveStatus status, LeaveDuration duration) =>
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = "u-" + profileId,
            EmployeeProfileId = profileId,
            StartDate = start,
            EndDate = end,
            Status = status,
            Duration = duration,
        });

    private static Task<DailyHoursContext> Load(AppDbContext db) =>
        DailyHoursContext.LoadAsync(db, ["p-ann", "p-bob"], Mon, Mon.AddDays(4), Now, CancellationToken.None);

    [Fact]
    public async Task Reads_the_schedule_net_of_the_break()
    {
        using var db = Seed();
        var ctx = await Load(db);
        Assert.Equal(480, ctx.ScheduledMinutes);
        Assert.Equal(new DateOnly(2026, 9, 24), ctx.TodayUtc);
    }

    [Fact]
    public async Task Only_the_configured_countrys_holidays_count()
    {
        using var db = Seed();
        var ctx = await Load(db);
        Assert.True(ctx.IsWorkingDay(new DateOnly(2026, 9, 22)));   // GR holiday: not ours
        Assert.False(ctx.IsWorkingDay(new DateOnly(2026, 9, 23)));  // CY holiday
        Assert.False(ctx.IsWorkingDay(new DateOnly(2026, 9, 26)));  // Saturday
    }

    [Fact]
    public async Task Only_approved_leave_counts_and_halves_are_told_apart()
    {
        using var db = Seed();
        var ctx = await Load(db);
        Assert.Equal(LeaveOnDay.Full, ctx.LeaveOn("p-ann", Mon));
        Assert.Equal(LeaveOnDay.None, ctx.LeaveOn("p-ann", Mon.AddDays(1)));        // pending
        Assert.Equal(LeaveOnDay.HalfDay, ctx.LeaveOn("p-ann", Mon.AddDays(3)));
        Assert.Equal(LeaveOnDay.Full, ctx.LeaveOn("p-bob", Mon.AddDays(3)));        // AM + PM
        Assert.Equal(LeaveOnDay.None, ctx.LeaveOn("p-nobody", Mon));
    }

    [Fact]
    public async Task A_past_day_is_closed_and_today_is_closed_only_once_checked_out()
    {
        using var db = Seed();
        var ctx = await Load(db);
        var open = new AttendanceDayState(AttendanceDayStatus.In, Now.AddHours(-4), null, null, 0, 240, false);
        var done = open with { Status = AttendanceDayStatus.Done, CheckOutAt = Now };

        Assert.True(ctx.IsClosed(Mon, null));
        Assert.False(ctx.IsClosed(ctx.TodayUtc, open));
        Assert.True(ctx.IsClosed(ctx.TodayUtc, done));
        Assert.False(ctx.IsClosed(ctx.TodayUtc.AddDays(1), null));
    }
}
