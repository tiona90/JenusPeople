using Application.Reminders;
using Application.Settings.DTOs;
using Application.Settings.Support;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A reminder goes out once a day, whatever happens to the process. "Already
/// sent today" used to live in memory on the hosted service, so every restart of
/// the API re-sent the morning's reminders — fifteen copies of the
/// pending-approvals digest on one development day, and on IIS one more copy
/// per deploy, recycle or idle wake-up. <see cref="ReminderRunner"/> records each
/// send in <c>ReminderRuns</c> and reads it back, so a second process over the
/// same database sends nothing.
/// </summary>
public class ReminderRunnerTests
{
    private const string EmployeeEmail = "eve@example.com";

    // Wednesday 23 September 2026, read on a UTC clock.
    private static readonly DateTime WednesdayNineThirty = new(2026, 9, 23, 9, 30, 0, DateTimeKind.Utc);

    // Every day works and no holiday country is set; the check-in reminder is
    // the only one switched on, scheduled for 09:00 on an 08:00–17:00 day.
    private static AppSettings Settings(string checkInTime = "09:00") => new()
    {
        EmailNotificationsEnabled = true,
        WorkingDays = "custom",
        WorkingDaysCustom = "sun,mon,tue,wed,thu,fri,sat",
        HolidayCountryCode = null,
        TimeZoneId = "UTC",
        WorkingHoursStart = "08:00",
        WorkingHoursEnd = "17:00",
        RemindersJson = ReminderSerializer.ToJson(
            ReminderDefaults.Create().Select(r => new ReminderSettingDto
            {
                Id = r.Id,
                Enabled = r.Id == ReminderDispatcher.CheckInReminder,
                Time = r.Id == ReminderDispatcher.CheckInReminder ? checkInTime : r.Time,
                Frequency = r.Frequency,
            })),
    };

    private static AppDbContext SeedWorld(string dbName, AppSettings settings)
    {
        var db = TestDb.Create(dbName);
        db.AppSettings.Add(settings);
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Users.Add(new User { Id = "employee-u", UserName = "employee-u", DisplayName = "Eve Employee", Email = EmployeeEmail });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "employee-p", UserId = "employee-u", DepartmentId = 1 });
        db.SaveChanges();
        return db;
    }

    private static ReminderRunner RunnerFor(AppDbContext db, FakeEmailService email) =>
        new(db, new ReminderDispatcher(db, email, NullLogger<ReminderDispatcher>.Instance), NullLogger<ReminderRunner>.Instance);

    [Fact]
    public async Task A_due_reminder_is_sent_and_recorded()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = SeedWorld(dbName, Settings());
        var email = new FakeEmailService();

        var outcome = await RunnerFor(db, email).TickAsync(WednesdayNineThirty, refreshHolidays: true, CancellationToken.None);

        Assert.Empty(outcome.Failures);
        Assert.Single(email.Sent, m => m.Recipient == EmployeeEmail);
        var run = Assert.Single(db.ReminderRuns);
        Assert.Equal(ReminderDispatcher.CheckInReminder, run.ReminderId);
        Assert.Equal(new DateOnly(2026, 9, 23), run.RanOn);
        Assert.Equal("sent", run.Outcome);
    }

    [Fact]
    public async Task A_restarted_process_does_not_send_the_same_reminder_again()
    {
        var dbName = Guid.NewGuid().ToString();
        var email = new FakeEmailService();
        using (var firstProcess = SeedWorld(dbName, Settings()))
        {
            await RunnerFor(firstProcess, email).TickAsync(WednesdayNineThirty, true, CancellationToken.None);
        }
        Assert.Single(email.Sent);

        // A new context over the same database: what a fresh API process sees.
        using var secondProcess = TestDb.Create(dbName);
        await RunnerFor(secondProcess, email).TickAsync(WednesdayNineThirty.AddMinutes(5), true, CancellationToken.None);

        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task The_next_day_it_is_sent_again()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = SeedWorld(dbName, Settings());
        var email = new FakeEmailService();
        var runner = RunnerFor(db, email);

        await runner.TickAsync(WednesdayNineThirty, true, CancellationToken.None);
        await runner.TickAsync(WednesdayNineThirty.AddDays(1), true, CancellationToken.None);

        Assert.Equal(2, email.Sent.Count);
        Assert.Equal(new DateOnly(2026, 9, 24), Assert.Single(db.ReminderRuns).RanOn);
    }

    [Fact]
    public async Task A_check_in_reminder_missed_until_after_the_working_day_is_dropped_not_caught_up()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = SeedWorld(dbName, Settings());
        var email = new FakeEmailService();

        // The API comes back at 18:30, after the 17:00 end of the working day.
        await RunnerFor(db, email).TickAsync(new DateTime(2026, 9, 23, 18, 30, 0, DateTimeKind.Utc), true, CancellationToken.None);

        Assert.Empty(email.Sent);
        var run = Assert.Single(db.ReminderRuns);
        Assert.StartsWith("skipped", run.Outcome);
        Assert.Equal(new DateOnly(2026, 9, 23), run.RanOn);
    }

    [Fact]
    public async Task A_check_in_reminder_missed_during_the_working_day_is_caught_up()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = SeedWorld(dbName, Settings());
        var email = new FakeEmailService();

        // The API comes back at 14:00; whoever has not checked in can still be nudged.
        await RunnerFor(db, email).TickAsync(new DateTime(2026, 9, 23, 14, 0, 0, DateTimeKind.Utc), true, CancellationToken.None);

        Assert.Single(email.Sent, m => m.Recipient == EmployeeEmail);
    }

    [Fact]
    public async Task Nothing_is_sent_before_the_scheduled_time()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = SeedWorld(dbName, Settings());
        var email = new FakeEmailService();

        await RunnerFor(db, email).TickAsync(new DateTime(2026, 9, 23, 8, 59, 0, DateTimeKind.Utc), true, CancellationToken.None);

        Assert.Empty(email.Sent);
        Assert.Empty(db.ReminderRuns);
    }
}
