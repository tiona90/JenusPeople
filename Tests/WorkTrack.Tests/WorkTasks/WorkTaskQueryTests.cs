using Application.WorkTasks.Queries;
using Application.WorkTasks.Support;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

public class WorkTaskQueryTests
{
    [Fact]
    public async Task The_list_holds_only_tasks_in_the_callers_departments()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.WorkTasks.AddRange(
            NewTask(Sales, Hr, SalesManager, "sales task"),
            NewTask(Ops, OpsManager, OpsManager, "ops task"));
        await db.SaveChangesAsync();

        var result = await new GetWorkTaskList.Handler(db).Handle(
            new GetWorkTaskList.Query { CallerUserId = SalesManager }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(result.Value!);
        Assert.Equal("sales task", task.Title);
        Assert.Equal("Sales", task.DepartmentName);
        Assert.Equal(["Sam Sales"], task.Assignees.Select(a => a.DisplayName).ToList());
        Assert.Equal("Hana HR", task.CreatedByName);
    }

    [Fact]
    public async Task Permission_flags_follow_creator_and_assignee()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.WorkTasks.Add(NewTask(Sales, Hr, SalesManager));
        await db.SaveChangesAsync();

        async Task<(bool edit, bool status)> FlagsFor(string caller)
        {
            var r = await new GetWorkTaskList.Handler(db).Handle(new GetWorkTaskList.Query { CallerUserId = caller }, CancellationToken.None);
            var t = Assert.Single(r.Value!);
            return (t.CanEdit, t.CanChangeStatus);
        }

        Assert.Equal((true, true), await FlagsFor(Hr));            // creator
        Assert.Equal((false, true), await FlagsFor(SalesManager)); // assignee
    }

    [Fact]
    public async Task A_bystander_in_scope_sees_the_task_read_only()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.UserDepartments.Add(new Domain.UserDepartment { UserId = OpsManager, DepartmentId = Sales });
        db.WorkTasks.Add(NewTask(Sales, Hr, SalesManager));
        await db.SaveChangesAsync();

        var r = await new GetWorkTaskList.Handler(db).Handle(new GetWorkTaskList.Query { CallerUserId = OpsManager }, CancellationToken.None);

        var t = Assert.Single(r.Value!);
        Assert.False(t.CanEdit);
        Assert.False(t.CanChangeStatus);
    }

    [Fact]
    public async Task Open_tasks_sort_before_closed_then_by_due_date()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var done = NewTask(Sales, Hr, SalesManager, "done", Domain.WorkTaskStatus.Done);
        var noDue = NewTask(Sales, Hr, SalesManager, "no due");
        var later = NewTask(Sales, Hr, SalesManager, "later"); later.DueDate = new DateOnly(2026, 10, 20);
        var soon = NewTask(Sales, Hr, SalesManager, "soon"); soon.DueDate = new DateOnly(2026, 10, 1);
        db.WorkTasks.AddRange(done, noDue, later, soon);
        await db.SaveChangesAsync();

        var r = await new GetWorkTaskList.Handler(db).Handle(new GetWorkTaskList.Query { CallerUserId = Hr }, CancellationToken.None);

        Assert.Equal(["soon", "later", "no due", "done"], r.Value!.Select(t => t.Title).ToList());
    }

    [Fact]
    public async Task Eligible_assignees_are_active_managers_and_hr_covering_the_department()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var sales = await WorkTaskAssigneeRule.EligibleAsync(db, Sales, CancellationToken.None);
        var ops = await WorkTaskAssigneeRule.EligibleAsync(db, Ops, CancellationToken.None);

        Assert.Equal([Hr, SalesManager], sales.Select(a => a.UserId).OrderBy(x => x).ToList());
        Assert.Equal([OpsManager], ops.Select(a => a.UserId).ToList());
    }

    [Fact]
    public async Task Deactivated_manager_is_not_eligible()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        Assert.False(await WorkTaskAssigneeRule.IsEligibleAsync(db, GoneManager, Ops, CancellationToken.None));
        Assert.False(await WorkTaskAssigneeRule.IsEligibleAsync(db, Employee, Sales, CancellationToken.None));
    }

    [Fact]
    public async Task Assignees_for_a_department_outside_scope_is_not_found()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var r = await new GetWorkTaskAssignees.Handler(db).Handle(
            new GetWorkTaskAssignees.Query { CallerUserId = SalesManager, DepartmentId = Ops }, CancellationToken.None);

        Assert.False(r.IsSuccess);
        Assert.Equal(Application.Core.ResultErrorKind.NotFound, r.ErrorKind);
    }

    [Fact]
    public async Task Departments_are_the_callers_scope_by_name()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.UserDepartments.Add(new Domain.UserDepartment { UserId = Hr, DepartmentId = Ops });
        await db.SaveChangesAsync();

        var r = await new GetWorkTaskDepartments.Handler(db).Handle(
            new GetWorkTaskDepartments.Query { CallerUserId = Hr }, CancellationToken.None);

        Assert.Equal(["Ops", "Sales"], r.Value!.Select(d => d.Name).ToList());
    }

    [Fact]
    public async Task Projects_are_the_departments_active_ones()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var r = await new GetWorkTaskProjects.Handler(db).Handle(
            new GetWorkTaskProjects.Query { CallerUserId = Hr, DepartmentId = Sales }, CancellationToken.None);

        Assert.True(r.IsSuccess, r.Error);
        var project = Assert.Single(r.Value!);
        Assert.Equal(SalesProject, project.Id);
        Assert.Equal("CRM Rollout", project.Name);
        Assert.Equal("CRM", project.Code);
    }

    [Fact]
    public async Task Projects_for_a_department_outside_scope_is_not_found()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var r = await new GetWorkTaskProjects.Handler(db).Handle(
            new GetWorkTaskProjects.Query { CallerUserId = SalesManager, DepartmentId = Ops }, CancellationToken.None);

        Assert.Equal(Application.Core.ResultErrorKind.NotFound, r.ErrorKind);
    }
}
