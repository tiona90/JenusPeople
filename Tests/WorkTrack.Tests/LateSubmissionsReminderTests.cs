using Application.Reminders;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The late-submissions reminder reaches the people who have not submitted a
/// timesheet for the most recent week whose submission deadline has passed —
/// the same week the daily attendance report's "Timesheet not submitted"
/// section reads (<c>ReminderDispatcher.LatestTimesheetWeekPastDeadline</c>).
///
/// It used to email only whoever had a <see cref="TimesheetStatus.Draft"/> row,
/// for any week at all. Somebody who never started the week's sheet — the usual
/// way a timesheet goes unsubmitted — heard nothing, while a draft begun that
/// morning for the current week was nagged about before it was due. On the
/// deployed host that read as the reminder not working.
/// </summary>
public class LateSubmissionsReminderTests
{
    private const int DepartmentId = 1;

    // Monday 00:00 UTC as the deadline makes the current week the week past its
    // deadline on any day of the week, so the seeded rows below sit in the week
    // the reminder reads. Every weekday works and there is no holiday country,
    // so today is always a working day.
    private static AppSettings Settings(bool emailEnabled = true) => new()
    {
        EmailNotificationsEnabled = emailEnabled,
        WorkingDays = "custom",
        WorkingDaysCustom = "sun,mon,tue,wed,thu,fri,sat",
        TimeZoneId = "UTC",
        TimesheetSubmissionDeadlineDay = "mon",
        TimesheetSubmissionDeadlineTime = "00:00",
    };

    private static DateTime CurrentWeekStart()
    {
        var today = DateTime.UtcNow.Date;
        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
    }

    private static Timesheet WeekTimesheet(string profileId, TimesheetStatus status, int weeksFromNow = 0) => new()
    {
        EmployeeProfileId = profileId,
        DepartmentId = DepartmentId,
        PeriodStart = CurrentWeekStart().AddDays(7 * weeksFromNow),
        PeriodEnd = CurrentWeekStart().AddDays(7 * weeksFromNow + 6),
        Status = status,
    };

