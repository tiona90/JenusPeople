using Application.Core;
using Application.WorkTasks.Commands;
using Application.WorkTasks.Queries;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// An Employee works the tasks they are given: they can be assigned one, see the
/// ones they are on — only those — and move their status. Creating, editing and
/// deleting stay with Managers and HR Administrators (the controller gates those
/// actions; see WorkTaskSurfaceTests).
/// </summary>
public class WorkTaskEmployeeTests
{
    private readonly FakeEmailService _email = new();

    private static async Task<int> Seeded(AppDbContext db, WorkTask task)
    {
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task.Id;
    }

    private static Task<Result<List<Application.WorkTasks.DTOs.WorkTaskDto>>> ListAsEmployee(AppDbContext db) =>
        new GetWorkTaskList.Handler(db).Handle(
            new GetWorkTaskList.Query { CallerUserId = Employee, AssignedOnly = true }, CancellationToken.None);

    [Fact]
    public async Task An_employee_in_the_department_can_be_assigned_and_is_emailed()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await new CreateWorkTask.Handler(db, _email, NullLogger<CreateWorkTask.Handler>.Instance).Handle(
            new CreateWorkTask.Command
            {
                CallerUserId = SalesManager,
                Task = new() { Title = "File the receipts", DepartmentId = Sales, ProjectId = SalesProject, AssigneeIds = [Employee], IsBillable = false },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal($"{Employee}@t", Assert.Single(_email.Sent).Recipient);
    }

    [Fact]
    public async Task An_employee_sees_only_the_tasks_they_are_on_and_cannot_edit_them()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Seeded(db, NewTask(Sales, SalesManager, Employee, "mine"));
        await Seeded(db, NewTask(Sales, SalesManager, SalesManager, "not mine"));

        var result = await ListAsEmployee(db);

        var task = Assert.Single(result.Value!);
        Assert.Equal("mine", task.Title);
        Assert.False(task.CanEdit);
        Assert.True(task.CanChangeStatus);
    }

    [Fact]
    public async Task A_deactivated_creator_does_not_hand_an_employee_the_edit()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Seeded(db, NewTask(Sales, SalesManager, Employee));
        (await db.Users.SingleAsync(u => u.Id == SalesManager)).IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var task = Assert.Single((await ListAsEmployee(db)).Value!);

        Assert.False(task.CanEdit);
        Assert.True(task.CanChangeStatus);
    }

    [Fact]
    public async Task An_employee_moves_the_status_of_their_own_task_only()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var mine = await Seeded(db, NewTask(Sales, SalesManager, Employee));
        var other = await Seeded(db, NewTask(Sales, SalesManager, SalesManager));

        Task<Result<Application.WorkTasks.DTOs.WorkTaskDto>> Move(int id) =>
            new UpdateWorkTaskStatus.Handler(db).Handle(
                new UpdateWorkTaskStatus.Command { Id = id, CallerUserId = Employee, Status = WorkTaskStatus.Done, AssignedOnly = true },
                CancellationToken.None);

        var moved = await Move(mine);
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(WorkTaskStatus.Done, moved.Value!.Status);
        Assert.False(moved.Value.CanEdit);

        // A task in their department they are not on is not theirs to see at all.
        Assert.Equal(ResultErrorKind.NotFound, (await Move(other)).ErrorKind);
    }
}
