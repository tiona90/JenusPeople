using Application.WorkTasks.DTOs;
using Application.WorkTasks.Queries;
using Domain;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// Who is not working on anything: the people a task in the caller's departments
/// could go to, with no task In Progress. Sales has the Sales manager and the
/// Employee (HR covers it but is never given a task); Ops has its manager and a
/// deactivated one.
/// </summary>
public class GetIdleTaskPeopleTests
{
    private static async Task Seed(AppDbContext db, params WorkTask[] tasks)
    {
        db.WorkTasks.AddRange(tasks);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<List<WorkTaskIdlePersonDto>> IdleFor(AppDbContext db, string caller) =>
        (await new GetIdleTaskPeople.Handler(db).Handle(
            new GetIdleTaskPeople.Query { CallerUserId = caller }, CancellationToken.None)).Value!;

    [Fact]
    public async Task Somebody_with_a_task_in_progress_is_not_listed()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Seed(db, NewTask(Sales, SalesManager, Employee, status: WorkTaskStatus.InProgress));

        var idle = await IdleFor(db, Hr);

        Assert.Equal([SalesManager], idle.Select(p => p.UserId));
        Assert.True(idle[0].IsManager);
        Assert.Equal(["Sales"], idle[0].DepartmentNames);
    }

    [Fact]
    public async Task Tasks_still_to_do_do_not_count_as_working_and_are_counted()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Seed(db,
            NewTask(Sales, SalesManager, Employee),
            NewTask(Sales, SalesManager, Employee),
            NewTask(Sales, SalesManager, Employee, status: WorkTaskStatus.Done));

        var idle = await IdleFor(db, Hr);

        // Nothing at all sorts before tasks waiting to start.
        Assert.Equal([SalesManager, Employee], idle.Select(p => p.UserId));
        Assert.Equal(0, idle[0].ToDoCount);
        Assert.Equal(2, idle[1].ToDoCount);
        Assert.False(idle[1].IsManager);
    }

    [Fact]
    public async Task The_caller_hr_and_the_deactivated_are_left_out_and_scope_is_the_callers()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var forSalesManager = await IdleFor(db, SalesManager);
        var forOpsManager = await IdleFor(db, OpsManager);

        Assert.Equal([Employee], forSalesManager.Select(p => p.UserId));
        // Ops has only its own manager and a deactivated one.
        Assert.Empty(forOpsManager);
    }

    [Fact]
    public async Task A_task_waiting_for_confirmation_does_not_count_as_working()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Seed(db, NewTask(Sales, SalesManager, Employee, status: WorkTaskStatus.AwaitingConfirmation));

        var idle = await IdleFor(db, Hr);

        var employee = Assert.Single(idle, p => p.UserId == Employee);
        Assert.Equal(0, employee.ToDoCount);
    }
}
