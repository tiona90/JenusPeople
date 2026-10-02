using Application.Reminders;
using Application.Settings.Support;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// No reminder goes out on a public holiday, however it is asked for. The
/// scheduler already refused to dispatch on one, but the on-demand
/// run-reminder endpoint bypasses the schedule, and the dispatcher repeated the
/// question only for check-in, check-out and the daily report — so a manual run
/// of pending-approvals, late-submissions, low-balance or the birthday digest
/// still sent on a bank holiday. <see cref="ReminderDispatcher.DispatchAsync(string, AppSettings, CancellationToken)"/>
/// now asks once, for every id.
/// </summary>
public class RemindersSkipPublicHolidaysTests
{
    private const string Country = "CY";
    private const string EmployeeEmail = "eve@example.com";

    // Every weekday works, so only the holiday can make today a day off.
    private static AppSettings Settings(string? country) => new()
    {
        EmailNotificationsEnabled = true,
        WorkingDays = "custom",
        WorkingDaysCustom = "sun,mon,tue,wed,thu,fri,sat",
        HolidayCountryCode = country,
        TimeZoneId = "UTC",
    };

    public static TheoryData<string> AllReminderIds()
    {
        var data = new TheoryData<string>();
        foreach (var r in ReminderDefaults.Create()) data.Add(r.Id);
        return data;
    }

    private static AppDbContext SeedWorld(bool todayIsHoliday)
    {
        var db = TestDb.Create();
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Users.Add(new User { Id = "employee-u", UserName = "employee-u", DisplayName = "Eve Employee", Email = EmployeeEmail });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "employee-p", UserId = "employee-u", DepartmentId = 1 });

        if (todayIsHoliday)
        {
            var today = DateTime.UtcNow.Date;
            db.PublicHolidays.Add(new PublicHoliday
            {
                CountryCode = Country, Year = today.Year, Date = today,
                LocalName = "Holiday", EnglishName = "Holiday", CachedAt = DateTime.UtcNow,
            });
        }

        db.SaveChanges();
        return db;
    }

    private static ReminderDispatcher DispatcherFor(AppDbContext db, FakeEmailService email) =>
        new(db, email, NullLogger<ReminderDispatcher>.Instance);

    [Fact]
    public async Task On_a_working_day_the_check_in_reminder_reaches_the_employee()
    {
        using var db = SeedWorld(todayIsHoliday: false);
        var email = new FakeEmailService();

        var dispatched = await DispatcherFor(db, email)
            .DispatchAsync(ReminderDispatcher.CheckInReminder, Settings(Country), CancellationToken.None);

        Assert.True(dispatched);
        Assert.Contains(email.Sent, m => m.Recipient == EmployeeEmail);
    }

    [Theory]
    [MemberData(nameof(AllReminderIds))]
    public async Task On_a_public_holiday_no_reminder_is_sent(string reminderId)
    {
        using var db = SeedWorld(todayIsHoliday: true);
        var email = new FakeEmailService();

        var dispatched = await DispatcherFor(db, email)
            .DispatchAsync(reminderId, Settings(Country), CancellationToken.None);

        Assert.False(dispatched);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_holiday_of_another_country_does_not_stop_the_reminder()
    {
        using var db = SeedWorld(todayIsHoliday: true);
        var email = new FakeEmailService();

        var dispatched = await DispatcherFor(db, email)
            .DispatchAsync(ReminderDispatcher.CheckInReminder, Settings("GR"), CancellationToken.None);

        Assert.True(dispatched);
        Assert.Contains(email.Sent, m => m.Recipient == EmployeeEmail);
    }
}
