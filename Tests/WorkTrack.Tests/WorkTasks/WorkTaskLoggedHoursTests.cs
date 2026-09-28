using Application.WorkTasks.Queries;
using Domain;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// A task's logged hours are every timesheet row linked to it, whatever the sheet's
/// status — summed on read, stored nowhere.
/// </summary>
public class WorkTaskLoggedHoursTests
{
    [Fact]
    public async Task Logged_hours_sum_every_linked_entry_in_any_timesheet_status()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var logged = NewTask(Sales, Hr, SalesManager, "logged");
        var untouched = NewTask(Sales, Hr, SalesManager, "untouched");
        db.WorkTasks.AddRange(logged, untouched);
        await db.SaveChangesAsync();

        var draft = await WorkTaskTimesheetLinkTests.AddSheetAsync(db, SalesManager);
        var approved = await WorkTaskTimesheetLinkTests.AddSheetAsync(db, Employee);
        approved.Status = TimesheetStatus.Approved;
        db.TimesheetEntries.AddRange(
            new TimesheetEntry { TimesheetId = draft.Id, ProjectId = SalesProject, Date = new DateTime(2026, 9, 21), HoursWorked = 1.5m, WorkTaskId = logged.Id },
            new TimesheetEntry { TimesheetId = approved.Id, ProjectId = SalesProject, Date = new DateTime(2026, 9, 22), HoursWorked = 6m, WorkTaskId = logged.Id },
            new TimesheetEntry { TimesheetId = draft.Id, ProjectId = SalesProject, Date = new DateTime(2026, 9, 23), HoursWorked = 8m });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetWorkTaskList.Handler(db).Handle(
            new GetWorkTaskList.Query { CallerUserId = SalesManager }, CancellationToken.None);

        Assert.Equal(7.5m, result.Value!.Single(t => t.Title == "logged").LoggedHours);
        Assert.Equal(0m, result.Value!.Single(t => t.Title == "untouched").LoggedHours);
    }
}
