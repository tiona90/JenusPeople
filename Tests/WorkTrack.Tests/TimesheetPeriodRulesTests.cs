using System.Security.Claims;
using API.Controllers;
using Application.Timesheets.Commands;
using Application.Timesheets.Support;
using Application.Timesheets.Validators;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A timesheet is one Monday-to-Sunday week, and its entries fall inside it. The
/// unique index is on (EmployeeProfileId, PeriodStart), so a sheet starting on a
/// Tuesday sat beside the Monday one for the same days without tripping it; and an
/// entry dated outside its sheet's week was counted in TotalHours, dropped from the
/// daily breakdown, never shown by the editor — and deleted on its next save. The
/// editor and LogTaskTime only ever build Monday weeks; these are the server rules
/// behind them.
/// </summary>
public class TimesheetPeriodRulesTests
{
    private static async Task<List<string>> ValidateCreate(DateTime start, DateTime end)
    {
        using var db = TestDb.Create();
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = 1 });
        await db.SaveChangesAsync();
        var result = await new CreateTimesheetValidator(db).ValidateAsync(new CreateTimesheet.Command
        {
            RequestingUserId = "u1", PeriodStart = start, PeriodEnd = end,
        });
        return result.Errors.Select(e => e.ErrorMessage).ToList();
    }

    [Fact]
    public async Task A_monday_to_sunday_week_is_accepted() =>
        Assert.Empty(await ValidateCreate(new DateTime(2026, 10, 26), new DateTime(2026, 11, 1)));

    [Theory]
    [InlineData("2026-10-27", "2026-11-02")] // starts on a Tuesday
    [InlineData("2026-10-26", "2026-11-08")] // two weeks
    [InlineData("2026-10-26T09:30:00", "2026-11-01")] // not midnight
    public async Task Any_other_period_is_refused(string start, string end) =>
        Assert.Contains(CreateTimesheetValidator.WeekMessage,
            await ValidateCreate(DateTime.Parse(start), DateTime.Parse(end)));

    [Fact]
    public void An_entry_outside_its_week_is_refused()
    {
        var entry = new TimesheetEntry { Id = "e", ProjectId = 1, Date = new DateTime(2024, 1, 8), HoursWorked = 2 };

        var result = TimesheetEntryValidator.Validate(entry, [], today: new DateTime(2024, 2, 1),
            period: (new DateTime(2024, 1, 1), new DateTime(2024, 1, 7)));

        Assert.False(result.IsValid);
        Assert.Equal(TimesheetEntryValidator.OutsideWeekMessage, result.Error);
    }

    [Fact]
    public void An_entry_on_the_week_s_last_day_is_accepted()
    {
        var entry = new TimesheetEntry { Id = "e", ProjectId = 1, Date = new DateTime(2024, 1, 7), HoursWorked = 2 };

        var result = TimesheetEntryValidator.Validate(entry, [], today: new DateTime(2024, 2, 1),
            period: (new DateTime(2024, 1, 1), new DateTime(2024, 1, 7)));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public async Task The_entry_actions_refuse_a_date_outside_the_sheet()
    {
        using var db = TestDb.Create();
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = 1, Name = "Apollo", Code = "APL", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });
        db.Users.Add(new User { Id = "u1", UserName = "u1", Email = "u1@t.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = 1 });
        db.Timesheets.Add(new Timesheet
        {
            Id = "ts", EmployeeProfileId = "p1", DepartmentId = 1, Status = TimesheetStatus.Draft,
            PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7),
        });
        await db.SaveChangesAsync();

        var controller = new TimesheetEntriesController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "u1"), new Claim(ClaimTypes.Role, AppRoles.Employee)], "Test")),
                },
            },
        };

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => controller.AddEntry(
            "ts", new CreateEntryRequest { ProjectId = 1, Date = new DateTime(2024, 1, 9), HoursWorked = 4 }, CancellationToken.None));

        Assert.Equal(TimesheetEntryValidator.OutsideWeekMessage, thrown.Message);
        Assert.Empty(db.TimesheetEntries);
    }
}
