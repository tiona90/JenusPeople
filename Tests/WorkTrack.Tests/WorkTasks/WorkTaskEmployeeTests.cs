using Application.Core;
using Application.WorkTasks.Commands;
using Application.WorkTasks.Queries;
using Application.WorkTasks.Support;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// An Employee works the tasks they are given: they can be assigned one, see the
/// ones they are on or created — only those — and move their status. They may also
/// create their own, always assigned to themselves, and edit or delete only the
/// tasks they created. Picking assignees stays with Managers and HR Administrators
/// (see WorkTaskSurfaceTests).
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
    public async Task An_employee_sees_only_the_tasks_they_are_on_and_cannot_edit_a_managers()
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

    private Task<Result<Application.WorkTasks.DTOs.WorkTaskDto>> CreateAsEmployee(AppDbContext db, List<string> assigneeIds) =>
        new CreateWorkTask.Handler(db, _email, NullLogger<CreateWorkTask.Handler>.Instance).Handle(
            new CreateWorkTask.Command
            {
                CallerUserId = Employee,
                AssignedOnly = true,
                Task = new() { Title = "My own", DepartmentId = Sales, ProjectId = SalesProject, AssigneeIds = assigneeIds, IsBillable = true },
            },
            CancellationToken.None);

    [Fact]
    public async Task An_employee_creates_a_task_assigned_to_themselves_only()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        // Whatever the request says, and an empty list is not "everyone" for an Employee.
        var named = await CreateAsEmployee(db, [SalesManager]);
        var empty = await CreateAsEmployee(db, []);

        foreach (var result in new[] { named, empty })
        {
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(Employee, Assert.Single(result.Value!.Assignees).UserId);
            Assert.Equal(Employee, result.Value.CreatedById);
            Assert.True(result.Value.CanEdit);
        }
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task An_employee_cannot_create_outside_their_department()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await new CreateWorkTask.Handler(db, _email, NullLogger<CreateWorkTask.Handler>.Instance).Handle(
            new CreateWorkTask.Command
            {
                CallerUserId = Employee,
                AssignedOnly = true,
                Task = new() { Title = "Elsewhere", DepartmentId = Ops, ProjectId = OpsProject, IsBillable = false },
            },
            CancellationToken.None);

        Assert.Equal(WorkTaskAccess.DepartmentOutOfScopeMessage, result.Error);
    }

    [Fact]
    public async Task An_employee_sees_and_edits_a_task_they_created()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Employee, Employee, "mine"));

        var listed = Assert.Single((await ListAsEmployee(db)).Value!);
        Assert.True(listed.CanEdit);

        var result = await new UpdateWorkTask.Handler(db, _email, NullLogger<UpdateWorkTask.Handler>.Instance).Handle(
            new UpdateWorkTask.Command
            {
                Id = id,
                CallerUserId = Employee,
                AssignedOnly = true,
                Task = new() { Title = "renamed", DepartmentId = Sales, ProjectId = SalesProject, AssigneeIds = [SalesManager], IsBillable = true },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("renamed", result.Value!.Title);
        Assert.Equal(Employee, Assert.Single(result.Value.Assignees).UserId);
    }

    [Fact]
    public async Task An_employee_cannot_edit_or_delete_a_task_somebody_else_created()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));

        var edit = await new UpdateWorkTask.Handler(db, _email, NullLogger<UpdateWorkTask.Handler>.Instance).Handle(
            new UpdateWorkTask.Command
            {
                Id = id,
                CallerUserId = Employee,
                AssignedOnly = true,
                Task = new() { Title = "hijacked", DepartmentId = Sales, ProjectId = SalesProject, IsBillable = true },
            },
            CancellationToken.None);
        var delete = await new DeleteWorkTask.Handler(db).Handle(
            new DeleteWorkTask.Command { Id = id, CallerUserId = Employee, AssignedOnly = true }, CancellationToken.None);

        Assert.Equal(ResultErrorKind.Forbidden, edit.ErrorKind);
        Assert.Equal(ResultErrorKind.Forbidden, delete.ErrorKind);
    }

    [Fact]
    public async Task A_deactivated_creator_does_not_hand_an_employee_the_delete()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));
        (await db.Users.SingleAsync(u => u.Id == SalesManager)).IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var delete = await new DeleteWorkTask.Handler(db).Handle(
            new DeleteWorkTask.Command { Id = id, CallerUserId = Employee, AssignedOnly = true }, CancellationToken.None);

        Assert.Equal(ResultErrorKind.Forbidden, delete.ErrorKind);
    }

    [Fact]
    public async Task An_employee_deletes_a_task_they_created()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Employee, Employee));

        var delete = await new DeleteWorkTask.Handler(db).Handle(
            new DeleteWorkTask.Command { Id = id, CallerUserId = Employee, AssignedOnly = true }, CancellationToken.None);

        Assert.True(delete.IsSuccess, delete.Error);
        Assert.False(await db.WorkTasks.AnyAsync(t => t.Id == id));
    }
}
