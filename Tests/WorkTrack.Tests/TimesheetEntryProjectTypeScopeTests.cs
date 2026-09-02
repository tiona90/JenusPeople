using System.Security.Claims;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// An entry records which kind of engagement the work was — the project's own
/// classification narrowed to one type per row. The picker in the timesheet UI
/// only offers types the project carries, but the picker is not the rule; these
/// pin the server side, which is what stops a hand-made request logging
/// "Support" hours against a project that is only ever a Task.
///
/// The two fallbacks matter as much as the rule: a project carrying no types is
/// unclassified rather than broken, and an entry with no type at all is how
/// every row that predates this field stays valid.
///
/// The last three cases cover the same-day rule. Before per-entry types it was
/// one entry per project per date; a project fielding both Support and Inquiry
/// work in one day is exactly what the field is for, so the check now keys on
/// project *and* type — while two untyped rows on one project still collide as
/// they always did.
/// </summary>
public class TimesheetEntryProjectTypeScopeTests
{
    private const string OwnerUserId = "owner-u";
    private const string OwnerProfileId = "owner-p";
    private const string TimesheetId = "ts-1";

    private static readonly DateTime EntryDate = new(2024, 1, 2);

    private const int ClassifiedProjectId = 1;
    private const int UnclassifiedProjectId = 2;
    private const int SupportId = 10;
    private const int InquiryId = 20;
    private const int IssueId = 30;

    /// <summary>
    /// One Draft timesheet owned by owner-p, and two projects: project 1 is
    /// classified as Support and Inquiry, project 2 carries no types at all.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = ClassifiedProjectId, Name = "Apollo", Code = "APL", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });
        db.Projects.Add(new Project { Id = UnclassifiedProjectId, Name = "Borealis", Code = "BOR", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });

        db.ProjectTypes.Add(new ProjectType { Id = SupportId, Name = "Support" });
        db.ProjectTypes.Add(new ProjectType { Id = InquiryId, Name = "Inquiry" });
        db.ProjectTypes.Add(new ProjectType { Id = IssueId, Name = "Issue" });
        db.ProjectTypeAssignments.Add(new ProjectTypeAssignment { ProjectId = ClassifiedProjectId, ProjectTypeId = SupportId });
        db.ProjectTypeAssignments.Add(new ProjectTypeAssignment { ProjectId = ClassifiedProjectId, ProjectTypeId = InquiryId });

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

    private static CreateEntryRequest EntryOn(int projectId, int? projectTypeId, decimal hours = 4m) => new()
    {
        ProjectId = projectId,
        Date = EntryDate,
        HoursWorked = hours,
        ProjectTypeId = projectTypeId,
    };

    private static int StatusOf(ActionResult<TimesheetEntry> result) => result.Result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? 0,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => 0,
    };

    [Fact]
    public async Task Type_the_project_is_not_classified_as_is_rejected()
    {
        using var db = SeedWorld();

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            ControllerFor(db).AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, IssueId), CancellationToken.None));

        Assert.Contains("type", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Type_the_project_is_classified_as_is_accepted()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, SupportId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Any_type_is_accepted_when_the_project_carries_none()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(UnclassifiedProjectId, IssueId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task An_entry_with_no_type_at_all_is_still_accepted()
    {
        using var db = SeedWorld();

        var result = await ControllerFor(db)
            .AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, null), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Same_project_on_the_same_day_is_accepted_under_a_different_type()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db);

        await controller.AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, SupportId, 3m), CancellationToken.None);
        var result = await controller.AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, InquiryId, 2m), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Same_project_and_type_on_the_same_day_is_still_rejected()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db);

        await controller.AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, SupportId, 3m), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, SupportId, 2m), CancellationToken.None));

        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Same_project_twice_on_the_same_day_with_no_type_is_still_rejected()
    {
        using var db = SeedWorld();
        var controller = ControllerFor(db);

        await controller.AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, null, 3m), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.AddEntry(TimesheetId, EntryOn(ClassifiedProjectId, null, 2m), CancellationToken.None));

        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
