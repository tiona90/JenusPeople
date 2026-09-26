using Application.Attendance.Support;
using Application.Reminders;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The daily attendance report: every HR Administrator is emailed each working
/// morning about the previous working day in their departments — who checked in
/// late, who never checked in, who never checked out, whose timesheet for the last
/// week past its deadline is still unsubmitted, and who was on leave. A System
/// Administrator runs the workspace, not attendance, and is sent nothing; the
/// administrators and deactivated accounts are reported on by nobody either, since
/// they are not part of the tracked workforce.
/// </summary>
public class DailyAttendanceReportTests
{
    private const string AdminEmail = "ada@example.com";
    private const string HrEmail = "hope@example.com";
    private const int DepartmentId = 1;
    private const int OtherDepartmentId = 2;

    /// <summary>
    /// Every day is a working day and no holiday country is set, so the report
    /// always runs and always covers yesterday. The deadline is Monday 00:00, so
    /// the current week's timesheet is already due whatever day the test runs
    /// on. UTC keeps the late arithmetic independent of the host.
    /// </summary>
    private static AppSettings Settings() => new()
    {
        EmailNotificationsEnabled = true,
        WorkingDays = "custom",
        WorkingDaysCustom = "sun,mon,tue,wed,thu,fri,sat",
        HolidayCountryCode = null,
        TimeZoneId = "UTC",
        WorkingHoursStart = "09:00",
        WorkingHoursEnd = "18:00",
        TimesheetSubmissionDeadlineDay = "mon",
        TimesheetSubmissionDeadlineTime = "00:00",
    };

    private static readonly DateTime Yesterday = AttendanceDay.UtcDayStart(DateTime.UtcNow).AddDays(-1);

