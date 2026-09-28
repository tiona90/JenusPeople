using Application.Core;
using Application.WorkTasks.Commands;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

public class WorkTaskCommandTests
{
    private readonly FakeEmailService _email = new();

    private Task<Result<WorkTaskDto>> Create(AppDbContext db, string caller, UpsertWorkTaskRequest task) =>
        new CreateWorkTask.Handler(db, _email, NullLogger<CreateWorkTask.Handler>.Instance)
            .Handle(new CreateWorkTask.Command { CallerUserId = caller, Task = task }, CancellationToken.None);

    private Task<Result<WorkTaskDto>> Update(AppDbContext db, int id, string caller, UpsertWorkTaskRequest task) =>
        new UpdateWorkTask.Handler(db, _email, NullLogger<UpdateWorkTask.Handler>.Instance)
            .Handle(new UpdateWorkTask.Command { Id = id, CallerUserId = caller, Task = task }, CancellationToken.None);

    private static Task<Result<WorkTaskDto>> SetStatus(AppDbContext db, int id, string caller, WorkTaskStatus status) =>
        new UpdateWorkTaskStatus.Handler(db)
            .Handle(new UpdateWorkTaskStatus.Command { Id = id, CallerUserId = caller, Status = status }, CancellationToken.None);

    private static Task<Result<int>> Delete(AppDbContext db, int id, string caller) =>
        new DeleteWorkTask.Handler(db).Handle(new DeleteWorkTask.Command { Id = id, CallerUserId = caller }, CancellationToken.None);

    private static UpsertWorkTaskRequest Request(int department, string assignee, string title = "Chase notes", int? project = null) =>
        new()
        {
            Title = title,
            DepartmentId = department,
            ProjectId = project ?? (department == Sales ? SalesProject : OpsProject),
            AssigneeIds = [assignee],
            Priority = WorkTaskPriority.High,
            IsBillable = true,
        };

    private static async Task<int> Seeded(AppDbContext db, WorkTask task)
    {
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task.Id;
    }

