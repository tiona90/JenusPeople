using System.Security.Claims;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The write actions on TimesheetEntriesController used to carry nothing but
/// [Authorize]: any signed-in user who knew (or guessed) a timesheet id could
/// add, edit or delete entries on someone else's timesheet — the hours their
/// colleague gets paid for. These pin the ownership rule per action: the owning
/// employee may write, an unrelated employee is refused with 403, an Admin may
/// write. Manager scope and the missing-timesheet case are covered at the end.
/// </summary>
public class TimesheetEntryOwnershipTests
{
    private const string OwnerUserId = "owner-u";
    private const string OwnerProfileId = "owner-p";
    private const string OutsiderUserId = "outsider-u";
    private const string AdminUserId = "admin-u";
    private const string TimesheetId = "ts-1";
    private const string EntryId = "e-1";

    // Comfortably in the past: TimesheetEntryValidator rejects future dates.
    private static readonly DateTime EntryDate = new(2024, 1, 2);

    /// <summary>
    /// One Draft timesheet owned by owner-p (department 1) holding one 8-hour
    /// entry. outsider-p sits in department 2 and manages nobody.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Departments.Add(new Department { Id = 2, Name = "Sales", Code = "SLS" });
        db.Projects.Add(new Project { Id = 1, Name = "Apollo", Code = "APL", DepartmentId = 1 });
        db.Projects.Add(new Project { Id = 2, Name = "Borealis", Code = "BOR", DepartmentId = 1 });

        db.Users.Add(new User { Id = OwnerUserId, UserName = "owner", Email = "owner@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = OwnerProfileId, UserId = OwnerUserId, DepartmentId = 1 });

        db.Users.Add(new User { Id = OutsiderUserId, UserName = "outsider", Email = "outsider@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "outsider-p", UserId = OutsiderUserId, DepartmentId = 2 });

        db.Users.Add(new User { Id = AdminUserId, UserName = "admin", Email = "admin@test.local" });

        db.Timesheets.Add(new Timesheet
        {
            Id = TimesheetId,
            EmployeeProfileId = OwnerProfileId,
            DepartmentId = 1,
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 7),
            TotalHours = 8m,
            Status = TimesheetStatus.Draft,
        });
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            Id = EntryId,
            TimesheetId = TimesheetId,
            ProjectId = 1,
            Date = EntryDate,
            HoursWorked = 8m,
        });

        db.SaveChanges();
        // UpdateEntry attaches its own TimesheetEntry instance; leaving the seeded
        // one tracked would make EF reject a second instance for the same key.
        db.ChangeTracker.Clear();
        return db;
    }

    private static TimesheetEntriesController ControllerFor(AppDbContext db, string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        };

        return new TimesheetEntriesController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    // NoContent/NotFound are StatusCodeResults; Created and the ApiErrorResponse
    // bodies are ObjectResults that already carry their status.
    private static int StatusOf(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? 0,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => 0,
    };

    private static int StatusOf(ActionResult<TimesheetEntry> result) => StatusOf((IActionResult)result.Result!);

    private static CreateEntryRequest NewEntryRequest() => new()
    {
        // A different project on the same date — the validator rejects a repeat
        // of project 1, which would mask an authorization failure as a 400.
        ProjectId = 2,
        Date = EntryDate,
        HoursWorked = 4m,
    };

    private static TimesheetEntry EditedEntry() => new()
    {
        Id = EntryId,
        TimesheetId = TimesheetId,
        ProjectId = 1,
        Date = EntryDate,
        HoursWorked = 6m,
    };

    // AddEntry

