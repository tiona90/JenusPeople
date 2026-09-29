using System.Security.Claims;
using API.Controllers;
using Application.Timesheets.Support;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A row may name one of its owner's open tasks on the same project. The rule is
/// asked only when the task or the project changes, so closing a task or taking
/// someone off it never strands the hours already logged.
/// </summary>
public class TimesheetEntryTaskRuleTests
{
    private const string OwnerUserId = "owner-u";
    private const string OtherUserId = "other-u";
    private const string HrUserId = "hr-u";
    private const string TimesheetId = "ts-1";
    private const int ProjectA = 1;
    private const int ProjectB = 2;
    private const int OpenTask = 100;
    private const int DoneTask = 101;
    private const int SomeoneElsesTask = 102;
    private const int OtherProjectTask = 103;
    private const int WaitingTask = 104;
    private static readonly DateTime Day = new(2024, 1, 2);

    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = ProjectA, Name = "Apollo", Code = "APL" });
        db.Projects.Add(new Project { Id = ProjectB, Name = "Borealis", Code = "BOR" });
        db.Roles.Add(new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() });
        foreach (var id in new[] { OwnerUserId, OtherUserId, HrUserId })
            db.Users.Add(new User { Id = id, UserName = id, Email = $"{id}@t" });
        db.UserRoles.Add(new UserRole { UserId = HrUserId, RoleId = "r-hr" });
        db.UserDepartments.Add(new UserDepartment { UserId = HrUserId, DepartmentId = 1 });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "owner-p", UserId = OwnerUserId, DepartmentId = 1 });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "hr-p", UserId = HrUserId, DepartmentId = null });

        WorkTask Task(int id, int projectId, string assignee, WorkTaskStatus status = WorkTaskStatus.InProgress) => new()
        {
            Id = id, Title = $"t{id}", DepartmentId = 1, ProjectId = projectId, CreatedById = OtherUserId,
            Status = status, Assignees = [new WorkTaskAssignee { UserId = assignee }],
        };
        db.WorkTasks.AddRange(
            Task(OpenTask, ProjectA, OwnerUserId),
            Task(DoneTask, ProjectA, OwnerUserId, WorkTaskStatus.Done),
            Task(SomeoneElsesTask, ProjectA, OtherUserId),
            Task(OtherProjectTask, ProjectB, OwnerUserId),
            Task(WaitingTask, ProjectA, OwnerUserId, WorkTaskStatus.AwaitingConfirmation));

        db.Timesheets.Add(new Timesheet
        {
            Id = TimesheetId, EmployeeProfileId = "owner-p", DepartmentId = 1,
            PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7), Status = TimesheetStatus.Draft,
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static TimesheetEntriesController ControllerFor(AppDbContext db, string userId = OwnerUserId, string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new TimesheetEntriesController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    private static CreateEntryRequest Entry(int? taskId, int projectId = ProjectA) => new()
    {
        ProjectId = projectId, Date = Day, HoursWorked = 4m, WorkTaskId = taskId,
    };

    private static async Task<string> RefusalAsync(AppDbContext db, CreateEntryRequest request) =>
        (await Assert.ThrowsAsync<ArgumentException>(() =>
            ControllerFor(db).AddEntry(TimesheetId, request, CancellationToken.None))).Message;

    private static async Task SeedEntryAsync(AppDbContext db, int taskId)
    {
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            Id = "e-1", TimesheetId = TimesheetId, ProjectId = ProjectA, Date = Day, HoursWorked = 2m, WorkTaskId = taskId,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task An_open_task_the_owner_is_on_is_accepted_and_stored()
    {
        using var db = SeedWorld();
        await ControllerFor(db).AddEntry(TimesheetId, Entry(OpenTask), CancellationToken.None);
        Assert.Equal(OpenTask, (await db.TimesheetEntries.SingleAsync()).WorkTaskId);
    }

    [Fact]
    public async Task No_task_is_always_fine()
    {
        using var db = SeedWorld();
        await ControllerFor(db).AddEntry(TimesheetId, Entry(null), CancellationToken.None);
        Assert.Null((await db.TimesheetEntries.SingleAsync()).WorkTaskId);
    }

    [Fact]
    public async Task A_task_the_owner_is_not_on_is_refused()
    {
        using var db = SeedWorld();
        Assert.Equal(TimesheetEntryTaskRule.NotYoursMessage, await RefusalAsync(db, Entry(SomeoneElsesTask)));
    }

    [Fact]
    public async Task A_task_that_does_not_exist_reads_the_same_as_not_yours()
    {
        using var db = SeedWorld();
        Assert.Equal(TimesheetEntryTaskRule.NotYoursMessage, await RefusalAsync(db, Entry(9999)));
    }

    [Fact]
    public async Task A_closed_task_is_refused()
    {
        using var db = SeedWorld();
        Assert.Equal(TimesheetEntryTaskRule.ClosedMessage, await RefusalAsync(db, Entry(DoneTask)));
    }

    [Fact]
    public async Task A_task_on_another_project_is_refused()
    {
        using var db = SeedWorld();
        Assert.Equal(TimesheetEntryTaskRule.OtherProjectMessage, await RefusalAsync(db, Entry(OtherProjectTask, ProjectA)));
    }

    [Fact]
    public async Task HR_writing_on_behalf_is_judged_against_the_owner()
    {
        using var db = SeedWorld();
        // HR is on no task at all; the owner's task is accepted all the same.
        var result = await ControllerFor(db, HrUserId, AppRoles.HrAdministrator)
            .AddEntry(TimesheetId, Entry(OpenTask), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(OpenTask, (await db.TimesheetEntries.SingleAsync()).WorkTaskId);
    }

    [Fact]
    public async Task Keeping_a_since_closed_task_while_changing_the_hours_saves()
    {
        using var db = SeedWorld();
        await SeedEntryAsync(db, DoneTask);

        var edited = new TimesheetEntry
        {
            Id = "e-1", TimesheetId = TimesheetId, ProjectId = ProjectA, Date = Day, HoursWorked = 3m, WorkTaskId = DoneTask,
        };
        var result = await ControllerFor(db).UpdateEntry(TimesheetId, "e-1", edited, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(3m, (await db.TimesheetEntries.AsNoTracking().SingleAsync()).HoursWorked);
    }

    [Fact]
    public async Task Moving_the_row_to_another_project_under_the_same_task_is_refused()
    {
        using var db = SeedWorld();
        await SeedEntryAsync(db, OpenTask);

        var moved = new TimesheetEntry
        {
            Id = "e-1", TimesheetId = TimesheetId, ProjectId = ProjectB, Date = Day, HoursWorked = 2m, WorkTaskId = OpenTask,
        };
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            ControllerFor(db).UpdateEntry(TimesheetId, "e-1", moved, CancellationToken.None));
        Assert.Equal(TimesheetEntryTaskRule.OtherProjectMessage, error.Message);
    }

    [Fact]
    public async Task A_task_waiting_for_confirmation_can_still_be_logged()
    {
        using var db = SeedWorld();
        await ControllerFor(db).AddEntry(TimesheetId, Entry(WaitingTask), CancellationToken.None);
        Assert.Equal(WaitingTask, (await db.TimesheetEntries.SingleAsync()).WorkTaskId);
    }
}
