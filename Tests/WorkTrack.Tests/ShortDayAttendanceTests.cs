using Application.Attendance.Queries;
using Application.Attendance.Support;
using Domain;
using Domain.Services;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A working day that came in under the working hours net of the break, with no
/// approved leave to excuse it, is flagged on every attendance surface: the
/// employee's own history, the team board's week grid, and the dashboards' issue
/// lines. 08:00–17:00 with a flexible hour's break is an 8-hour day.
/// </summary>
public class ShortDayAttendanceTests
{
    private const int DepartmentId = 1;
    // Week of Mon 21 Sep 2026; "now" is Thursday afternoon.
    private static readonly DateTime Mon = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Mon.AddDays(3).AddHours(15);

    internal static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC", WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00",
            BreakMode = "flexible", BreakMinutes = 60, WorkingDays = "mon-fri",
        });
        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });

        // Sam: Mon full 8h, Tue 6h30 (short by 1h30), Wed 7h50 (within grace), Thu still in.
        AddEmployee(db, "sam", "Sam Short");
        Day(db, "sam", Mon, 8, 17, breakMinutes: 60);
        Day(db, "sam", Mon.AddDays(1), 8, 15, breakMinutes: 30);          // 7h − 30m = 6h30
        Day(db, "sam", Mon.AddDays(2), 8, 16, breakMinutes: 10);          // 8h − 10m = 7h50
        Add(db, "sam", Mon.AddDays(3).AddHours(8), AttendanceEventType.CheckIn);

        // Lea: on approved leave Tuesday, half-day Wednesday (worked 3h), nothing Monday.
        AddEmployee(db, "lea", "Lea Leave");
        Leave(db, "lea", Mon.AddDays(1), LeaveDuration.Full);
        Leave(db, "lea", Mon.AddDays(2), LeaveDuration.HalfDayMorning);
        Day(db, "lea", Mon.AddDays(2), 13, 16, breakMinutes: 0);           // 3h of a 4h target
        Day(db, "lea", Mon.AddDays(3), 8, 17, breakMinutes: 60);

        db.SaveChanges();
        return db;
    }

    private static void AddEmployee(AppDbContext db, string key, string name)
    {
        db.Users.Add(new User { Id = $"u-{key}", UserName = key, DisplayName = name });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"p-{key}", UserId = $"u-{key}", DepartmentId = DepartmentId });
    }

    private static void Day(AppDbContext db, string key, DateTime day, int inHour, int outHour, int breakMinutes)
    {
        Add(db, key, day.AddHours(inHour), AttendanceEventType.CheckIn);
        if (breakMinutes > 0)
        {
            Add(db, key, day.AddHours(12), AttendanceEventType.BreakStart);
            Add(db, key, day.AddHours(12).AddMinutes(breakMinutes), AttendanceEventType.BreakEnd);
        }
        Add(db, key, day.AddHours(outHour), AttendanceEventType.CheckOut);
    }

    private static void Add(AppDbContext db, string key, DateTime at, AttendanceEventType type) =>
        db.AttendanceEvents.Add(AttendanceDay.NewEvent($"p-{key}", at, type));

    private static void Leave(AppDbContext db, string key, DateTime day, LeaveDuration duration) =>
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = $"u-{key}",
            EmployeeProfileId = $"p-{key}",
            DepartmentId = DepartmentId,
            StartDate = day,
            EndDate = day,
            Status = AnnualLeaveStatus.Approved,
            Duration = duration,
        });

    private static async Task<List<Application.Attendance.DTOs.DayHistoryDto>> History(AppDbContext db, string key)
    {
        var result = await new GetMyAttendanceHistory.Handler(db).Handle(
            new GetMyAttendanceHistory.Query { RequestingUserId = $"u-{key}", Days = 6, NowUtc = Now },
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    // ── My Attendance ─────────────────────────────────────────────────────────

    [Fact]
    public async Task History_grades_a_short_day_and_leaves_one_within_the_grace_alone()
    {
        using var db = SeedWorld();
        var days = (await History(db, "sam")).ToDictionary(d => d.Date);

        Assert.Equal("complete", days["2026-09-21"].Status);
        Assert.Null(days["2026-09-21"].ShortByMinutes);
        Assert.Equal("short", days["2026-09-22"].Status);
        Assert.Equal(90, days["2026-09-22"].ShortByMinutes);
        Assert.Equal(480, days["2026-09-22"].TargetMinutes);
        Assert.Equal("complete", days["2026-09-23"].Status);            // 10 min short: within grace
        Assert.Equal("in-progress", days["2026-09-24"].Status);
        Assert.Null(days["2026-09-24"].ShortByMinutes);                  // open day says nothing
    }

    [Fact]
    public async Task History_reads_leave_as_leave_and_a_weekend_as_off()
    {
        using var db = SeedWorld();
        var days = (await History(db, "lea")).ToDictionary(d => d.Date);

        Assert.Equal("off", days["2026-09-19"].Status);                  // Saturday
        Assert.Null(days["2026-09-19"].ShortByMinutes);
        Assert.Equal("absent", days["2026-09-21"].Status);               // nothing, no leave
        Assert.Equal(480, days["2026-09-21"].ShortByMinutes);
        Assert.Equal("leave", days["2026-09-22"].Status);
        Assert.True(days["2026-09-22"].OnLeave);
        Assert.Null(days["2026-09-22"].ShortByMinutes);
        Assert.Equal(240, days["2026-09-23"].TargetMinutes);             // half day
        Assert.Equal(60, days["2026-09-23"].ShortByMinutes);
        Assert.Equal("short", days["2026-09-23"].Status);
    }
}
