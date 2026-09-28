using Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// The table's foreign keys are what DeleteAdminUser and DeleteDepartment have to
/// unpick, so they are pinned against a database that enforces them.
/// </summary>
public class WorkTaskMappingTests
{
    [Fact]
    public async Task A_task_round_trips_with_its_department_and_people()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        db.Departments.Add(new Department { Id = 1, Name = "Sales", Code = "SAL" });
        db.Users.Add(new User { Id = "u-a", UserName = "a@t", Email = "a@t", DisplayName = "A" });
        db.Users.Add(new User { Id = "u-b", UserName = "b@t", Email = "b@t", DisplayName = "B" });
        db.WorkTasks.Add(new WorkTask
        {
            Title = "Chase sick notes",
            DepartmentId = 1,
            AssigneeId = "u-b",
            CreatedById = "u-a",
            DueDate = new DateOnly(2026, 10, 5),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var task = await db.WorkTasks.Include(t => t.Department).SingleAsync();
        Assert.Equal("Sales", task.Department!.Name);
        Assert.Equal(WorkTaskStatus.ToDo, task.Status);
        Assert.Equal(WorkTaskPriority.Normal, task.Priority);
        Assert.Equal(new DateOnly(2026, 10, 5), task.DueDate);
    }

    [Fact]
    public async Task A_task_cannot_point_at_a_missing_department()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        db.Users.Add(new User { Id = "u-a", UserName = "a@t", Email = "a@t", DisplayName = "A" });
        db.WorkTasks.Add(new WorkTask
        {
            Title = "x", DepartmentId = 99, AssigneeId = "u-a", CreatedById = "u-a",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
