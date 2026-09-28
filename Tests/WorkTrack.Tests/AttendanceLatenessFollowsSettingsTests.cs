using Application.Attendance.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The attendance surfaces judge "late" and "not checked in yet" against the
/// organisation's working-hours start in its own time zone — the rule the daily
/// attendance report already followed — rather than against a clock hour in UTC.
///
/// Found on a UTC+3 deployment: the company dashboard called a check-in late only
/// from 10:00 UTC, which is 13:00 local, and listed nobody as not checked in
/// before then either. An employee arriving at 09:45 local read "Checked in" all
/// morning, and the "Today's issues" card said nothing about the eleven people
/// who had not arrived at all.
///
/// Every query here takes an explicit <c>NowUtc</c> so the assertions do not
/// depend on when the suite runs; the controllers leave it null.
/// </summary>
public class AttendanceLatenessFollowsSettingsTests
{
    private const int DepartmentId = 1;
    private const string Eve = "eve";   // 09:30 local — late
    private const string Bob = "bob";   // 08:30 local — on time
    private const string Cara = "cara"; // never checked in

    private static readonly DateTime Today = new(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>10:00 local at UTC+2 — an hour past the 09:00 start.</summary>
    private static readonly DateTime TenLocal = Today.AddHours(8);

    private static AppDbContext SeedWorld(string timeZoneId = "Etc/GMT-2")
    {
        var db = TestDb.Create();

        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = timeZoneId,
            WorkingHoursStart = "09:00",
            WorkingHoursEnd = "18:00",
        });
        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });

        AddEmployee(db, Eve, "Eve Employee");
        AddEmployee(db, Bob, "Bob Employee");
        AddEmployee(db, Cara, "Cara Employee");

        // Etc/GMT-2 is UTC+2: 07:30 UTC is 09:30 local, 06:30 UTC is 08:30 local.
        db.AttendanceEvents.Add(Event(Eve, Today.AddHours(7).AddMinutes(30), AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(Event(Bob, Today.AddHours(6).AddMinutes(30), AttendanceEventType.CheckIn));

        db.SaveChanges();
        return db;
    }

    private static void AddEmployee(AppDbContext db, string key, string name)
    {
        db.Users.Add(new User { Id = $"u-{key}", UserName = key, DisplayName = name });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"p-{key}", UserId = $"u-{key}", DepartmentId = DepartmentId });
    }

    private static AttendanceEvent Event(string key, DateTime at, AttendanceEventType type) => new()
    {
        Id = Guid.NewGuid().ToString(),
        EmployeeProfileId = $"p-{key}",
        At = at,
        Type = type,
    };

    // ── Company dashboard ─────────────────────────────────────────────────────

    [Fact]
    public async Task Company_issues_name_the_late_check_in_by_local_minutes()
    {
        using var db = SeedWorld();

        var result = await new GetCompanyAttendance.Handler(db)
            .Handle(new GetCompanyAttendance.Query { NowUtc = TenLocal }, CancellationToken.None);

        var late = Assert.Single(result.Value!.Issues, i => i.Title == "1 late check-in");
        Assert.Contains("Eve Employee", late.Detail);
        Assert.Contains("30 min late", late.Detail);
        Assert.DoesNotContain("Bob Employee", late.Detail);
    }

    [Fact]
    public async Task Company_feed_labels_the_late_check_in_and_not_the_punctual_one()
    {
        using var db = SeedWorld();

        var result = await new GetCompanyAttendance.Handler(db)
            .Handle(new GetCompanyAttendance.Query { NowUtc = TenLocal }, CancellationToken.None);

        var recent = result.Value!.Recent;
        Assert.Equal("Late check-in", Assert.Single(recent, r => r.EmployeeName == "Eve Employee" && r.At != null).Action);
        Assert.Equal("Checked in", Assert.Single(recent, r => r.EmployeeName == "Bob Employee").Action);
    }

    [Fact]
    public async Task Company_flags_the_absent_once_the_local_grace_hour_has_passed()
    {
        using var db = SeedWorld();

        var result = await new GetCompanyAttendance.Handler(db)
            .Handle(new GetCompanyAttendance.Query { NowUtc = TenLocal }, CancellationToken.None);

        var issue = Assert.Single(result.Value!.Issues, i => i.Title == "1 not checked in (Engineering)");
        Assert.Contains("10:00", issue.Detail);
        var row = Assert.Single(result.Value.Recent, r => r.Action == "Not checked in");
        Assert.Equal("Cara Employee", row.EmployeeName);
    }

    [Fact]
    public async Task Company_says_nothing_about_the_absent_before_the_grace_hour_runs_out()
    {
        using var db = SeedWorld();

        // 09:59 local: Cara may still be on her way.
        var result = await new GetCompanyAttendance.Handler(db)
            .Handle(new GetCompanyAttendance.Query { NowUtc = TenLocal.AddMinutes(-1) }, CancellationToken.None);

        Assert.DoesNotContain(result.Value!.Issues, i => i.Title.Contains("not checked in"));
        Assert.DoesNotContain(result.Value.Recent, r => r.Action == "Not checked in");
        // The late check-in has no grace and is reported regardless.
        Assert.Contains(result.Value.Issues, i => i.Title == "1 late check-in");
    }

    [Fact]
    public async Task Company_reads_the_same_check_ins_as_punctual_under_a_UTC_setting()
    {
        // The proof that the settings decide: at UTC, 07:30 is before a 09:00 start.
        using var db = SeedWorld(timeZoneId: "UTC");

        var result = await new GetCompanyAttendance.Handler(db)
            .Handle(new GetCompanyAttendance.Query { NowUtc = TenLocal }, CancellationToken.None);

        Assert.DoesNotContain(result.Value!.Issues, i => i.Title.Contains("late check-in"));
        Assert.DoesNotContain(result.Value.Issues, i => i.Title.Contains("not checked in"));
        Assert.All(result.Value.Recent.Where(r => r.At != null), r => Assert.Equal("Checked in", r.Action));
    }

    // ── Team board ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Team_board_notes_the_late_check_in_in_local_time()
    {
        using var db = SeedWorld();

        var result = await new GetTeamAttendance.Handler(db).Handle(
            new GetTeamAttendance.Query { RequestingUserId = "nobody", IsAdmin = true, NowUtc = TenLocal },
            CancellationToken.None);

        var members = result.Value!.Members;
        Assert.Equal("Late check-in", Assert.Single(members, m => m.EmployeeName == "Eve Employee").TodayNote);
        Assert.Equal("On track", Assert.Single(members, m => m.EmployeeName == "Bob Employee").TodayNote);
    }

    // ── Personal history strip ────────────────────────────────────────────────

    [Fact]
    public async Task History_grades_a_finished_day_late_by_the_local_start()
    {
        using var db = SeedWorld();
        // Checked out a full 9-hour (540 min) day after each check-in, so neither
        // reads as short — only Eve's late start is left to grade.
        db.AttendanceEvents.Add(Event(Eve, Today.AddHours(7).AddMinutes(30).AddMinutes(540), AttendanceEventType.CheckOut));
        db.AttendanceEvents.Add(Event(Bob, Today.AddHours(6).AddMinutes(30).AddMinutes(540), AttendanceEventType.CheckOut));
        db.SaveChanges();

        var eve = await new GetMyAttendanceHistory.Handler(db).Handle(
            new GetMyAttendanceHistory.Query { RequestingUserId = $"u-{Eve}", Days = 1, NowUtc = Today.AddHours(17) },
            CancellationToken.None);
        var bob = await new GetMyAttendanceHistory.Handler(db).Handle(
            new GetMyAttendanceHistory.Query { RequestingUserId = $"u-{Bob}", Days = 1, NowUtc = Today.AddHours(17) },
            CancellationToken.None);

        Assert.Equal("late", Assert.Single(eve.Value!).Status);
        Assert.Equal("complete", Assert.Single(bob.Value!).Status);
    }

    // ── Team check-in trend ───────────────────────────────────────────────────

    [Fact]
    public async Task Team_history_plots_check_ins_in_local_minutes()
    {
        using var db = SeedWorld();

        var result = await new GetTeamAttendanceHistory.Handler(db).Handle(
            new GetTeamAttendanceHistory.Query { RequestingUserId = "nobody", IsAdmin = true, Days = 1, NowUtc = TenLocal },
            CancellationToken.None);

        var eve = Assert.Single(result.Value!.Members, m => m.EmployeeName == "Eve Employee");
        Assert.Equal(9 * 60 + 30, Assert.Single(eve.Days).CheckInMinutesFromMidnight);
    }
}
