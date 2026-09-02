using System.Security.Claims;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// An entry records which part of the product the work was done on, narrowed to
/// the components its project is made up of — the same relationship the activity
/// has to the project, one column over. The picker filters itself, but the
/// picker is not the rule; these pin the server side.
///
/// Both fallbacks match the activity's: a project that has declared no
/// components accepts any (which is every project predating component
/// assignment), and an entry with no component is always valid.
///
/// The last two cases cover the same-day rule, which now keys on project, type
/// *and* component — 2h on DM and 3h on Lasernet for one project in one day are
/// two facts, not a duplicate.
/// </summary>
public class TimesheetEntryComponentScopeTests
{
    private const string OwnerUserId = "owner-u";
    private const string OwnerProfileId = "owner-p";
    private const string TimesheetId = "ts-1";

    private static readonly DateTime EntryDate = new(2024, 1, 2);

    private const int NarrowedProjectId = 1;
    private const int OpenProjectId = 2;
    private const int DmId = 10;
    private const int LasernetId = 20;
    private const int JDocsId = 30;

    /// <summary>
    /// One Draft timesheet owned by owner-p, and two projects: project 1 is made
    /// up of DM and Lasernet, project 2 has declared no components.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = NarrowedProjectId, Name = "Apollo", Code = "APL", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });
        db.Projects.Add(new Project { Id = OpenProjectId, Name = "Borealis", Code = "BOR", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });

        db.ProjectComponents.Add(new ProjectComponent { Id = DmId, Name = "DM" });
        db.ProjectComponents.Add(new ProjectComponent { Id = LasernetId, Name = "Lasernet" });
        db.ProjectComponents.Add(new ProjectComponent { Id = JDocsId, Name = "jDocs" });
        db.ProjectComponentAssignments.Add(new ProjectComponentAssignment { ProjectId = NarrowedProjectId, ComponentId = DmId });
        db.ProjectComponentAssignments.Add(new ProjectComponentAssignment { ProjectId = NarrowedProjectId, ComponentId = LasernetId });

        db.Users.Add(new User { Id = OwnerUserId, UserName = "owner", Email = "owner@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = OwnerProfileId, UserId = OwnerUserId, DepartmentId = 1 });

        db.Timesheets.Add(new Timesheet
        {
            Id = TimesheetId,
            EmployeeProfileId = OwnerProfileId,
            DepartmentId = 1,
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 7),
            Status = TimesheetStatus.Draft,
        });

        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static TimesheetEntriesController ControllerFor(AppDbContext db)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, OwnerUserId)], "Test")),
        };

        return new TimesheetEntriesController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static CreateEntryRequest EntryOn(int projectId, int? componentId, decimal hours = 4m) => new()
    {
        ProjectId = projectId,
        Date = EntryDate,
        HoursWorked = hours,
        ProjectComponentId = componentId,
    };

    private static int StatusOf(ActionResult<TimesheetEntry> result) => result.Result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? 0,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => 0,
    };

    [Fact]
    public async Task Component_the_project_is_not_made_up_of_is_rejected()
    {
        using var db = SeedWorld();

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            ControllerFor(db).AddEntry(TimesheetId, EntryOn(NarrowedProjectId, JDocsId), CancellationToken.None));

        Assert.Contains("component", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Component_the_project_has_declared_is_accepted()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(NarrowedProjectId, DmId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Any_component_is_accepted_when_the_project_has_declared_none()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(OpenProjectId, JDocsId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task An_entry_with_no_component_at_all_is_still_accepted()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(NarrowedProjectId, null), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Same_project_on_the_same_day_is_accepted_under_a_different_component()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db);

        await controller.AddEntry(TimesheetId, EntryOn(NarrowedProjectId, DmId, 3m), CancellationToken.None);
        var result = await controller.AddEntry(TimesheetId, EntryOn(NarrowedProjectId, LasernetId, 2m), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Same_project_and_component_on_the_same_day_is_still_rejected()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db);

        await controller.AddEntry(TimesheetId, EntryOn(NarrowedProjectId, DmId, 3m), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.AddEntry(TimesheetId, EntryOn(NarrowedProjectId, DmId, 2m), CancellationToken.None));

        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
