using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Queries;
using Domain;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// The timesheet editor's Task picker: the sheet owner's open tasks, plus any a row
/// on the sheet already names, and the caller's own when there is no sheet yet.
/// </summary>
public class GetTimesheetTaskOptionsTests
{
    private static Task<Result<List<TimesheetTaskOptionDto>>> Run(
        AppDbContext db, string caller, string? timesheetId = null, bool isManager = false) =>
        new GetTimesheetTaskOptions.Handler(db).Handle(new GetTimesheetTaskOptions.Query
        {
            CallerUserId = caller, TimesheetId = timesheetId, IsManager = isManager,
        }, CancellationToken.None);

    [Fact]
    public async Task Without_a_sheet_the_callers_own_open_tasks_are_offered()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.WorkTasks.AddRange(
            NewTask(Sales, Hr, Employee, "mine open"),
            NewTask(Sales, Hr, Employee, "mine done", WorkTaskStatus.Done),
            NewTask(Sales, Hr, SalesManager, "not mine"));
        await db.SaveChangesAsync();

        var result = await Run(db, Employee);

        var option = Assert.Single(result.Value!);
        Assert.Equal("mine open", option.Title);
        Assert.Equal(SalesProject, option.ProjectId);
        Assert.Equal("CRM", option.ProjectCode);
        Assert.False(option.IsClosed);
    }

    [Fact]
    public async Task A_closed_task_already_on_the_sheet_is_offered_and_flagged()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var done = NewTask(Sales, Hr, Employee, "done", WorkTaskStatus.Done);
        db.WorkTasks.Add(done);
        await db.SaveChangesAsync();
        var sheet = await WorkTaskTimesheetLinkTests.AddSheetAsync(db, Employee);
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            TimesheetId = sheet.Id, ProjectId = SalesProject, Date = new DateTime(2026, 9, 21), HoursWorked = 2m, WorkTaskId = done.Id,
        });
        await db.SaveChangesAsync();

        var result = await Run(db, Employee, sheet.Id);

        var option = Assert.Single(result.Value!);
        Assert.True(option.IsClosed);
        Assert.Equal(2m, option.LoggedHours);
    }

    [Fact]
    public async Task A_manager_reading_a_team_sheet_gets_the_owners_tasks()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.WorkTasks.AddRange(NewTask(Sales, Hr, Employee, "eve's"), NewTask(Sales, Hr, SalesManager, "sam's"));
        await db.SaveChangesAsync();
        var sheet = await WorkTaskTimesheetLinkTests.AddSheetAsync(db, Employee);

        var result = await Run(db, SalesManager, sheet.Id, isManager: true);

        Assert.Equal(["eve's"], result.Value!.Select(o => o.Title).ToList());
    }

    [Fact]
    public async Task A_sheet_outside_the_callers_scope_is_not_found()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var sheet = await WorkTaskTimesheetLinkTests.AddSheetAsync(db, Employee);

        var result = await Run(db, OpsManager, sheet.Id, isManager: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }
}
