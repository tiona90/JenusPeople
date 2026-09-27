using Application.Attendance.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Nobody is expected to check in on a day the Working Week does not work, so the
/// company dashboard's "N not checked in" issue and the feed's synthetic "Not
/// checked in" rows stay silent on a weekend and on a public holiday. They used to
/// ask only whether the local clock was an hour past the start, so every Saturday
/// and Sunday morning HR was told the whole company had failed to turn up.
/// </summary>
public class NotCheckedInFollowsWorkingWeekTests
{
    private static readonly DateTime Sunday = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Monday = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

    private static AppDbContext SeedWorld(string? holidayCountry = null)
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC", WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00",
            WorkingDays = "mon-fri", HolidayCountryCode = holidayCountry,
        });
        db.Departments.Add(new Department { Id = 1, Name = "Finance", Code = "FIN" });
        foreach (var key in new[] { "ann", "bob" })
        {
            db.Users.Add(new User { Id = $"u-{key}", UserName = key, DisplayName = key });
            db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"p-{key}", UserId = $"u-{key}", DepartmentId = 1 });
        }
        db.SaveChanges();
        return db;
    }

    private static async Task<Application.Attendance.DTOs.CompanyAttendanceDto> Company(AppDbContext db, DateTime now)
    {
        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query { NowUtc = now }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task A_weekend_morning_reports_nobody_as_not_checked_in()
    {
        using var db = SeedWorld();
        var company = await Company(db, Sunday);

        Assert.DoesNotContain(company.Issues, i => i.Title.Contains("not checked in"));
        Assert.DoesNotContain(company.Recent, r => r.Action == "Not checked in");
    }

    [Fact]
    public async Task A_public_holiday_reports_nobody_as_not_checked_in()
    {
        using var db = SeedWorld(holidayCountry: "CY");
        db.PublicHolidays.Add(new PublicHoliday { CountryCode = "CY", Year = 2026, Date = new DateTime(2026, 9, 28), EnglishName = "Test Day" });
        db.SaveChanges();

        var company = await Company(db, Monday);

        Assert.DoesNotContain(company.Issues, i => i.Title.Contains("not checked in"));
        Assert.DoesNotContain(company.Recent, r => r.Action == "Not checked in");
    }

    [Fact]
    public async Task A_working_morning_still_reports_the_absences()
    {
        using var db = SeedWorld();
        var company = await Company(db, Monday);

        Assert.Contains(company.Issues, i => i.Title == "2 not checked in (Finance)");
        Assert.Equal(2, company.Recent.Count(r => r.Action == "Not checked in"));
    }
}
