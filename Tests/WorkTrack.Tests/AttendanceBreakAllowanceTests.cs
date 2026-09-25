using Application.Attendance.Queries;
using Application.Attendance.Support;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The break the organisation allows for (Organization settings → Working Week)
/// is compared with the break each person actually took, on every attendance
/// surface a Manager or an HR Administrator reads: the team board's member row,
/// the company dashboard's issues, and the employee's own today panel and
/// history. All four read <c>WorkingDaySchedule.BreakVariance</c>, so they agree.
/// </summary>
public class AttendanceBreakAllowanceTests
{
    private const int DepartmentId = 1;
    private const string Owen = "owen";   // 1h 20m of break — 20 over the hour
    private const string Una = "una";     // 30 min, checked out — 30 under
    private const string Ivy = "ivy";     // 10 min, still working — nothing to say yet

    private static readonly DateTime Today = new(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Afternoon = Today.AddHours(15);

    private static AppDbContext SeedWorld(string breakMode = "flexible", int breakMinutes = 60)
    {
        var db = TestDb.Create();

        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC",
            WorkingHoursStart = "08:00",
            WorkingHoursEnd = "17:00",
            BreakMode = breakMode,
            BreakMinutes = breakMinutes,
        });
        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });

        AddEmployee(db, Owen, "Owen Over");
        AddEmployee(db, Una, "Una Under");
        AddEmployee(db, Ivy, "Ivy Inside");

        Add(db, Owen, Today.AddHours(8), AttendanceEventType.CheckIn);
        Add(db, Owen, Today.AddHours(12), AttendanceEventType.BreakStart);
        Add(db, Owen, Today.AddHours(13).AddMinutes(20), AttendanceEventType.BreakEnd);

        Add(db, Una, Today.AddHours(8), AttendanceEventType.CheckIn);
        Add(db, Una, Today.AddHours(12), AttendanceEventType.BreakStart);
        Add(db, Una, Today.AddHours(12).AddMinutes(30), AttendanceEventType.BreakEnd);
        Add(db, Una, Today.AddHours(14), AttendanceEventType.CheckOut);

        Add(db, Ivy, Today.AddHours(8), AttendanceEventType.CheckIn);
        Add(db, Ivy, Today.AddHours(10), AttendanceEventType.BreakStart);
        Add(db, Ivy, Today.AddHours(10).AddMinutes(10), AttendanceEventType.BreakEnd);

        db.SaveChanges();
        return db;
    }

    private static void AddEmployee(AppDbContext db, string key, string name)
    {
        db.Users.Add(new User { Id = $"u-{key}", UserName = key, DisplayName = name });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"p-{key}", UserId = $"u-{key}", DepartmentId = DepartmentId });
    }

    private static void Add(AppDbContext db, string key, DateTime at, AttendanceEventType type) =>
        db.AttendanceEvents.Add(new AttendanceEvent
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeProfileId = $"p-{key}",
            At = at,
            Type = type,
        });

    // ── Team board ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Team_board_carries_the_break_taken_and_its_variance()
    {
        using var db = SeedWorld();

        var result = await new GetTeamAttendance.Handler(db).Handle(
            new GetTeamAttendance.Query { RequestingUserId = "nobody", IsAdmin = true, NowUtc = Afternoon },
            CancellationToken.None);

        var members = result.Value!.Members;
        var owen = Assert.Single(members, m => m.EmployeeName == "Owen Over");
        Assert.Equal(80, owen.BreakMinutes);
        Assert.Equal(20, owen.BreakVarianceMinutes);

        var una = Assert.Single(members, m => m.EmployeeName == "Una Under");
        Assert.Equal(30, una.BreakMinutes);
        Assert.Equal(-30, una.BreakVarianceMinutes);

        var ivy = Assert.Single(members, m => m.EmployeeName == "Ivy Inside");
        Assert.Equal(10, ivy.BreakMinutes);
        Assert.Null(ivy.BreakVarianceMinutes);
    }

    [Fact]
    public async Task Team_board_says_nothing_when_no_break_is_configured()
    {
        using var db = SeedWorld(breakMode: "none", breakMinutes: 0);

        var result = await new GetTeamAttendance.Handler(db).Handle(
            new GetTeamAttendance.Query { RequestingUserId = "nobody", IsAdmin = true, NowUtc = Afternoon },
            CancellationToken.None);

        Assert.All(result.Value!.Members, m => Assert.Null(m.BreakVarianceMinutes));
        Assert.Equal(80, Assert.Single(result.Value.Members, m => m.EmployeeName == "Owen Over").BreakMinutes);
    }

    // ── Company dashboard ─────────────────────────────────────────────────────

    [Fact]
    public async Task Company_issues_list_who_is_over_the_break_allowance()
    {
        using var db = SeedWorld();

        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query { NowUtc = Afternoon },
            CancellationToken.None);

        var issue = Assert.Single(result.Value!.Issues, i => i.Title.Contains("over break allowance"));
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("1 over break allowance", issue.Title);
        Assert.Contains("Owen Over (Engineering) · 20 min over", issue.Detail);
        // Under is not an issue for the dashboard: it flags what needs attention.
        Assert.DoesNotContain("Una", issue.Detail);
    }

    [Fact]
    public async Task Company_issues_carry_no_break_line_when_nobody_is_over()
    {
        using var db = SeedWorld(breakMinutes: 120);

        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query { NowUtc = Afternoon },
            CancellationToken.None);

        Assert.DoesNotContain(result.Value!.Issues, i => i.Title.Contains("break allowance"));
    }

    /// <summary>
    /// The feed row for a break's end says how far over the allowance the day's
    /// break stood at that moment, the way a check-in row says "Late check-in".
    /// It is the one place an HR Administrator reads a break on Company
    /// Attendance — the issues card is on the dashboard and the team board is
    /// the Manager's — and a row reading "Back from break" for an hour and
    /// twenty minutes told them nothing. Under is not news here either.
    /// </summary>
    [Fact]
    public async Task Company_feed_marks_a_break_end_that_went_over_the_allowance()
    {
        using var db = SeedWorld();

        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query { NowUtc = Afternoon },
            CancellationToken.None);

        var recent = result.Value!.Recent;
        var owen = Assert.Single(recent, r => r.EmployeeName == "Owen Over" && r.Action == "Back from break");
        Assert.Equal(20, owen.BreakVarianceMinutes);

        var una = Assert.Single(recent, r => r.EmployeeName == "Una Under" && r.Action == "Back from break");
        Assert.Null(una.BreakVarianceMinutes);
        var ivy = Assert.Single(recent, r => r.EmployeeName == "Ivy Inside" && r.Action == "Back from break");
        Assert.Null(ivy.BreakVarianceMinutes);

        // Only the break's end carries it: the start of one has nothing to judge yet.
        Assert.All(recent.Where(r => r.Action != "Back from break"), r => Assert.Null(r.BreakVarianceMinutes));
    }

    /// <summary>
    /// The verdict is as of the break's end, not as of now: a second break that
    /// takes the day over marks its own row, and the earlier row stays clean.
    /// </summary>
    [Fact]
    public async Task Company_feed_judges_each_break_end_as_of_that_moment()
    {
        using var db = SeedWorld();
        // Ivy's 10 minutes were fine; a further 55 at lunch put the day 5 over.
        Add(db, Ivy, Today.AddHours(12), AttendanceEventType.BreakStart);
        Add(db, Ivy, Today.AddHours(12).AddMinutes(55), AttendanceEventType.BreakEnd);
        db.SaveChanges();

        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query { NowUtc = Afternoon },
            CancellationToken.None);

        var ivyRows = result.Value!.Recent
            .Where(r => r.EmployeeName == "Ivy Inside" && r.Action == "Back from break")
            .OrderBy(r => r.At)
            .ToList();
        Assert.Equal(2, ivyRows.Count);
        Assert.Null(ivyRows[0].BreakVarianceMinutes);
        Assert.Equal(5, ivyRows[1].BreakVarianceMinutes);
    }

    [Fact]
    public async Task Company_feed_says_nothing_about_breaks_when_none_is_configured()
    {
        using var db = SeedWorld(breakMode: "none", breakMinutes: 0);

        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query { NowUtc = Afternoon },
            CancellationToken.None);

        Assert.All(result.Value!.Recent, r => Assert.Null(r.BreakVarianceMinutes));
    }

    // ── The employee's own surfaces ───────────────────────────────────────────

    [Fact]
    public async Task Today_state_carries_the_variance()
    {
        using var db = SeedWorld();

        var owen = await AttendanceDay.BuildTodayStateAsync(db, $"p-{Owen}", Afternoon, CancellationToken.None);
        var ivy = await AttendanceDay.BuildTodayStateAsync(db, $"p-{Ivy}", Afternoon, CancellationToken.None);

        Assert.Equal(20, owen.BreakVarianceMinutes);
        Assert.Null(ivy.BreakVarianceMinutes);
    }

    [Fact]
    public async Task History_carries_the_variance_per_day()
    {
        using var db = SeedWorld();

        var una = await new GetMyAttendanceHistory.Handler(db).Handle(
            new GetMyAttendanceHistory.Query { RequestingUserId = $"u-{Una}", Days = 2, NowUtc = Afternoon },
            CancellationToken.None);

        var days = una.Value!;
        Assert.Equal(2, days.Count);
        // Yesterday had no events: no break, and nothing to judge.
        Assert.Null(days[0].BreakVarianceMinutes);
        Assert.Equal(-30, days[1].BreakVarianceMinutes);
    }
}
