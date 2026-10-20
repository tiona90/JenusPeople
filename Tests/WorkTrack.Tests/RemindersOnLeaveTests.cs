using Application.Reminders;
using Domain;
using Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Somebody on approved leave is sent no reminder at all — the dispatcher drops
/// them in one place, so check-in, check-out and the timesheet chase all agree —
/// and, the working day before a leave that runs past the week's timesheet
/// deadline, they are asked to submit the week before they go
/// (<see cref="ReminderDispatcher.TimesheetBeforeLeave"/>).
/// </summary>
public class RemindersOnLeaveTests
{
    private const int DepartmentId = 1;

    // Every day is a working day, the org runs on UTC and there is no holiday
    // country, so "today" and "the next working day" are today and tomorrow.
    private static AppSettings Settings(string deadlineDay = "fri", string deadlineTime = "18:00") => new()
    {
        EmailNotificationsEnabled = true,
        WorkingDays = "custom",
        WorkingDaysCustom = "sun,mon,tue,wed,thu,fri,sat",
        TimeZoneId = "UTC",
        TimesheetSubmissionDeadlineDay = deadlineDay,
        TimesheetSubmissionDeadlineTime = deadlineTime,
    };

    private static DateTime Today => DateTime.UtcNow.Date;
    private static DateTime WeekStart => Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));

    private static void SeedPerson(AppDbContext db, string key)
    {
        db.Users.Add(new User { Id = $"{key}-u", UserName = key, DisplayName = key, Email = $"{key}@example.com", IsActive = true });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"{key}-p", UserId = $"{key}-u", DepartmentId = DepartmentId });
    }

    private static void SeedLeave(AppDbContext db, string key, DateTime start, DateTime end,
        LeaveDuration duration = LeaveDuration.Full, AnnualLeaveStatus status = AnnualLeaveStatus.Approved) =>
        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = $"{key}-u",
            EmployeeProfileId = $"{key}-p",
            DepartmentId = DepartmentId,
            StartDate = start,
            EndDate = end,
            Duration = duration,
            Status = status,
        });

    private static void CheckIn(AppDbContext db, string key) =>
        db.AttendanceEvents.Add(new AttendanceEvent { EmployeeProfileId = $"{key}-p", Type = AttendanceEventType.CheckIn, At = DateTime.UtcNow.AddMinutes(-1) });

    private static AppDbContext NewWorld()
    {
        var db = TestDb.Create();
        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });
        return db;
    }

    private static async Task<List<string>> RunAsync(AppDbContext db, string reminderId, AppSettings? settings = null)
    {
        db.SaveChanges();
        var email = new FakeEmailService();
        Assert.True(await new ReminderDispatcher(db, email, NullLogger<ReminderDispatcher>.Instance)
            .DispatchAsync(reminderId, settings ?? Settings(), CancellationToken.None));
        return email.Sent.Select(m => m.Recipient).OrderBy(r => r).ToList();
    }

    // ── nobody on leave is reminded ──────────────────────────────────────────

    [Fact]
    public async Task Check_in_reminder_skips_the_last_day_of_a_leave()
    {
        using var db = NewWorld();
        SeedPerson(db, "eve");
        SeedPerson(db, "lia");
        // Ends today: stored at midnight, so it used to read as over by morning.
        SeedLeave(db, "lia", Today.AddDays(-3), Today);

        Assert.Equal(["eve@example.com"], await RunAsync(db, ReminderDispatcher.CheckInReminder));
    }

    [Fact]
    public async Task Check_in_reminder_skips_a_morning_half_day_but_not_an_afternoon_one()
    {
        using var db = NewWorld();
        SeedPerson(db, "am");
        SeedPerson(db, "pm");
        SeedLeave(db, "am", Today, Today, LeaveDuration.HalfDayMorning);
        SeedLeave(db, "pm", Today, Today, LeaveDuration.HalfDayAfternoon);

        Assert.Equal(["pm@example.com"], await RunAsync(db, ReminderDispatcher.CheckInReminder));
    }

    [Fact]
    public async Task Check_out_reminder_skips_somebody_on_leave_today_even_if_checked_in()
    {
        using var db = NewWorld();
        SeedPerson(db, "eve");
        SeedPerson(db, "lia");
        CheckIn(db, "eve");
        CheckIn(db, "lia");
        SeedLeave(db, "lia", Today, Today.AddDays(2));

        Assert.Equal(["eve@example.com"], await RunAsync(db, ReminderDispatcher.CheckOutReminder));
    }

    [Fact]
    public async Task Leave_that_is_only_pending_does_not_silence_reminders()
    {
        using var db = NewWorld();
        SeedPerson(db, "eve");
        SeedLeave(db, "eve", Today, Today, status: AnnualLeaveStatus.Pending);

        Assert.Equal(["eve@example.com"], await RunAsync(db, ReminderDispatcher.CheckInReminder));
    }

    [Fact]
    public async Task Late_submissions_skips_somebody_on_leave_today()
    {
        using var db = NewWorld();
        SeedPerson(db, "eve");
        SeedPerson(db, "lia");
        SeedLeave(db, "lia", Today, Today.AddDays(5));

        // A Monday 00:00 deadline makes the current week the one past its deadline.
        var sent = await RunAsync(db, ReminderDispatcher.LateSubmissions, Settings("mon", "00:00"));

        Assert.Equal(["eve@example.com"], sent);
    }

    // ── timesheet-before-leave ───────────────────────────────────────────────

    [Fact]
    public async Task Somebody_leaving_tomorrow_past_the_deadline_is_asked_to_submit_first()
    {
        using var db = NewWorld();
        SeedPerson(db, "lia");
        // Two weeks from tomorrow: back well after this week's deadline, whatever today is.
        SeedLeave(db, "lia", Today.AddDays(1), Today.AddDays(14));

        var email = new FakeEmailService();
        db.SaveChanges();
        await new ReminderDispatcher(db, email, NullLogger<ReminderDispatcher>.Instance)
            .DispatchAsync(ReminderDispatcher.TimesheetBeforeLeave, Settings("sun", "23:59"), CancellationToken.None);

        var message = Assert.Single(email.Sent);
        Assert.Equal("lia@example.com", message.Recipient);
        Assert.Contains("before you go", message.HtmlBody);
        Assert.Contains($"{WeekStart:dd MMM yyyy}", message.HtmlBody);
        Assert.Contains($"{Today.AddDays(15):dddd dd MMM}", message.HtmlBody); // back on
    }

    [Fact]
    public async Task A_submitted_week_or_nobody_leaving_sends_nothing()
    {
        using var db = NewWorld();
        SeedPerson(db, "eve"); // not going anywhere
        SeedPerson(db, "sam");
        SeedLeave(db, "sam", Today.AddDays(1), Today.AddDays(14));
        db.Timesheets.Add(new Timesheet
        {
            EmployeeProfileId = "sam-p",
            DepartmentId = DepartmentId,
            PeriodStart = WeekStart,
            PeriodEnd = WeekStart.AddDays(6),
            Status = TimesheetStatus.Submitted,
        });

        Assert.Empty(await RunAsync(db, ReminderDispatcher.TimesheetBeforeLeave, Settings("sun", "23:59")));
    }

    [Fact]
    public async Task A_draft_or_rejected_week_still_counts_as_owing()
    {
        using var db = NewWorld();
        SeedPerson(db, "lia");
        SeedLeave(db, "lia", Today.AddDays(1), Today.AddDays(14));
        db.Timesheets.Add(new Timesheet
        {
            EmployeeProfileId = "lia-p",
            DepartmentId = DepartmentId,
            PeriodStart = WeekStart,
            PeriodEnd = WeekStart.AddDays(6),
            Status = TimesheetStatus.Draft,
        });

        Assert.Equal(["lia@example.com"], await RunAsync(db, ReminderDispatcher.TimesheetBeforeLeave, Settings("sun", "23:59")));
    }

    [Fact]
    public async Task Back_before_the_deadline_is_not_asked()
    {
        // Needs the day after tomorrow to fall inside this week, before a Sunday
        // 23:59 deadline; on a Saturday or Sunday there is no such day to test.
        if (Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;

        using var db = NewWorld();
        SeedPerson(db, "lia");
        SeedLeave(db, "lia", Today.AddDays(1), Today.AddDays(1)); // one day off, back the day after

        Assert.Empty(await RunAsync(db, ReminderDispatcher.TimesheetBeforeLeave, Settings("sun", "23:59")));
    }

    [Fact]
    public async Task A_half_day_tomorrow_is_not_going_on_leave()
    {
        using var db = NewWorld();
        SeedPerson(db, "lia");
        SeedLeave(db, "lia", Today.AddDays(1), Today.AddDays(1), LeaveDuration.HalfDayMorning);

        Assert.Empty(await RunAsync(db, ReminderDispatcher.TimesheetBeforeLeave, Settings("sun", "23:59")));
    }

    [Fact]
    public async Task Somebody_already_on_leave_today_is_not_asked()
    {
        using var db = NewWorld();
        SeedPerson(db, "lia");
        SeedLeave(db, "lia", Today, Today.AddDays(14));

        Assert.Empty(await RunAsync(db, ReminderDispatcher.TimesheetBeforeLeave, Settings("sun", "23:59")));
    }

    // The in-memory provider runs any LINQ; the leave query walks a navigation
    // for the email, so it is proven on a relational provider too.
    [Fact]
    public async Task The_same_rules_hold_over_a_relational_provider()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });
        db.LeaveTypes.Add(new LeaveType { Id = 1, Name = "Annual Leave" });
        SeedPerson(db, "eve");
        SeedPerson(db, "lia");
        SeedPerson(db, "sam");
        SeedLeave(db, "lia", Today.AddDays(-2), Today);
        SeedLeave(db, "sam", Today.AddDays(1), Today.AddDays(14));
        foreach (var leave in db.ChangeTracker.Entries<AnnualLeave>()) leave.Entity.LeaveTypeId = 1;

        Assert.Equal(["eve@example.com", "sam@example.com"], await RunAsync(db, ReminderDispatcher.CheckInReminder));
        Assert.Equal(["sam@example.com"], await RunAsync(db, ReminderDispatcher.TimesheetBeforeLeave, Settings("sun", "23:59")));
    }
}
