using Application.Core;
using Application.WorkTasks.Commands;
using Application.WorkTasks.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// An assignee logs hours on an open task from its card: the week's Draft sheet is
/// found or made, the task's entry for that day is added to or created, and every
/// timesheet rule still holds.
/// </summary>
public class LogTaskTimeTests
{
    // Monday 19 October 2026; "now" is the Tuesday after.
    private static readonly DateOnly Monday = new(2026, 10, 19);
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static Task<Result<LogTaskTimeResult>> Log(AppDbContext db, int taskId, string caller, DateOnly date, decimal hours,
        string? notes = null, bool assignedOnly = false) =>
        new LogTaskTime.Handler(db).Handle(new LogTaskTime.Command
        {
            TaskId = taskId, CallerUserId = caller, AssignedOnly = assignedOnly,
            Date = date, Hours = hours, Notes = notes, NowUtc = Now,
        }, CancellationToken.None);

    private static async Task<int> Seeded(AppDbContext db, WorkTask task)
    {
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task.Id;
    }

    private static async Task<Timesheet> SheetFor(AppDbContext db, string userId, TimesheetStatus status)
    {
        var sheet = new Timesheet
        {
            Id = Guid.NewGuid().ToString(), EmployeeProfileId = $"p-{userId}", DepartmentId = Sales,
            PeriodStart = Monday.ToDateTime(TimeOnly.MinValue), PeriodEnd = Monday.AddDays(6).ToDateTime(TimeOnly.MinValue),
            Status = status,
        };
        db.Timesheets.Add(sheet);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return sheet;
    }

