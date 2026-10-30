using System.Security.Claims;
using API.Controllers;
using Application.Timesheets.Support;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Entries are written only while the sheet is the employee's to edit — Draft or
/// Rejected. TimesheetAccess checked who was writing but never the sheet's status,
/// so a direct call could change the hours on a submitted or approved week (and
/// TotalHours with them) with no history and no re-review; only the editor's
/// client-side lock stood in the way. LogTaskTime already refused
/// (WeekSubmittedMessage); the entry actions now agree with it.
/// </summary>
public class TimesheetEntryStatusLockTests
{
    private const string OwnerUserId = "owner-u";
    private const string OwnerProfileId = "owner-p";
    private const string TimesheetId = "ts-1";
    private const string EntryId = "e-1";
    private static readonly DateTime EntryDate = new(2024, 1, 2);

    private static AppDbContext SeedWorld(TimesheetStatus status)
    {
        var db = TestDb.Create();
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = 1, Name = "Apollo", Code = "APL", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });
        db.Projects.Add(new Project { Id = 2, Name = "Borealis", Code = "BOR", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });
        db.Users.Add(new User { Id = OwnerUserId, UserName = "owner", Email = "owner@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = OwnerProfileId, UserId = OwnerUserId, DepartmentId = 1 });
        db.Timesheets.Add(new Timesheet
        {
            Id = TimesheetId, EmployeeProfileId = OwnerProfileId, DepartmentId = 1,
            PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7),
            TotalHours = 8m, Status = status,
        });
        db.TimesheetEntries.Add(new TimesheetEntry { Id = EntryId, TimesheetId = TimesheetId, ProjectId = 1, Date = EntryDate, HoursWorked = 8m });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static TimesheetEntriesController OwnerController(AppDbContext db)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, OwnerUserId), new Claim(ClaimTypes.Role, AppRoles.Employee)], "Test")),
        };
        return new TimesheetEntriesController(db) { ControllerContext = new ControllerContext { HttpContext = httpContext } };
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? 0,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => 0,
    };

    private static CreateEntryRequest NewEntry() => new() { ProjectId = 2, Date = EntryDate, HoursWorked = 4m };

    private static TimesheetEntry EditedEntry() => new()
    {
        Id = EntryId, TimesheetId = TimesheetId, ProjectId = 1, Date = EntryDate, HoursWorked = 12m,
    };

    public static TheoryData<TimesheetStatus> LockedStatuses =>
        [TimesheetStatus.Submitted, TimesheetStatus.Approved, TimesheetStatus.Resubmitted];

    public static TheoryData<TimesheetStatus> EditableStatuses =>
        [TimesheetStatus.Draft, TimesheetStatus.Rejected];

    [Theory]
    [MemberData(nameof(LockedStatuses))]
    public async Task AddEntry_is_refused_once_the_sheet_is_submitted(TimesheetStatus status)
    {
        using var db = SeedWorld(status);

        var result = await OwnerController(db).AddEntry(TimesheetId, NewEntry(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf((IActionResult)result.Result!));
        Assert.Equal(1, db.TimesheetEntries.Count(e => e.TimesheetId == TimesheetId));
    }

    [Theory]
    [MemberData(nameof(LockedStatuses))]
    public async Task UpdateEntry_is_refused_once_the_sheet_is_submitted(TimesheetStatus status)
    {
        using var db = SeedWorld(status);

        var result = await OwnerController(db).UpdateEntry(TimesheetId, EntryId, EditedEntry(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal(8m, db.TimesheetEntries.Single(e => e.Id == EntryId).HoursWorked);
        Assert.Equal(8m, db.Timesheets.Single(t => t.Id == TimesheetId).TotalHours);
    }

    [Theory]
    [MemberData(nameof(LockedStatuses))]
    public async Task DeleteEntry_is_refused_once_the_sheet_is_submitted(TimesheetStatus status)
    {
        using var db = SeedWorld(status);

        var result = await OwnerController(db).DeleteEntry(TimesheetId, EntryId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.True(db.TimesheetEntries.Any(e => e.Id == EntryId));
    }

    [Theory]
    [MemberData(nameof(EditableStatuses))]
    public async Task Entries_are_still_written_on_a_draft_or_rejected_sheet(TimesheetStatus status)
    {
        using var db = SeedWorld(status);

        var result = await OwnerController(db).UpdateEntry(TimesheetId, EntryId, EditedEntry(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal(12m, db.TimesheetEntries.Single(e => e.Id == EntryId).HoursWorked);
    }

    [Fact]
    public void Only_draft_and_rejected_are_editable()
    {
        Assert.True(TimesheetAccess.IsEditable(TimesheetStatus.Draft));
        Assert.True(TimesheetAccess.IsEditable(TimesheetStatus.Rejected));
        Assert.False(TimesheetAccess.IsEditable(TimesheetStatus.Submitted));
        Assert.False(TimesheetAccess.IsEditable(TimesheetStatus.Approved));
        Assert.False(TimesheetAccess.IsEditable(TimesheetStatus.Resubmitted));
    }
}
