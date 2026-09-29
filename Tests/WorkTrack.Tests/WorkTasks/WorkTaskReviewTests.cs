using Application.Core;
using Application.WorkTasks;
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

/// <summary>
/// A task somebody else handed you is not finished when you say so: your Done puts it
/// in AwaitingConfirmation, and a reviewer — its creator, or a Manager or HR
/// Administrator in scope who is not on it — confirms it or sends it back.
/// </summary>
public class WorkTaskReviewTests
{
    private readonly FakeEmailService _email = new();

    private Task<Result<WorkTaskDto>> SetStatus(AppDbContext db, int id, string caller, WorkTaskStatus status,
        string? reason = null, bool assignedOnly = false) =>
        new UpdateWorkTaskStatus.Handler(db, _email, NullLogger<UpdateWorkTaskStatus.Handler>.Instance)
            .Handle(new UpdateWorkTaskStatus.Command
            {
                Id = id, CallerUserId = caller, Status = status, Reason = reason, AssignedOnly = assignedOnly,
            }, CancellationToken.None);

    private static async Task<int> Seeded(AppDbContext db, WorkTask task)
    {
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task.Id;
    }

    private static async Task<WorkTask> Reload(AppDbContext db, int id) =>
        await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == id);

    [Fact]
    public async Task An_assignees_done_waits_for_confirmation()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.AwaitingConfirmation, result.Value!.Status);
        Assert.Null(result.Value.CompletedAtUtc);
        Assert.False(result.Value.CanConfirm);
    }

    [Fact]
    public async Task An_employees_done_waits_too()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, Employee, WorkTaskStatus.Done, assignedOnly: true);

        Assert.Equal(WorkTaskStatus.AwaitingConfirmation, result.Value!.Status);
    }

    [Fact]
    public async Task A_personal_task_closes_directly()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Employee, Employee, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, Employee, WorkTaskStatus.Done, assignedOnly: true);

        Assert.Equal(WorkTaskStatus.Done, result.Value!.Status);
        Assert.NotNull(result.Value.CompletedAtUtc);
    }

    [Theory]
    [InlineData(Hr)]          // the creator
    [InlineData(OpsManager)]  // a Manager in scope who is not on it
    public async Task A_reviewer_confirms(string reviewer)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.UserDepartments.Add(new UserDepartment { UserId = OpsManager, DepartmentId = Sales });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));

        var listed = await new Application.WorkTasks.Queries.GetWorkTaskList.Handler(db).Handle(
            new Application.WorkTasks.Queries.GetWorkTaskList.Query { CallerUserId = reviewer }, CancellationToken.None);
        Assert.True(Assert.Single(listed.Value!).CanConfirm);

        var result = await SetStatus(db, id, reviewer, WorkTaskStatus.Done);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.Done, result.Value!.Status);
        Assert.NotNull(result.Value.CompletedAtUtc);
        Assert.Equal(reviewer, (await Reload(db, id)).ConfirmedById);
    }

    [Fact]
    public async Task An_assignee_manager_cannot_confirm()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation);
        task.Assignees.Add(new WorkTaskAssignee { UserId = Employee });
        var id = await Seeded(db, task);

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        // Their Done on a waiting task is the Done they already gave: nothing moves.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.AwaitingConfirmation, result.Value!.Status);
        Assert.False(result.Value.CanConfirm);
        var cancelled = await SetStatus(db, id, SalesManager, WorkTaskStatus.Cancelled);
        Assert.Equal(WorkTaskReviewRule.AwaitingConfirmationMessage, cancelled.Error);
    }

    [Fact]
    public async Task Marking_done_again_while_waiting_is_a_no_op()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);
        _email.Sent.Clear();
        var again = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(WorkTaskStatus.AwaitingConfirmation, again.Value!.Status);
        Assert.Empty(_email.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Sending_back_needs_a_reason(string? reason)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));

        var result = await SetStatus(db, id, Hr, WorkTaskStatus.InProgress, reason);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkTaskReviewRule.SendBackReasonRequiredMessage, result.Error);
        Assert.Equal(WorkTaskStatus.AwaitingConfirmation, (await Reload(db, id)).Status);
    }

    [Fact]
    public async Task A_reason_longer_than_500_is_refused()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));

        var result = await SetStatus(db, id, Hr, WorkTaskStatus.InProgress, new string('x', 501));

        Assert.Equal(WorkTaskReviewRule.SendBackReasonTooLongMessage, result.Error);
    }

    [Fact]
    public async Task Sending_back_returns_it_to_in_progress_with_the_reason()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));

        var result = await SetStatus(db, id, Hr, WorkTaskStatus.InProgress, "  Totals are off  ");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.InProgress, result.Value!.Status);
        Assert.Equal("Totals are off", result.Value.SentBackReason);
        Assert.NotNull(result.Value.SentBackAtUtc);

        // Resubmitted, the reason stays for the reviewer to read; confirmed, it goes.
        await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);
        Assert.Equal("Totals are off", (await Reload(db, id)).SentBackReason);
        await SetStatus(db, id, Hr, WorkTaskStatus.Done);
        Assert.Null((await Reload(db, id)).SentBackReason);
    }

    [Fact]
    public async Task An_assignee_may_withdraw_but_not_otherwise_move_a_waiting_task()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));

        var todo = await SetStatus(db, id, SalesManager, WorkTaskStatus.ToDo);
        Assert.Equal(WorkTaskReviewRule.AwaitingConfirmationMessage, todo.Error);

        var withdrawn = await SetStatus(db, id, SalesManager, WorkTaskStatus.InProgress);
        Assert.True(withdrawn.IsSuccess, withdrawn.Error);
        Assert.Equal(WorkTaskStatus.InProgress, withdrawn.Value!.Status);
        Assert.Null(withdrawn.Value.SentBackReason);
    }

    [Fact]
    public async Task Asking_for_awaiting_confirmation_directly_is_refused()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.AwaitingConfirmation);

        Assert.Equal(WorkTaskReviewRule.StageIsDerivedMessage, result.Error);
    }

    [Fact]
    public async Task A_bystander_reviewer_may_act_only_on_a_waiting_task()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.UserDepartments.Add(new UserDepartment { UserId = OpsManager, DepartmentId = Sales });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, OpsManager, WorkTaskStatus.Done);

        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }

    [Fact]
    public async Task With_nobody_left_to_review_done_closes_directly()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        // Ops: the creator is deactivated and its one active manager is the assignee.
        var id = await Seeded(db, NewTask(Ops, GoneManager, OpsManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, OpsManager, WorkTaskStatus.Done);

        Assert.Equal(WorkTaskStatus.Done, result.Value!.Status);
    }

    [Fact]
    public async Task Reopening_clears_the_confirmation()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));
        await SetStatus(db, id, Hr, WorkTaskStatus.Done);

        var reopened = await SetStatus(db, id, Hr, WorkTaskStatus.InProgress);

        Assert.True(reopened.IsSuccess, reopened.Error);
        var row = await Reload(db, id);
        Assert.Null(row.ConfirmedById);
        Assert.Null(row.CompletedAtUtc);
    }

    [Fact]
    public async Task The_creator_is_emailed_when_a_task_waits()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, "Chase notes", WorkTaskStatus.InProgress));

        await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        var mail = Assert.Single(_email.Sent);
        Assert.Equal($"{Hr}@t", mail.Recipient);
        Assert.Equal(WorkTaskReviewNotification.SubmittedSubjectPrefix + "Chase notes", mail.Subject);
    }

    [Fact]
    public async Task With_the_creator_gone_the_covering_reviewers_are_emailed()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        // Sales: creator deactivated; the Employee is on it; HR and the Sales manager cover it.
        var id = await Seeded(db, NewTask(Sales, OpsManager, Employee, status: WorkTaskStatus.InProgress));
        var opsManager = await db.Users.SingleAsync(u => u.Id == OpsManager);
        opsManager.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await SetStatus(db, id, Employee, WorkTaskStatus.Done, assignedOnly: true);

        Assert.Equal(
            new[] { $"{Hr}@t", $"{SalesManager}@t" },
            _email.Sent.Select(m => m.Recipient).OrderBy(r => r));
    }

    [Fact]
    public async Task Sending_back_emails_the_assignees_with_the_reason()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, SalesManager, "Chase notes", WorkTaskStatus.AwaitingConfirmation);
        task.Assignees.Add(new WorkTaskAssignee { UserId = Employee });
        var id = await Seeded(db, task);

        await SetStatus(db, id, Hr, WorkTaskStatus.InProgress, "Totals are off");

        Assert.Equal(new[] { $"{Employee}@t", $"{SalesManager}@t" }, _email.Sent.Select(m => m.Recipient).OrderBy(r => r));
        Assert.All(_email.Sent, m =>
        {
            Assert.Equal(WorkTaskReviewNotification.SentBackSubjectPrefix + "Chase notes", m.Subject);
            Assert.Contains("Totals are off", m.TextBody);
        });
    }

    [Fact]
    public async Task Confirming_and_withdrawing_email_nobody()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var confirmId = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));
        var withdrawId = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));

        await SetStatus(db, confirmId, Hr, WorkTaskStatus.Done);
        await SetStatus(db, withdrawId, SalesManager, WorkTaskStatus.InProgress);

        Assert.Empty(_email.Sent);
    }
}
