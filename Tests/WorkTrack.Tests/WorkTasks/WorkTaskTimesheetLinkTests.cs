using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// Deleting a task keeps the hours logged against it and drops only the link.
/// Over SQLite so the foreign key's ON DELETE is real.
/// </summary>
public class WorkTaskTimesheetLinkTests
{
    /// <summary>A Draft sheet for 21–27 Sep 2026 owned by <paramref name="userId"/>'s profile. One per user (unique index).</summary>
    internal static async Task<Timesheet> AddSheetAsync(AppDbContext db, string userId)
    {
        var sheet = new Timesheet
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeProfileId = $"p-{userId}",
            DepartmentId = Sales,
            PeriodStart = new DateTime(2026, 9, 21),
            PeriodEnd = new DateTime(2026, 9, 27),
            Status = TimesheetStatus.Draft,
        };
        db.Timesheets.Add(sheet);
        await db.SaveChangesAsync();
        return sheet;
    }

    [Fact]
    public async Task Deleting_a_task_nulls_the_link_and_keeps_the_entry()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, SalesManager);
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        var sheet = await AddSheetAsync(db, SalesManager);
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            TimesheetId = sheet.Id, ProjectId = SalesProject, Date = new DateTime(2026, 9, 21),
            HoursWorked = 4m, WorkTaskId = task.Id,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.WorkTasks.Remove(await db.WorkTasks.SingleAsync(t => t.Id == task.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var entry = await db.TimesheetEntries.SingleAsync();
        Assert.Null(entry.WorkTaskId);
        Assert.Equal(4m, entry.HoursWorked);
    }
}