    private static void SeedPerson(AppDbContext db, string key, string displayName, int? departmentId, bool isActive = true, DateOnly? startedOn = null)
    {
        db.Users.Add(new User { Id = $"{key}-u", UserName = key, DisplayName = displayName, Email = $"{key}@example.com", IsActive = isActive });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"{key}-p", UserId = $"{key}-u", DepartmentId = departmentId, EmploymentStartDate = startedOn });
    }

    private static AppDbContext SeedWorld() => SeedWorld(TestDb.Create());

    private static AppDbContext SeedWorld(AppDbContext db)
    {
        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });

        var adminRole = new Role { Id = "r-admin", Name = AppRoles.SystemAdministrator, NormalizedName = AppRoles.SystemAdministrator.ToUpperInvariant() };
        db.Roles.Add(adminRole);

        // Eve never started the week's sheet.
        SeedPerson(db, "eve", "Eve Employee", DepartmentId);

        // Bob started it and left it in draft.
        SeedPerson(db, "bob", "Bob Employee", DepartmentId);
        db.Timesheets.Add(WeekTimesheet("bob-p", TimesheetStatus.Draft));

        // Cara submitted the due week, and has begun next week's sheet — which is not due yet.
        SeedPerson(db, "cara", "Cara Employee", DepartmentId);
        db.Timesheets.Add(WeekTimesheet("cara-p", TimesheetStatus.Submitted));
        db.Timesheets.Add(WeekTimesheet("cara-p", TimesheetStatus.Draft, weeksFromNow: 1));

        // Dan's was approved already.
        SeedPerson(db, "dan", "Dan Employee", DepartmentId);
        db.Timesheets.Add(WeekTimesheet("dan-p", TimesheetStatus.Approved));

        // Rex's was rejected and has not been resubmitted.
        SeedPerson(db, "rex", "Rex Employee", DepartmentId);
        db.Timesheets.Add(WeekTimesheet("rex-p", TimesheetStatus.Rejected));

        // Ada runs the system; she files no timesheet anyone chases.
        SeedPerson(db, "ada", "Ada System Administrator", departmentId: null);
        db.UserRoles.Add(new UserRole { UserId = "ada-u", RoleId = adminRole.Id });

        // Lee has left.
        SeedPerson(db, "lee", "Lee Leaver", DepartmentId, isActive: false);

        // Nia joins next week; there was no week of hers to log.
        SeedPerson(db, "nia", "Nia Newcomer", DepartmentId,
            startedOn: DateOnly.FromDateTime(CurrentWeekStart().AddDays(7)));

        db.SaveChanges();
        return db;
    }

    private static ReminderDispatcher DispatcherFor(AppDbContext db, FakeEmailService email) =>
        new(db, email, NullLogger<ReminderDispatcher>.Instance);

    private static async Task<FakeEmailService> RunAsync(AppDbContext db, AppSettings? settings = null)
    {
        var email = new FakeEmailService();
        var dispatched = await DispatcherFor(db, email)
            .DispatchAsync(ReminderDispatcher.LateSubmissions, settings ?? Settings(), CancellationToken.None);
        Assert.True(dispatched);
        return email;
    }

    [Fact]
    public async Task Somebody_who_never_started_the_weeks_sheet_is_reminded()
    {
        using var db = SeedWorld();
        var email = await RunAsync(db);

        var eve = Assert.Single(email.Sent, m => m.Recipient == "eve@example.com");
        Assert.Contains("not been submitted", eve.HtmlBody);
        Assert.Contains($"{CurrentWeekStart():dd MMM yyyy}", eve.HtmlBody);
    }

    [Fact]
    public async Task A_draft_for_the_due_week_is_reminded_as_a_draft()
    {
        using var db = SeedWorld();
        var email = await RunAsync(db);

        var bob = Assert.Single(email.Sent, m => m.Recipient == "bob@example.com");
        Assert.Contains("draft", bob.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_rejected_sheet_for_the_due_week_is_reminded_to_resubmit()
    {
        using var db = SeedWorld();
        var email = await RunAsync(db);

        var rex = Assert.Single(email.Sent, m => m.Recipient == "rex@example.com");
        Assert.Contains("rejected", rex.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Whoever_submitted_the_due_week_is_left_alone_whatever_else_is_in_draft()
    {
        using var db = SeedWorld();
        var email = await RunAsync(db);

        Assert.DoesNotContain(email.Sent, m => m.Recipient == "cara@example.com");
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "dan@example.com");
    }

    [Fact]
    public async Task Administrators_leavers_and_people_who_had_not_joined_yet_are_not_reminded()
    {
        using var db = SeedWorld();
        var email = await RunAsync(db);

        Assert.DoesNotContain(email.Sent, m => m.Recipient == "ada@example.com");
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "lee@example.com");
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "nia@example.com");
    }

    [Fact]
    public async Task Exactly_the_three_who_owe_the_week_are_emailed_once_each()
    {
        using var db = SeedWorld();
        var email = await RunAsync(db);

        Assert.Equal(
            new[] { "bob@example.com", "eve@example.com", "rex@example.com" },
            email.Sent.Select(m => m.Recipient).OrderBy(r => r).ToArray());
    }

    // The in-memory provider runs any LINQ; the query compares a nullable DateOnly
    // column and walks the role join, so it is proven on a relational provider too.
    [Fact]
    public async Task The_same_three_are_found_over_a_relational_provider()
    {
        await using var db = SeedWorld(await TransactionalTestDb.CreateAsync());
        var email = await RunAsync(db);

        Assert.Equal(
            new[] { "bob@example.com", "eve@example.com", "rex@example.com" },
            email.Sent.Select(m => m.Recipient).OrderBy(r => r).ToArray());
    }

    [Fact]
    public async Task Nothing_is_sent_when_email_notifications_are_off()
    {
        using var db = SeedWorld();
        var email = await RunAsync(db, Settings(emailEnabled: false));

        Assert.Empty(email.Sent);
    }
}