    [Fact]
    public async Task AddEntry_is_allowed_for_the_owner()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, OwnerUserId, AppRoles.Employee);

        var result = await controller.AddEntry(TimesheetId, NewEntryRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        Assert.Equal(2, db.TimesheetEntries.Count(e => e.TimesheetId == TimesheetId));
    }

    [Fact]
    public async Task AddEntry_is_refused_for_a_non_owner()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, OutsiderUserId, AppRoles.Employee);

        var result = await controller.AddEntry(TimesheetId, NewEntryRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        // A refusal that still wrote would be no fix at all.
        Assert.Equal(1, db.TimesheetEntries.Count(e => e.TimesheetId == TimesheetId));
    }

    [Fact]
    public async Task AddEntry_is_allowed_for_an_admin()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, AdminUserId, AppRoles.Admin);

        var result = await controller.AddEntry(TimesheetId, NewEntryRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        Assert.Equal(2, db.TimesheetEntries.Count(e => e.TimesheetId == TimesheetId));
    }

    // UpdateEntry

    [Fact]
    public async Task UpdateEntry_is_allowed_for_the_owner()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, OwnerUserId, AppRoles.Employee);

        var result = await controller.UpdateEntry(TimesheetId, EntryId, EditedEntry(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal(6m, db.TimesheetEntries.Single(e => e.Id == EntryId).HoursWorked);
    }

    [Fact]
    public async Task UpdateEntry_is_refused_for_a_non_owner()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, OutsiderUserId, AppRoles.Employee);

        var result = await controller.UpdateEntry(TimesheetId, EntryId, EditedEntry(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        Assert.Equal(8m, db.TimesheetEntries.Single(e => e.Id == EntryId).HoursWorked);
    }

    [Fact]
    public async Task UpdateEntry_is_allowed_for_an_admin()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, AdminUserId, AppRoles.Admin);

        var result = await controller.UpdateEntry(TimesheetId, EntryId, EditedEntry(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal(6m, db.TimesheetEntries.Single(e => e.Id == EntryId).HoursWorked);
    }

    // DeleteEntry

    [Fact]
    public async Task DeleteEntry_is_allowed_for_the_owner()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, OwnerUserId, AppRoles.Employee);

        var result = await controller.DeleteEntry(TimesheetId, EntryId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.False(db.TimesheetEntries.Any(e => e.Id == EntryId));
    }

    [Fact]
    public async Task DeleteEntry_is_refused_for_a_non_owner()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, OutsiderUserId, AppRoles.Employee);

        var result = await controller.DeleteEntry(TimesheetId, EntryId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        Assert.True(db.TimesheetEntries.Any(e => e.Id == EntryId));
    }

    [Fact]
    public async Task DeleteEntry_is_allowed_for_an_admin()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, AdminUserId, AppRoles.Admin);

        var result = await controller.DeleteEntry(TimesheetId, EntryId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.False(db.TimesheetEntries.Any(e => e.Id == EntryId));
    }

    // Manager scope

    [Fact]
    public async Task A_manager_may_write_only_inside_their_own_scope()
    {
        using var db = SeedWorld();
        // Manager over department 1 — the owner's department.
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mgr-in-p", UserId = "mgr-in-u", DepartmentId = 1 });
        // Manager over department 2, with no claim on the owner.
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mgr-out-p", UserId = "mgr-out-u", DepartmentId = 2 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var outsideManager = ControllerFor(db, "mgr-out-u", AppRoles.Manager);
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(await outsideManager.DeleteEntry(TimesheetId, EntryId, CancellationToken.None)));
        Assert.True(db.TimesheetEntries.Any(e => e.Id == EntryId));

        var departmentManager = ControllerFor(db, "mgr-in-u", AppRoles.Manager);
        Assert.Equal(
            StatusCodes.Status204NoContent,
            StatusOf(await departmentManager.DeleteEntry(TimesheetId, EntryId, CancellationToken.None)));
    }

    // Unknown timesheet

    [Fact]
    public async Task A_timesheet_that_does_not_exist_is_a_404_not_a_403()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db, OwnerUserId, AppRoles.Employee);

        var result = await controller.AddEntry("no-such-timesheet", NewEntryRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }
}