    [Fact]
    public async Task Creating_a_task_stores_it_and_emails_the_assignee()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await Create(db, Hr, Request(Sales, SalesManager));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Hr, result.Value!.CreatedById);
        Assert.Equal(WorkTaskStatus.ToDo, result.Value.Status);
        var mail = Assert.Single(_email.Sent);
        Assert.Equal($"{SalesManager}@t", mail.Recipient);
        Assert.Equal("New task: Chase notes", mail.Subject);
    }

    [Fact]
    public async Task Assigning_yourself_sends_no_email()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await Create(db, SalesManager, Request(Sales, SalesManager));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task No_email_when_notifications_are_switched_off()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.AppSettings.Add(new AppSettings { EmailNotificationsEnabled = false });
        await db.SaveChangesAsync();

        var result = await Create(db, Hr, Request(Sales, SalesManager));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task A_department_outside_the_callers_scope_is_refused()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await Create(db, SalesManager, Request(Ops, OpsManager));

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Equal(WorkTaskAccess.DepartmentOutOfScopeMessage, result.Error);
    }

    [Theory]
    [InlineData(OpsManager)]  // covers another department
    [InlineData(Employee)]    // wrong role
    public async Task An_ineligible_assignee_is_refused(string assignee)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await Create(db, Hr, Request(Sales, assignee));

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkTaskAssigneeRule.NotEligibleMessage, result.Error);
    }

    [Fact]
    public async Task Only_the_creator_may_edit()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        var result = await Update(db, id, SalesManager, Request(Sales, SalesManager, "renamed"));

        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
        Assert.Equal(WorkTaskAccess.NotCreatorMessage, result.Error);
    }

    [Fact]
    public async Task Reassigning_emails_the_new_assignee_only()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, SalesManager, "Chase notes"));

        var result = await Update(db, id, SalesManager, Request(Sales, Hr));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([Hr], result.Value!.Assignees.Select(a => a.UserId).ToList());
        Assert.Equal($"{Hr}@t", Assert.Single(_email.Sent).Recipient);
    }

    [Fact]
    public async Task Editing_title_only_does_not_recheck_an_assignee_who_left_scope()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));
        // The manager moves to Ops after being given the task.
        var profile = await db.EmployeeProfiles.SingleAsync(p => p.UserId == SalesManager);
        profile.DepartmentId = Ops;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Update(db, id, Hr, Request(Sales, SalesManager, "Typo fixed"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Typo fixed", result.Value!.Title);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task A_task_outside_scope_reads_as_not_found()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Ops, OpsManager, OpsManager));

        Assert.Equal(ResultErrorKind.NotFound, (await Update(db, id, Hr, Request(Sales, Hr))).ErrorKind);
        Assert.Equal(ResultErrorKind.NotFound, (await SetStatus(db, id, Hr, WorkTaskStatus.Done)).ErrorKind);
        Assert.Equal(ResultErrorKind.NotFound, (await Delete(db, id, Hr)).ErrorKind);
    }

    [Fact]
    public async Task The_assignee_may_move_the_status_and_done_stamps_completion()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        var done = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);
        Assert.True(done.IsSuccess, done.Error);
        Assert.NotNull(done.Value!.CompletedAtUtc);

        var reopened = await SetStatus(db, id, SalesManager, WorkTaskStatus.InProgress);
        Assert.Null(reopened.Value!.CompletedAtUtc);
    }

    [Fact]
    public async Task Setting_done_again_keeps_the_first_completion_time()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.Done);
        var first = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc);
        task.CompletedAtUtc = first;
        var id = await Seeded(db, task);

        var again = await SetStatus(db, id, Hr, WorkTaskStatus.Done);

        Assert.Equal(first, again.Value!.CompletedAtUtc);
    }

    [Fact]
    public async Task A_bystander_in_scope_may_not_move_the_status()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.UserDepartments.Add(new UserDepartment { UserId = OpsManager, DepartmentId = Sales });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        var result = await SetStatus(db, id, OpsManager, WorkTaskStatus.Done);

        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
        Assert.Equal(WorkTaskAccess.NotParticipantMessage, result.Error);
    }

    [Fact]
    public async Task Only_the_creator_may_delete()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        Assert.Equal(ResultErrorKind.Forbidden, (await Delete(db, id, SalesManager)).ErrorKind);

        var deleted = await Delete(db, id, Hr);
        Assert.True(deleted.IsSuccess);
        Assert.Equal(Sales, deleted.Value);
        Assert.False(await db.WorkTasks.AnyAsync());
    }

    /// <summary>
    /// A leaver is deactivated, not deleted, so the delete-time cleanup never runs for
    /// them. Their tasks must not freeze: once the creator is inactive, anyone who can
    /// see the task may manage it.
    /// </summary>
    [Fact]
    public async Task A_deactivated_creators_task_can_be_managed_by_anyone_in_scope()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.UserDepartments.Add(new UserDepartment { UserId = OpsManager, DepartmentId = Sales });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, SalesManager, Hr));
        var creator = await db.Users.SingleAsync(u => u.Id == SalesManager);
        creator.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var listed = await new Application.WorkTasks.Queries.GetWorkTaskList.Handler(db).Handle(
            new Application.WorkTasks.Queries.GetWorkTaskList.Query { CallerUserId = OpsManager }, CancellationToken.None);
        var row = Assert.Single(listed.Value!);
        Assert.True(row.CanEdit);
        Assert.True(row.CanChangeStatus);

        var edited = await Update(db, id, OpsManager, Request(Sales, Hr, "Taken over"));
        Assert.True(edited.IsSuccess, edited.Error);

        var moved = await SetStatus(db, id, OpsManager, WorkTaskStatus.InProgress);
        Assert.True(moved.IsSuccess, moved.Error);

        var deleted = await Delete(db, id, OpsManager);
        Assert.True(deleted.IsSuccess, deleted.Error);
    }

    [Fact]
    public async Task A_created_task_carries_its_project()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await Create(db, Hr, Request(Sales, SalesManager));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SalesProject, result.Value!.ProjectId);
        Assert.Equal("CRM Rollout", result.Value.ProjectName);
        Assert.Equal("CRM", result.Value.ProjectCode);
        Assert.Equal("p3", result.Value.ProjectColorKey);
    }

    [Theory]
    [InlineData(OpsProject)]            // belongs to another department
    [InlineData(InactiveSalesProject)]  // switched off
    [InlineData(999)]                   // does not exist
    public async Task A_project_not_open_to_the_department_is_refused(int project)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await Create(db, Hr, Request(Sales, SalesManager, project: project));

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Equal(WorkTaskProjectRule.NotAvailableMessage, result.Error);
    }

    [Fact]
    public async Task Moving_a_task_to_another_department_requires_a_project_of_that_department()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.UserDepartments.Add(new UserDepartment { UserId = Hr, DepartmentId = Ops });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, Hr));

        var kept = await Update(db, id, Hr, Request(Ops, OpsManager, project: SalesProject));
        Assert.Equal(WorkTaskProjectRule.NotAvailableMessage, kept.Error);

        var moved = await Update(db, id, Hr, Request(Ops, OpsManager, project: OpsProject));
        Assert.True(moved.IsSuccess, moved.Error);
    }

    [Fact]
    public async Task Editing_title_only_does_not_recheck_a_project_since_switched_off()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));
        var project = await db.Projects.SingleAsync(p => p.Id == SalesProject);
        project.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Update(db, id, Hr, Request(Sales, SalesManager, "Typo fixed"));

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>OpsManager covers Sales as well, so Sales has three eligible people.</summary>
    private static async Task OpsManagerCoversSalesAsync(AppDbContext db)
    {
        db.UserDepartments.Add(new UserDepartment { UserId = OpsManager, DepartmentId = Sales });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static UpsertWorkTaskRequest RequestFor(int department, params string[] assignees)
    {
        var request = Request(department, assignees[0]);
        request.AssigneeIds = [.. assignees];
        return request;
    }

    [Fact]
    public async Task A_task_can_have_several_assignees_and_each_is_emailed_once()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await OpsManagerCoversSalesAsync(db);

        var result = await Create(db, Hr, RequestFor(Sales, SalesManager, OpsManager, Hr));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            ["Hana HR", "Olga Ops", "Sam Sales"],
            result.Value!.Assignees.Select(a => a.DisplayName).ToList());
        // Hr is the creator, and nobody is emailed about a task they assigned themselves.
        Assert.Equal(
            [$"{OpsManager}@t", $"{SalesManager}@t"],
            _email.Sent.Select(m => m.Recipient).OrderBy(x => x).ToList());
    }

    [Fact]
    public async Task One_ineligible_person_among_several_refuses_the_save()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await Create(db, Hr, RequestFor(Sales, SalesManager, Employee));

        Assert.Equal(WorkTaskAssigneeRule.NotEligibleMessage, result.Error);
        Assert.False(await db.WorkTasks.AnyAsync());
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Adding_an_assignee_emails_only_the_newcomer()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await OpsManagerCoversSalesAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        var result = await Update(db, id, Hr, RequestFor(Sales, SalesManager, OpsManager));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Assignees.Count);
        Assert.Equal($"{OpsManager}@t", Assert.Single(_email.Sent).Recipient);
    }

    [Fact]
    public async Task Removing_an_assignee_keeps_the_rest()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await OpsManagerCoversSalesAsync(db);
        var created = await Create(db, Hr, RequestFor(Sales, SalesManager, OpsManager));
        _email.Sent.Clear();

        var result = await Update(db, created.Value!.Id, Hr, RequestFor(Sales, OpsManager));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([OpsManager], result.Value!.Assignees.Select(a => a.UserId).ToList());
        Assert.Empty(_email.Sent);
        Assert.Equal(1, await db.WorkTaskAssignees.CountAsync());
    }

    [Fact]
    public async Task Any_assignee_may_move_the_status()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await OpsManagerCoversSalesAsync(db);
        var created = await Create(db, Hr, RequestFor(Sales, SalesManager, OpsManager));

        var moved = await SetStatus(db, created.Value!.Id, OpsManager, WorkTaskStatus.Done);

        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(WorkTaskStatus.Done, moved.Value!.Status);
        Assert.True(moved.Value.CanChangeStatus);
    }

    [Fact]
    public async Task Keeping_an_assignee_who_left_scope_beside_a_new_one_still_saves()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await OpsManagerCoversSalesAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));
        var profile = await db.EmployeeProfiles.SingleAsync(p => p.UserId == SalesManager);
        profile.DepartmentId = Ops;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Only the newcomer is checked: SalesManager stays although they now cover Ops.
        var result = await Update(db, id, Hr, RequestFor(Sales, SalesManager, OpsManager));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task A_task_keeps_its_target_hours_and_weeks_and_an_edit_can_clear_them()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var request = Request(Sales, SalesManager);
        request.TargetHours = 40;
        request.TargetWeeks = 2;

        var created = await Create(db, Hr, request);
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(40, created.Value!.TargetHours);
        Assert.Equal(2, created.Value.TargetWeeks);

        var cleared = await Update(db, created.Value.Id, Hr, Request(Sales, SalesManager));
        Assert.True(cleared.IsSuccess, cleared.Error);
        Assert.Null(cleared.Value!.TargetHours);
        Assert.Null(cleared.Value.TargetWeeks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_task_records_whether_it_is_billable(bool billable)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var request = Request(Sales, SalesManager);
        request.IsBillable = billable;

        var created = await Create(db, Hr, request);

        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(billable, created.Value!.IsBillable);
    }
}