    [Fact]
    public async Task Creates_the_weeks_draft_and_an_entry_on_the_task()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 2m, "Chased the client", assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        var sheet = await db.Timesheets.AsNoTracking().SingleAsync();
        Assert.Equal($"p-{Employee}", sheet.EmployeeProfileId);
        Assert.Equal(Sales, sheet.DepartmentId);
        Assert.Equal(TimesheetStatus.Draft, sheet.Status);
        Assert.Equal(Monday.ToDateTime(TimeOnly.MinValue), sheet.PeriodStart);
        Assert.Equal(Monday.AddDays(6).ToDateTime(TimeOnly.MinValue), sheet.PeriodEnd);
        Assert.Equal(2m, sheet.TotalHours);
        var entry = await db.TimesheetEntries.AsNoTracking().SingleAsync();
        Assert.Equal(id, entry.WorkTaskId);
        Assert.Equal(SalesProject, entry.ProjectId);
        Assert.Equal("Chased the client", entry.Notes);
        Assert.Equal(Monday, result.Value!.WeekStart);
        Assert.Equal(2m, result.Value.HoursOnEntry);
        Assert.Equal(2m, result.Value.HoursOnDay);
        Assert.Equal("Draft", result.Value.TimesheetStatus);
        Assert.Equal(2m, result.Value.Task.LoggedHours);
    }

    [Fact]
    public async Task A_second_log_on_the_same_task_and_day_adds_to_the_entry()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        await Log(db, id, Employee, Monday, 2m, "Morning", assignedOnly: true);
        var result = await Log(db, id, Employee, Monday, 1.5m, "Afternoon", assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        var entry = await db.TimesheetEntries.AsNoTracking().SingleAsync();
        Assert.Equal(3.5m, entry.HoursWorked);
        Assert.Equal("Morning\nAfternoon", entry.Notes);
        Assert.Equal(3.5m, result.Value!.HoursOnEntry);
        Assert.Equal(3.5m, (await db.Timesheets.AsNoTracking().SingleAsync()).TotalHours);
    }

    [Fact]
    public async Task A_second_task_on_the_same_project_and_day_gets_its_own_entry()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var first = await Seeded(db, NewTask(Sales, Hr, Employee, "first", WorkTaskStatus.InProgress));
        var second = await Seeded(db, NewTask(Sales, Hr, Employee, "second", WorkTaskStatus.ToDo));

        await Log(db, first, Employee, Monday, 2m, assignedOnly: true);
        var result = await Log(db, second, Employee, Monday, 3m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, await db.TimesheetEntries.CountAsync());
        Assert.Equal(5m, result.Value!.HoursOnDay);
    }

    [Fact]
    public async Task Writes_to_a_rejected_week()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));
        var sheet = await SheetFor(db, Employee, TimesheetStatus.Rejected);

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(sheet.Id, result.Value!.TimesheetId);
        Assert.Equal("Rejected", result.Value.TimesheetStatus);
    }

    [Theory]
    [InlineData(TimesheetStatus.Submitted)]
    [InlineData(TimesheetStatus.Approved)]
    [InlineData(TimesheetStatus.Resubmitted)]
    public async Task Refuses_a_week_already_submitted(TimesheetStatus status)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));
        await SheetFor(db, Employee, status);

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(LogTaskTime.WeekSubmittedMessage, result.Error);
        Assert.Equal(0, await db.TimesheetEntries.CountAsync());
    }

    [Fact]
    public async Task A_sunday_belongs_to_the_week_that_started_the_monday_before()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));
        var sunday = new DateOnly(2026, 10, 18);

        var result = await Log(db, id, Employee, sunday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new DateOnly(2026, 10, 12), result.Value!.WeekStart);
    }

    [Fact]
    public async Task Refuses_a_day_past_24_hours_and_leaves_nothing_behind()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var first = await Seeded(db, NewTask(Sales, Hr, Employee, "first", WorkTaskStatus.InProgress));
        var second = await Seeded(db, NewTask(Sales, Hr, Employee, "second", WorkTaskStatus.InProgress));
        await Log(db, first, Employee, Monday, 8m, assignedOnly: true);

        var result = await Log(db, second, Employee, Monday, 17m, assignedOnly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("daily cap", result.Error);
        Assert.Equal(1, await db.TimesheetEntries.CountAsync());
        Assert.Equal(8m, (await db.Timesheets.AsNoTracking().SingleAsync()).TotalHours);
    }

    [Fact]
    public async Task A_refused_first_log_creates_no_sheet()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 25m, assignedOnly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, await db.Timesheets.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(1.3)]
    [InlineData(24.5)]
    public async Task Refuses_hours_that_are_not_half_hours_between_half_and_24(double hours)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, (decimal)hours, assignedOnly: true);

        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Equal(LogTaskTime.HoursMessage, result.Error);
    }

    [Fact]
    public async Task Refuses_a_future_date()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, new DateOnly(2026, 10, 21), 1m, assignedOnly: true);

        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Equal(LogTaskTime.FutureDateMessage, result.Error);
    }

    [Fact]
    public async Task Refuses_a_creator_who_is_not_on_the_task()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, SalesManager, Monday, 1m);

        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
        Assert.Equal(LogTaskTime.NotAnAssigneeMessage, result.Error);
    }

    [Fact]
    public async Task Answers_not_found_for_a_task_the_caller_cannot_see()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Ops, OpsManager, OpsManager, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Done)]
    [InlineData(WorkTaskStatus.Cancelled)]
    public async Task Refuses_a_closed_task(WorkTaskStatus status)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: status));

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(LogTaskTime.TaskClosedMessage, result.Error);
    }

    [Fact]
    public async Task Accepts_a_task_waiting_for_confirmation()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.AwaitingConfirmation));

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Refuses_a_task_with_no_project()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress);
        task.ProjectId = null;
        var id = await Seeded(db, task);

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(LogTaskTime.TaskHasNoProjectMessage, result.Error);
    }

    [Fact]
    public async Task Fills_the_one_type_the_project_carries_and_leaves_a_choice_blank()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.ProjectTypes.Add(new ProjectType { Id = 1, Name = "Support" });
        db.ProjectTypes.Add(new ProjectType { Id = 2, Name = "Issue" });
        db.ProjectTypeAssignments.Add(new ProjectTypeAssignment { ProjectId = SalesProject, ProjectTypeId = 1 });
        db.ProjectComponents.Add(new ProjectComponent { Id = 1, Name = "DM" });
        db.ProjectComponents.Add(new ProjectComponent { Id = 2, Name = "jDocs" });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        var entry = await db.TimesheetEntries.AsNoTracking().SingleAsync();
        Assert.Equal(1, entry.ProjectTypeId);           // the project carries one type
        Assert.Null(entry.ProjectComponentId);          // declares none, two in the catalogue: a choice
        Assert.Null(entry.ActivityTypeId);              // empty catalogue: nothing to say
    }

    [Fact]
    public async Task Uses_the_tasks_own_type_over_a_choice_the_project_leaves()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.ProjectTypes.Add(new ProjectType { Id = 1, Name = "Support" });
        db.ProjectTypes.Add(new ProjectType { Id = 2, Name = "Issue" });
        db.ProjectTypeAssignments.Add(new ProjectTypeAssignment { ProjectId = SalesProject, ProjectTypeId = 1 });
        db.ProjectTypeAssignments.Add(new ProjectTypeAssignment { ProjectId = SalesProject, ProjectTypeId = 2 });
        await db.SaveChangesAsync();
        var task = NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress);
        task.ProjectTypeId = 2;
        var id = await Seeded(db, task);

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, (await db.TimesheetEntries.AsNoTracking().SingleAsync()).ProjectTypeId);
    }

    [Fact]
    public async Task Fills_a_single_entry_catalogue_for_a_project_that_declared_none()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.ProjectComponents.Add(new ProjectComponent { Id = 1, Name = "DM" });
        db.ProjectComponents.Add(new ProjectComponent { Id = 2, Name = "Retired", IsActive = false });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.Equal(1, (await db.TimesheetEntries.AsNoTracking().SingleAsync()).ProjectComponentId);
    }

    [Fact]
    public async Task Refuses_a_note_longer_than_the_column_holds()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 1m, new string('x', 301), assignedOnly: true);

        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Equal(LogTaskTime.NotesTooLongMessage, result.Error);
        Assert.Equal(0, await db.Timesheets.CountAsync());
    }

    [Fact]
    public async Task Refuses_a_note_that_would_push_the_entry_past_the_column()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));
        await Log(db, id, Employee, Monday, 1m, new string('a', 200), assignedOnly: true);

        var result = await Log(db, id, Employee, Monday, 1m, new string('b', 120), assignedOnly: true);

        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Equal(LogTaskTime.NotesTooLongMessage, result.Error);
        Assert.Equal(1m, (await db.TimesheetEntries.AsNoTracking().SingleAsync()).HoursWorked);
    }

    [Fact]
    public async Task After_the_task_moves_project_new_hours_go_on_a_new_entry_on_the_new_project()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.Projects.Add(new Project { Id = 13, Name = "Second Sales", Code = "SS2" });
        db.ProjectDepartments.Add(new ProjectDepartment { ProjectId = 13, DepartmentId = Sales });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));
        await Log(db, id, Employee, Monday, 2m, assignedOnly: true);
        var task = await db.WorkTasks.SingleAsync(t => t.Id == id);
        task.ProjectId = 13;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        var entries = await db.TimesheetEntries.AsNoTracking().OrderBy(e => e.ProjectId).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal((SalesProject, 2m), (entries[0].ProjectId, entries[0].HoursWorked));
        Assert.Equal((13, 1m), (entries[1].ProjectId, entries[1].HoursWorked));
        Assert.Equal(1m, result.Value!.HoursOnEntry);
    }
}