    private static DateTime CurrentWeekStart()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var back = ((int)today.DayOfWeek + 6) % 7; // Monday = 0
        return today.AddDays(-back).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }

    private static Timesheet WeekTimesheet(string profileId, TimesheetStatus status) => new()
    {
        EmployeeProfileId = profileId,
        DepartmentId = DepartmentId,
        PeriodStart = CurrentWeekStart(),
        PeriodEnd = CurrentWeekStart().AddDays(6),
        Status = status,
    };

    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });
        db.Departments.Add(new Department { Id = OtherDepartmentId, Name = "Finance", Code = "FIN" });
        db.LeaveTypes.Add(new LeaveType { Id = 1, Name = "Annual Leave", IsActive = true });

        var adminRole = new Role { Id = "r-admin", Name = AppRoles.SystemAdministrator, NormalizedName = AppRoles.SystemAdministrator.ToUpperInvariant() };
        var hrRole = new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() };
        db.Roles.AddRange(adminRole, hrRole);

        SeedPerson(db, "admin", "Ada System Administrator", AdminEmail, departmentId: null);
        db.UserRoles.Add(new UserRole { UserId = "admin-u", RoleId = adminRole.Id });
        // An admin checking in late must not be reported: the widget is hidden for the role.
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("admin-p", Yesterday.AddHours(11), AttendanceEventType.CheckIn));

        // Hope runs Engineering. Finance is nobody's, so Gus below is reported to nobody.
        SeedPerson(db, "hr", "Hope HR Administrator", HrEmail, departmentId: null);
        db.UserRoles.Add(new UserRole { UserId = "hr-u", RoleId = hrRole.Id });
        db.UserDepartments.Add(new UserDepartment { UserId = "hr-u", DepartmentId = DepartmentId });
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("hr-p", Yesterday.AddHours(11), AttendanceEventType.CheckIn));

        // Gus: never checked in, but in a department no HR Administrator runs.
        SeedPerson(db, "gus", "Gus Outsider", "gus@example.com", OtherDepartmentId);

        // Eve: 30 minutes late, checked out, no timesheet at all.
        SeedPerson(db, "eve", "Eve Employee", "eve@example.com", DepartmentId);
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("eve-p", Yesterday.AddHours(9).AddMinutes(30), AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("eve-p", Yesterday.AddHours(17), AttendanceEventType.CheckOut));

        // Bob: on time, never checked out, timesheet still in draft.
        SeedPerson(db, "bob", "Bob Employee", "bob@example.com", DepartmentId);
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("bob-p", Yesterday.AddHours(8), AttendanceEventType.CheckIn));
        db.Timesheets.Add(WeekTimesheet("bob-p", TimesheetStatus.Draft));

        // Cara: never checked in, timesheet submitted.
        SeedPerson(db, "cara", "Cara Employee", "cara@example.com", DepartmentId);
        db.Timesheets.Add(WeekTimesheet("cara-p", TimesheetStatus.Submitted));

        // Dan: on approved leave, so a missing check-in is not a miss.
        SeedPerson(db, "dan", "Dan Employee", "dan@example.com", DepartmentId);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = "dan-u",
            EmployeeProfileId = "dan-p",
            DepartmentId = DepartmentId,
            LeaveTypeId = 1,
            StartDate = Yesterday.AddDays(-1),
            EndDate = Yesterday.AddDays(1),
            Status = AnnualLeaveStatus.Approved,
            CreatedAt = DateTime.UtcNow,
        });
        db.Timesheets.Add(WeekTimesheet("dan-p", TimesheetStatus.Approved));

        // Fay: on time, 11.5 hours on site with a half-hour break, so 11 hours
        // worked against a 9-hour day: two hours of overtime.
        SeedPerson(db, "fay", "Fay Employee", "fay@example.com", DepartmentId);
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("fay-p", Yesterday.AddHours(8), AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("fay-p", Yesterday.AddHours(12), AttendanceEventType.BreakStart));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("fay-p", Yesterday.AddHours(12).AddMinutes(30), AttendanceEventType.BreakEnd));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("fay-p", Yesterday.AddHours(19).AddMinutes(30), AttendanceEventType.CheckOut));
        db.Timesheets.Add(WeekTimesheet("fay-p", TimesheetStatus.Submitted));

        // Zed: a leaver, switched off. Never reported.
        SeedPerson(db, "zed", "Zed Leaver", "zed@example.com", DepartmentId, isActive: false);

        db.SaveChanges();
        return db;
    }

    private static void SeedPerson(AppDbContext db, string key, string displayName, string email, int? departmentId, bool isActive = true)
    {
        db.Users.Add(new User { Id = $"{key}-u", UserName = key, DisplayName = displayName, Email = email, IsActive = isActive });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"{key}-p", UserId = $"{key}-u", DepartmentId = departmentId });
    }

    private static ReminderDispatcher DispatcherFor(AppDbContext db, FakeEmailService email) =>
        new(db, email, NullLogger<ReminderDispatcher>.Instance);

    /// <summary>The HTML between one h3 heading and the next.</summary>
    private static string Section(string html, string heading)
    {
        var marker = $"<h3>{heading}</h3>";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Section '{heading}' missing from report:\n{html}");
        start += marker.Length;
        var end = html.IndexOf("<h3>", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    private static async Task<SentEmail> RunAsync(AppDbContext db, AppSettings settings)
    {
        var email = new FakeEmailService();
        await DispatcherFor(db, email).DispatchAsync(ReminderDispatcher.DailyAttendanceReport, settings, CancellationToken.None);
        return Assert.Single(email.Sent);
    }

    [Fact]
    public async Task Report_goes_to_the_HR_Administrator_and_not_to_the_System_Administrator()
    {
        using var db = SeedWorld();

        var mail = await RunAsync(db, Settings());

        Assert.Equal(HrEmail, mail.Recipient);
        Assert.Contains("Hope HR Administrator", mail.HtmlBody);

        // The greeting names the HR Administrator; the report below it must name
        // neither administrator, the leaver, nor anyone outside HR's departments.
        var body = mail.HtmlBody[mail.HtmlBody.IndexOf("<h3>", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Ada System Administrator", body);
        Assert.DoesNotContain("Hope HR Administrator", body);
        Assert.DoesNotContain("Zed Leaver", body);
        Assert.DoesNotContain("Gus Outsider", body);
    }

    [Fact]
    public async Task Report_says_which_departments_it_covers()
    {
        using var db = SeedWorld();

        var mail = await RunAsync(db, Settings());

        Assert.Contains("covering Engineering", mail.HtmlBody);
        Assert.DoesNotContain("covering all staff", mail.HtmlBody);
        Assert.Contains("covering Engineering", mail.TextBody);
    }

    [Fact]
    public async Task Nothing_is_sent_when_no_HR_Administrator_has_a_department()
    {
        using var db = SeedWorld();
        db.UserDepartments.RemoveRange(db.UserDepartments);
        db.SaveChanges();

        var email = new FakeEmailService();
        await DispatcherFor(db, email).DispatchAsync(ReminderDispatcher.DailyAttendanceReport, Settings(), CancellationToken.None);

        // The System Administrator is not a fallback recipient.
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Each_section_names_the_right_people()
    {
        using var db = SeedWorld();

        var mail = await RunAsync(db, Settings());
        var html = mail.HtmlBody;

        var late = Section(html, "Late check-ins");
        Assert.Contains("Eve Employee", late);
        Assert.Contains("30 min late", late);
        Assert.DoesNotContain("Bob Employee", late);

        var notIn = Section(html, "Did not check in");
        Assert.Contains("Cara Employee", notIn);
        Assert.DoesNotContain("Dan Employee", notIn);
        Assert.DoesNotContain("Eve Employee", notIn);

        var notOut = Section(html, "Did not check out");
        Assert.Contains("Bob Employee", notOut);
        Assert.DoesNotContain("Eve Employee", notOut);

        var timesheets = Section(html, "Timesheet not submitted");
        Assert.Contains("Eve Employee", timesheets);
        Assert.Contains("Bob Employee", timesheets);
        Assert.DoesNotContain("Cara Employee", timesheets);
        Assert.DoesNotContain("Dan Employee", timesheets);

        var onLeave = Section(html, "On leave");
        Assert.Contains("Dan Employee", onLeave);
        Assert.Contains("Annual Leave", onLeave);

        // Overtime is worked time (breaks excluded) beyond the configured working
        // day. Bob never checked out, so his day has no length to judge.
        var overtime = Section(html, "Overtime");
        Assert.Contains("Fay Employee", overtime);
        Assert.Contains("2h 00m over", overtime);
        Assert.DoesNotContain("Eve Employee", overtime);
        Assert.DoesNotContain("Bob Employee", overtime);
    }

    /// <summary>
    /// The working day the overtime is judged against is net of the configured
    /// break: with an hour's lunch on 09:00–18:00 the day is eight hours, so Fay's
    /// eleven worked read as three over rather than two.
    /// </summary>
    [Fact]
    public async Task Overtime_is_judged_against_the_working_day_net_of_the_break()
    {
        using var db = SeedWorld();
        var settings = Settings();
        settings.BreakMode = "fixed";
        settings.BreakStart = "13:00";
        settings.BreakEnd = "14:00";

        var mail = await RunAsync(db, settings);

        var overtime = Section(mail.HtmlBody, "Overtime");
        Assert.Contains("Fay Employee", overtime);
        Assert.Contains("3h 00m over", overtime);
    }

    /// <summary>
    /// The break each person took against the break the settings allow for, over
    /// and under, on the days they checked out of. Bob never checked out, so his
    /// break is not judged here; "Did not check out" already names him.
    /// </summary>
    [Fact]
    public async Task Break_allowance_section_names_who_took_more_or_less_than_the_break()
    {
        using var db = SeedWorld();
        // Olly: an hour and a half's break against the hour allowed, checked out.
        SeedPerson(db, "olly", "Olly Employee", "olly@example.com", DepartmentId);
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("olly-p", Yesterday.AddHours(9), AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("olly-p", Yesterday.AddHours(12), AttendanceEventType.BreakStart));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("olly-p", Yesterday.AddHours(13).AddMinutes(30), AttendanceEventType.BreakEnd));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("olly-p", Yesterday.AddHours(18), AttendanceEventType.CheckOut));
        db.SaveChanges();

        var settings = Settings();
        settings.BreakMode = "fixed";
        settings.BreakStart = "13:00";
        settings.BreakEnd = "14:00";

        var section = Section((await RunAsync(db, settings)).HtmlBody, "Break allowance");

        Assert.Contains("Olly Employee (Engineering) — 0h 30m over (took 1h 30m of 1h 00m)", section);
        Assert.Contains("Fay Employee (Engineering) — 0h 30m under (took 0h 30m of 1h 00m)", section);
        Assert.Contains("Eve Employee (Engineering) — 1h 00m under (took 0h 00m of 1h 00m)", section);
        Assert.DoesNotContain("Bob Employee", section);
    }

    [Fact]
    public async Task Break_allowance_section_is_absent_when_no_break_is_configured()
    {
        using var db = SeedWorld();

        var mail = await RunAsync(db, Settings());

        Assert.DoesNotContain("Break allowance", mail.HtmlBody);
        Assert.DoesNotContain("Break allowance", mail.TextBody);
    }

    [Fact]
    public async Task Late_is_measured_in_the_org_time_zone()
    {
        using var db = SeedWorld();
        // At UTC+2, Eve's 09:30 UTC check-in is 11:30 local: 150 minutes late,
        // and Bob's 08:00 UTC check-in is 10:00 local, so he is late too.
        var settings = Settings();
        settings.TimeZoneId = "Etc/GMT-2";

        var late = Section((await RunAsync(db, settings)).HtmlBody, "Late check-ins");

        Assert.Contains("150 min late", late);
        Assert.Contains("Bob Employee", late);
    }

    [Fact]
    public async Task Nothing_is_sent_on_a_non_working_day()
    {
        using var db = SeedWorld();
        var settings = Settings();
        var today = (int)DateTime.UtcNow.DayOfWeek;
        settings.WorkingDaysCustom = string.Join(",",
            new[] { "sun", "mon", "tue", "wed", "thu", "fri", "sat" }.Where((_, i) => i != today));

        var email = new FakeEmailService();
        await DispatcherFor(db, email).DispatchAsync(ReminderDispatcher.DailyAttendanceReport, settings, CancellationToken.None);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Report_lists_who_checked_out_under_the_scheduled_day()
    {
        using var db = SeedWorld();

        var mail = await RunAsync(db, Settings());

        var shortDay = Section(mail.HtmlBody, "Short day");
        Assert.Contains("Eve Employee (Engineering) — 1h 30m short (worked 7h 30m of 9h 00m)", shortDay);
        Assert.DoesNotContain("Fay", shortDay);    // overtime, not short
        Assert.DoesNotContain("Bob", shortDay);    // never checked out: reported under that heading
        Assert.DoesNotContain("Cara", shortDay);   // never checked in: reported under that heading
        Assert.DoesNotContain("Dan", shortDay);    // on leave
        Assert.Contains("Short day:", mail.TextBody);
    }

    [Fact]
    public async Task A_half_day_of_leave_halves_the_day_the_report_expects()
    {
        using var db = SeedWorld();
        SeedPerson(db, "hal", "Hal Half", "hal@example.com", DepartmentId);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = "hal-u", EmployeeProfileId = "hal-p", DepartmentId = DepartmentId, LeaveTypeId = 1,
            StartDate = Yesterday, EndDate = Yesterday, Status = AnnualLeaveStatus.Approved,
            Duration = Domain.Services.LeaveDuration.HalfDayMorning, CreatedAt = DateTime.UtcNow,
        });
        // 4h 30m worked against a 4h 30m half day: not short.
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("hal-p", Yesterday.AddHours(13), AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("hal-p", Yesterday.AddHours(17).AddMinutes(30), AttendanceEventType.CheckOut));
        db.SaveChanges();

        var mail = await RunAsync(db, Settings());

        Assert.DoesNotContain("Hal Half", Section(mail.HtmlBody, "Short day"));
    }
}
