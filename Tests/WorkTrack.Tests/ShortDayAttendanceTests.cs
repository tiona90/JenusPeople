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

    // ── Team board ────────────────────────────────────────────────────────────

    private static async Task<Application.Attendance.DTOs.TeamAttendanceDto> Team(AppDbContext db, DateTime? now = null)
    {
        var result = await new GetTeamAttendance.Handler(db).Handle(
            new GetTeamAttendance.Query { RequestingUserId = "nobody", IsAdmin = true, NowUtc = now ?? Now },
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task Week_grid_marks_short_days_and_leave()
    {
        using var db = SeedWorld();
        var week = (await Team(db)).Week;

        var sam = Assert.Single(week, r => r.EmployeeName == "Sam Short").Days;
        Assert.Null(sam[0].ShortByMinutes);
        Assert.Equal(90, sam[1].ShortByMinutes);
        Assert.Null(sam[2].ShortByMinutes);   // within grace
        Assert.Null(sam[3].ShortByMinutes);   // today, still in
        Assert.Null(sam[4].ShortByMinutes);   // Friday, not yet

        var lea = Assert.Single(week, r => r.EmployeeName == "Lea Leave").Days;
        Assert.Equal(480, lea[0].ShortByMinutes);   // no attendance, no leave
        Assert.Null(lea[0].WorkedMinutes);
        Assert.True(lea[1].OnLeave);
        Assert.Null(lea[1].ShortByMinutes);
        Assert.Equal(60, lea[2].ShortByMinutes);    // 3h of a half-day 4h
    }

    [Fact]
    public async Task Team_board_lists_who_was_short_on_the_previous_working_day()
    {
        using var db = SeedWorld();
        var team = await Team(db, Mon.AddDays(2).AddHours(10)); // Wednesday morning → Tuesday

        Assert.Equal("2026-09-22", team.ShortDaysDate);
        var sam = Assert.Single(team.ShortDays!);   // Lea was on leave Tuesday
        Assert.Equal("Sam Short", sam.EmployeeName);
        Assert.Equal(90, sam.ShortByMinutes);
        Assert.Equal(390, sam.WorkedMinutes);
    }

    [Fact]
    public async Task On_a_Monday_the_digest_looks_back_to_Friday()
    {
        using var db = SeedWorld();
        var team = await Team(db, Mon.AddDays(7).AddHours(10));

        Assert.Equal("2026-09-25", team.ShortDaysDate);
        // Nobody recorded Friday, and nobody was on leave: both are short by the whole day.
        Assert.Equal(2, team.ShortDays!.Count);
        Assert.All(team.ShortDays, s => Assert.Equal(480, s.ShortByMinutes));
    }

    [Fact]
    public async Task A_past_day_left_checked_in_is_not_called_short()
    {
        using var db = SeedWorld();
        // Friday: Sam checks in and never checks out. The calculator runs the day to now.
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-sam", Mon.AddDays(4).AddHours(8), AttendanceEventType.CheckIn));
        db.SaveChanges();

        var team = await Team(db, Mon.AddDays(7).AddHours(10));

        Assert.DoesNotContain(team.ShortDays!, s => s.EmployeeName == "Sam Short");
    }

    [Fact]
    public async Task A_deactivated_account_is_not_in_the_digest()
    {
        using var db = SeedWorld();
        // A leaver keeps their profile, and records nothing: every working day would
        // read as short by the whole day. Kept on the board, but given no verdict.
        AddEmployee(db, "ina", "Ina Inactive");
        db.Users.Find("u-ina")!.IsActive = false;
        db.SaveChanges();

        var team = await Team(db, Mon.AddDays(2).AddHours(10)); // Wednesday → Tuesday

        Assert.DoesNotContain(team.ShortDays!, s => s.EmployeeName == "Ina Inactive");
        var ina = Assert.Single(team.Week, r => r.EmployeeName == "Ina Inactive").Days;
        Assert.All(ina, d => Assert.Null(d.ShortByMinutes));
    }

    [Fact]
    public async Task Days_before_the_employment_start_date_are_not_short()
    {
        using var db = SeedWorld();
        // Neo starts on Wednesday and has recorded nothing yet.
        AddEmployee(db, "neo", "Neo Newhire");
        db.EmployeeProfiles.Find("p-neo")!.EmploymentStartDate = new DateOnly(2026, 9, 23);
        db.SaveChanges();

        var neo = Assert.Single((await Team(db)).Week, r => r.EmployeeName == "Neo Newhire").Days;

        Assert.Null(neo[0].ShortByMinutes);           // Monday, before the start
        Assert.Null(neo[1].ShortByMinutes);           // Tuesday, before the start
        Assert.Equal(480, neo[2].ShortByMinutes);     // Wednesday, their first day
    }

    // ── Company dashboard ─────────────────────────────────────────────────────

    private static async Task<List<Application.Attendance.DTOs.IssueDto>> CompanyIssues(AppDbContext db, DateTime now)
    {
        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query { NowUtc = now }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Issues;
    }

    [Fact]
    public async Task Company_issues_name_who_was_short_on_the_previous_working_day()
    {
        using var db = SeedWorld();
        var issues = await CompanyIssues(db, Mon.AddDays(2).AddHours(10));

        var issue = Assert.Single(issues, i => i.Title.Contains("short day"));
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("1 short day on Tue 22 Sep", issue.Title);
        Assert.Contains("Sam Short (Engineering) · 1h 30m short", issue.Detail);
        Assert.DoesNotContain("Lea", issue.Detail);   // on leave that day
    }

    [Fact]
    public async Task On_Tuesday_morning_the_issue_speaks_about_Monday()
    {
        using var db = SeedWorld();
        var issues = await CompanyIssues(db, Mon.AddDays(1).AddHours(10)); // Tuesday → Monday

        // Monday: Sam made 8h; Lea recorded nothing and had no leave, so she is short.
        var issue = Assert.Single(issues, i => i.Title.Contains("short day"));
        Assert.Equal("1 short day on Mon 21 Sep", issue.Title);
        Assert.Contains("Lea Leave", issue.Detail);
        Assert.DoesNotContain("Sam", issue.Detail);
    }

    [Fact]
    public async Task Overtime_is_judged_against_the_scheduled_day_and_leaves_out_people_on_leave()
    {
        using var db = SeedWorld();
        // Thursday 15:00: Oli has worked 8h40 (over 8h + the 15-minute grace).
        // Lou has worked 9h too, but is on approved leave today, so is not counted.
        AddEmployee(db, "oli", "Oli Over");
        Add(db, "oli", Mon.AddDays(3).AddHours(6).AddMinutes(20), AttendanceEventType.CheckIn);
        AddEmployee(db, "lou", "Lou Leave");
        Add(db, "lou", Mon.AddDays(3).AddHours(6), AttendanceEventType.CheckIn);
        Leave(db, "lou", Mon.AddDays(3), LeaveDuration.Full);
        db.SaveChanges();

        var issues = await CompanyIssues(db, Now);

        Assert.Contains(issues, i => i.Title == "1 over the 8h working day today");
    }
}
