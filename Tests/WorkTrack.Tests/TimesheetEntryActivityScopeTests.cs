using System.Security.Claims;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A project can narrow the org-wide activity catalogue to the activities it
/// actually does. The timesheet UI filters its dropdown accordingly, but the
/// dropdown is not the rule — these pin the server side, which is what stops a
/// hand-made request logging "Design" hours against a build-only project.
///
/// The fallback matters as much as the rule: every project that existed before
/// this feature has no assignments, and must keep accepting any active activity
/// rather than silently losing the field.
/// </summary>
public class TimesheetEntryActivityScopeTests
{
    private const string OwnerUserId = "owner-u";
    private const string OwnerProfileId = "owner-p";
    private const string TimesheetId = "ts-1";

    private static readonly DateTime EntryDate = new(2024, 1, 2);

    private const int NarrowedProjectId = 1;
    private const int OpenProjectId = 2;
    private const int DevelopmentId = 10;
    private const int DesignId = 20;

    /// <summary>
    /// One Draft timesheet owned by owner-p, and two projects: project 1 has
    /// narrowed itself to Development, project 2 has assigned nothing.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = NarrowedProjectId, Name = "Apollo", Code = "APL", DepartmentId = 1 });
        db.Projects.Add(new Project { Id = OpenProjectId, Name = "Borealis", Code = "BOR", DepartmentId = 1 });

        db.ProjectActivityTypes.Add(new ProjectActivityType { Id = DevelopmentId, Name = "Development" });
        db.ProjectActivityTypes.Add(new ProjectActivityType { Id = DesignId, Name = "Design" });
        db.ProjectActivityAssignments.Add(new ProjectActivityAssignment
        {
            ProjectId = NarrowedProjectId,
            ActivityTypeId = DevelopmentId,
        });

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

    private static CreateEntryRequest EntryOn(int projectId, int? activityTypeId) => new()
    {
        ProjectId = projectId,
        Date = EntryDate,
        HoursWorked = 4m,
        ActivityTypeId = activityTypeId,
    };

    private static int StatusOf(ActionResult<TimesheetEntry> result) => result.Result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? 0,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => 0,
    };

    [Fact]
    public async Task Activity_the_project_has_not_assigned_is_rejected()
    {
        using var db = SeedWorld();

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            ControllerFor(db).AddEntry(TimesheetId, EntryOn(NarrowedProjectId, DesignId), CancellationToken.None));

        Assert.Contains("activity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Activity_the_project_has_assigned_is_accepted()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(NarrowedProjectId, DevelopmentId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Any_activity_is_accepted_when_the_project_has_assigned_none()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(OpenProjectId, DesignId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task An_entry_with_no_activity_at_all_is_still_accepted()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(NarrowedProjectId, null), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }
}
