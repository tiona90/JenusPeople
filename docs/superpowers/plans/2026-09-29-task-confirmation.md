# Task Confirmation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An assignee's "Done" on a task somebody else handed them puts it in a new `AwaitingConfirmation` status; a reviewer (the creator, or a Manager/HR Administrator in scope who is not on the task) confirms it to Done or sends it back to In Progress with a reason.

**Architecture:** One new enum value plus three columns on `WorkTask`. The routing lives in a new `WorkTaskReviewRule` called from `UpdateWorkTaskStatus`; the client always asks for `Done` and the server decides whether that means Done or AwaitingConfirmation (the same shape as `ApprovalStageRule` for leave). Emails go through a new `WorkTaskReviewNotification`. The client adds the status to its palette, the card grows Confirm / Send back / Withdraw controls, and the Topbar bell lists tasks awaiting the caller's confirmation.

**Tech Stack:** ASP.NET Core 10, EF Core (SQL Server; SQLite/in-memory in tests), MediatR, xUnit; React 19 + TypeScript, MUI 7, React Query, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-29-task-confirmation-design.md`

## Global Constraints

- `WorkTaskStatus.AwaitingConfirmation = 4` — appended; existing values unchanged. Nothing is backfilled.
- Send-back reason: required, trimmed, 1–500 characters (`WorkTaskReviewRule.SendBackReasonMaxLength = 500`).
- `ConfirmedById` FK onto `User` is `Restrict` and must be nulled in `DeleteAdminUser` and `DbInitializer.CleanupUserDependencies`.
- Emails honour `AppSettings.EmailNotificationsEnabled` and never throw into the handler.
- Client copy: status label "Awaiting confirmation"; buttons "Confirm", "Send back", "Withdraw"; card note "Waiting for {createdByName} to confirm"; "Sent back: {reason}".
- The client never sends `AwaitingConfirmation`; the server refuses it (`StageIsDerivedMessage`).
- Test runs: `DOTNET_ROOT="C:\Program Files\dotnet" DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests --filter <name>`; if the API is running and locks the build output, add `--artifacts-path C:\Users\user\AppData\Local\Temp\wt-artifacts`. Client: from `client/`, `node_modules/.bin/vitest run <path>` (never bare `npx vitest`; run per directory, not the whole suite).

## Review Focus

1. **Double-click on "Mark done"** — the second request arrives while the task is already AwaitingConfirmation; it must be a no-op, not a 400. (Task 2, test `Marking_done_again_while_waiting_is_a_no_op`.)
2. **A Manager who is both in scope and an assignee** must not be able to confirm the task they worked on, even via the Status menu or the edit dialog. (Task 2, test `An_assignee_manager_cannot_confirm`.)
3. **Whitespace-only reason** ("   ") must be refused like an empty one. (Task 2, test `Sending_back_needs_a_reason`.)
4. **A task that has already been sent back and is resubmitted** must reach the reviewer's bell as unread again, even if they read the first submission. (Task 7, keyed by id + `updatedAtUtc`.)
5. **A reviewer confirming a task and then someone reopening it** must clear `ConfirmedById`/`CompletedAtUtc`, so a later Done restamps them. (Task 2, test `Reopening_clears_the_confirmation`.)

---

### Task 1: Status value, columns, migration, leaver cleanup

**Files:**
- Modify: `Domain/WorkTask.cs`
- Modify: `Persistence/AppDbContext.cs` (the `builder.Entity<WorkTask>` block, ~line 551)
- Create: `Persistence/Migrations/<timestamp>_AddWorkTaskConfirmation.cs` (generated)
- Modify: `Application/AdminUsers/Commands/DeleteAdminUser.cs` (~line 113, the Tasks block)
- Modify: `Persistence/DbInitializer.cs` (~line 543, the mirror of that block)
- Test: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskDeleteCleanupTests.cs`

**Interfaces:**
- Produces: `WorkTaskStatus.AwaitingConfirmation`; `WorkTask.ConfirmedById` (`string?`), `WorkTask.ConfirmedBy` (`User?`), `WorkTask.SentBackReason` (`string?`), `WorkTask.SentBackAtUtc` (`DateTime?`), `WorkTask.SentBackReasonMaxLength = 500`.

- [ ] **Step 1: Write the failing test** — append to `WorkTaskDeleteCleanupTests`:

```csharp
    [Fact]
    public async Task Deleting_the_user_who_confirmed_a_task_keeps_the_task()
    {
        await AddUserAsync("creator");
        await AddUserAsync("worker");
        await AddUserAsync("confirmer");
        Db.Departments.Add(new Department { Id = 1, Name = "Sales", Code = "SAL" });
        var task = NewTask("creator", "Confirmed", "worker");
        task.Status = WorkTaskStatus.Done;
        task.ConfirmedById = "confirmer";
        Db.WorkTasks.Add(task);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var result = await new DeleteAdminUser.Handler(Db, Users).Handle(
            new DeleteAdminUser.Command { Id = "confirmer" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var kept = await Db.WorkTasks.AsNoTracking().SingleAsync();
        Assert.Null(kept.ConfirmedById);
        Assert.Equal(WorkTaskStatus.Done, kept.Status);
    }
```

Check how the existing test in the file invokes `DeleteAdminUser` (command property name, whether the department is already seeded) and match it exactly.

- [ ] **Step 2: Run it — expect a compile failure** (`ConfirmedById` does not exist).

Run: `dotnet test Tests/WorkTrack.Tests --filter Deleting_the_user_who_confirmed_a_task_keeps_the_task`

- [ ] **Step 3: Add the enum value and columns** in `Domain/WorkTask.cs`:

```csharp
/// <summary>
/// Where a task stands. Any move between the first four is allowed; reopening a Done
/// task is ordinary. <see cref="AwaitingConfirmation"/> is never asked for directly:
/// an assignee's Done lands there when somebody else handed them the task, and a
/// reviewer's Confirm or Send back takes it out (WorkTaskReviewRule).
/// </summary>
public enum WorkTaskStatus
{
    ToDo = 0,
    InProgress = 1,
    Done = 2,
    Cancelled = 3,
    AwaitingConfirmation = 4,
}
```

In the class, beside `CompletedAtUtc`:

```csharp
    public const int SentBackReasonMaxLength = 500;

    /// <summary>Set on entering Done, cleared on leaving it.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Who moved it to Done — the reviewer who confirmed it. Cleared on leaving Done.</summary>
    public string? ConfirmedById { get; set; }
    public User? ConfirmedBy { get; set; }

    /// <summary>
    /// The last send-back: why a reviewer returned the work, and when. Kept while the
    /// task is worked on and resubmitted, so the reviewer sees what they asked for;
    /// cleared once it is confirmed.
    /// </summary>
    public string? SentBackReason { get; set; }
    public DateTime? SentBackAtUtc { get; set; }
```

- [ ] **Step 4: Configure the FK** in `AppDbContext`, inside `builder.Entity<WorkTask>` after the `CreatedBy` relationship:

```csharp
            entity.Property(t => t.ConfirmedById).HasMaxLength(450);
            entity.Property(t => t.SentBackReason).HasMaxLength(WorkTask.SentBackReasonMaxLength);
            // Restrict like the others: DeleteAdminUser (and its DbInitializer mirror)
            // nulls it before the user row goes.
            entity.HasOne(t => t.ConfirmedBy)
                .WithMany()
                .HasForeignKey(t => t.ConfirmedById)
                .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 5: Null it on delete.** In `DeleteAdminUser.cs`, directly before the `// Tasks.` comment block:

```csharp
            // A task they confirmed stays; only the record of who confirmed it goes.
            var confirmedTasks = await context.WorkTasks
                .Where(t => t.ConfirmedById == userId)
                .ToListAsync(cancellationToken);
            foreach (var task in confirmedTasks)
            {
                task.ConfirmedById = null;
            }
```

Add the same block (with `cancellationToken` named as that method names it) before the matching tasks block in `DbInitializer.CleanupUserDependencies`. Update the `// Tasks. Both foreign keys onto User are Restrict.` comment in both to say "All three".

- [ ] **Step 6: Generate the migration** (from the solution root):

```bash
dotnet ef migrations add AddWorkTaskConfirmation --project Persistence --startup-project API
```

If the API is running and the build is locked: `dotnet build API -c Migrate` then `dotnet ef migrations add AddWorkTaskConfirmation --project Persistence --startup-project API --configuration Migrate --no-build`. Open the generated file and check it adds exactly three nullable columns, one index on `ConfirmedById` and one FK with `ReferentialAction.Restrict`, and nothing else.

- [ ] **Step 7: Run the test — expect PASS**, plus the whole `WorkTaskDeleteCleanupTests` class and `DeleteUserStoredFileCleanupTests`.

- [ ] **Step 8: Commit**

```bash
git add Domain/WorkTask.cs Persistence/AppDbContext.cs Persistence/Migrations Application/AdminUsers/Commands/DeleteAdminUser.cs Persistence/DbInitializer.cs Tests/WorkTrack.Tests/WorkTasks/WorkTaskDeleteCleanupTests.cs
git commit -m "Add the Awaiting confirmation task status and its columns"
```

---

### Task 2: The review rule and the status command

**Files:**
- Create: `Application/WorkTasks/Support/WorkTaskReviewRule.cs`
- Modify: `Application/WorkTasks/Commands/UpdateWorkTaskStatus.cs`
- Modify: `Application/WorkTasks/DTOs/WorkTaskDto.cs` (`WorkTaskDto`, `UpdateWorkTaskStatusRequest`)
- Modify: `Application/WorkTasks/Support/WorkTaskProjection.cs`
- Modify: `API/Controllers/WorkTasksController.cs` (`UpdateWorkTaskStatus` action)
- Create: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskReviewTests.cs`
- Modify: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskCommandTests.cs`, `WorkTaskEmployeeTests.cs` (handler constructor, changed expectations)

**Interfaces:**
- Consumes: Task 1's enum value and columns.
- Produces:
  - `WorkTaskReviewRule.StageIsDerivedMessage`, `.AwaitingConfirmationMessage`, `.SendBackReasonRequiredMessage`, `.SendBackReasonTooLongMessage` (`const string`)
  - `static bool WorkTaskReviewRule.NeedsConfirmation(WorkTask task)` — task has an assignee other than its creator (requires `Assignees` loaded)
  - `static bool WorkTaskReviewRule.IsReviewer(WorkTask task, string callerUserId, bool assignedOnly)`
  - `static Task<List<string>> WorkTaskReviewRule.CoveringReviewerIdsAsync(AppDbContext, WorkTask, CancellationToken)` — active Managers/HR covering the department, not on the task
  - `UpdateWorkTaskStatus.Command.Reason` (`string?`); `UpdateWorkTaskStatus.Handler(AppDbContext, IEmailService, ILogger<Handler>)`
  - `UpdateWorkTaskStatusRequest.Reason` (`string?`)
  - `WorkTaskDto.CanConfirm` (`bool`), `.ConfirmedByName` (`string?`), `.SentBackReason` (`string?`), `.SentBackAtUtc` (`DateTime?`)

- [ ] **Step 1: Write the failing tests** — `Tests/WorkTrack.Tests/WorkTasks/WorkTaskReviewTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run — expect compile failures** (`Reason`, `CanConfirm`, the new handler constructor, `WorkTaskReviewRule`).

Run: `dotnet test Tests/WorkTrack.Tests --filter WorkTaskReviewTests`

- [ ] **Step 3: Create the rule** — `Application/WorkTasks/Support/WorkTaskReviewRule.cs`:

```csharp
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

/// <summary>
/// Whether a Done is the end of a task or a request to end it. A task somebody else
/// handed you waits in AwaitingConfirmation until a reviewer confirms it: its creator,
/// or a Manager or HR Administrator whose scope covers it and who is not on it —
/// nobody signs off their own work but the creator, who owns the task. A task whose
/// only assignee is its creator closes directly, and so does one with nobody left who
/// could review it, so a task never waits on nobody.
/// </summary>
public static class WorkTaskReviewRule
{
    public const string StageIsDerivedMessage =
        "A task waits for confirmation by being marked done; that status can't be chosen directly.";
    public const string AwaitingConfirmationMessage =
        "This task is waiting for confirmation. You can take it back to In progress, or wait for the reviewer.";
    public const string SendBackReasonRequiredMessage = "Say why the task is being sent back.";
    public static readonly string SendBackReasonTooLongMessage =
        $"The reason can be at most {WorkTask.SentBackReasonMaxLength} characters.";

    private static readonly List<string> ReviewerRoles = [AppRoles.Manager, AppRoles.HrAdministrator];

    /// <summary>Somebody other than the creator is on it. Needs <see cref="WorkTask.Assignees"/> loaded.</summary>
    public static bool NeedsConfirmation(WorkTask task) =>
        task.Assignees.Any(a => a.UserId != task.CreatedById);

    /// <summary>
    /// The caller may confirm or send back a task they can see. Visibility already
    /// proved scope; <paramref name="assignedOnly"/> (an Employee) reviews only what
    /// they created. Mirrored by <c>WorkTaskDto.CanConfirm</c> in WorkTaskProjection.
    /// </summary>
    public static bool IsReviewer(WorkTask task, string callerUserId, bool assignedOnly) =>
        task.CreatedById == callerUserId
        || (!assignedOnly && task.Assignees.All(a => a.UserId != callerUserId));

    /// <summary>The active Managers and HR Administrators covering the task's department who are not on it.</summary>
    public static async Task<List<string>> CoveringReviewerIdsAsync(
        AppDbContext context, WorkTask task, CancellationToken cancellationToken)
    {
        var roleIds = await context.Roles
            .Where(r => r.Name != null && ReviewerRoles.Contains(r.Name))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var covering = context.EmployeeProfiles
            .Where(ep => ep.DepartmentId == task.DepartmentId)
            .Select(ep => ep.UserId)
            .Concat(context.UserDepartments
                .Where(ud => ud.DepartmentId == task.DepartmentId)
                .Select(ud => ud.UserId));

        var assigneeIds = task.Assignees.Select(a => a.UserId).ToList();
        return await (
                from ur in context.UserRoles
                where roleIds.Contains(ur.RoleId)
                join u in context.Users on ur.UserId equals u.Id
                where u.IsActive && covering.Contains(u.Id) && !assigneeIds.Contains(u.Id)
                select u.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>Somebody could confirm it: the creator while active, or a covering reviewer.</summary>
    public static async Task<bool> AnyReviewerAsync(AppDbContext context, WorkTask task, CancellationToken cancellationToken) =>
        await context.Users.AnyAsync(u => u.Id == task.CreatedById && u.IsActive, cancellationToken)
        || (await CoveringReviewerIdsAsync(context, task, cancellationToken)).Count > 0;
}
```

- [ ] **Step 4: Rewrite the handler** — `UpdateWorkTaskStatus.cs`:

```csharp
using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks.Commands;

public class UpdateWorkTaskStatus
{
    public class Command : IRequest<Result<WorkTaskDto>>
    {
        public int Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public WorkTaskStatus Status { get; set; }

        /// <summary>Why a reviewer sends a waiting task back. Required then, ignored otherwise.</summary>
        public string? Reason { get; set; }

        /// <summary>An Employee: only a task they are on or created is visible.</summary>
        public bool AssignedOnly { get; set; }
    }

    public class Handler(AppDbContext context, IEmailService emailService, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<WorkTaskDto>>
    {
        public async Task<Result<WorkTaskDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.Id, request.CallerUserId, cancellationToken, request.AssignedOnly);
            if (task is null)
                return Result<WorkTaskDto>.Failure(WorkTaskAccess.NotFoundMessage);

            var waiting = task.Status == WorkTaskStatus.AwaitingConfirmation;
            var isReviewer = WorkTaskReviewRule.IsReviewer(task, request.CallerUserId, request.AssignedOnly);
            var participates = task.Assignees.Any(a => a.UserId == request.CallerUserId)
                || await WorkTaskAccess.CanManageAsync(context, task, request.CallerUserId, cancellationToken, request.AssignedOnly);
            // A reviewer who is neither on the task nor its manager acts on it only once it waits for them.
            if (!participates && !(waiting && isReviewer))
                return Result<WorkTaskDto>.Forbidden(WorkTaskAccess.NotParticipantMessage);

            if (request.Status == WorkTaskStatus.AwaitingConfirmation && !waiting)
                return Result<WorkTaskDto>.Failure(WorkTaskReviewRule.StageIsDerivedMessage);

            var target = request.Status;
            var sendingBack = false;
            if (waiting && !isReviewer)
            {
                // The assignee's Done is already given: a repeat moves nothing. They may
                // take the task back to In progress; everything else is the reviewer's.
                if (target is WorkTaskStatus.Done or WorkTaskStatus.AwaitingConfirmation)
                    return await DtoAsync(task.Id, request, cancellationToken);
                if (target != WorkTaskStatus.InProgress)
                    return Result<WorkTaskDto>.Failure(WorkTaskReviewRule.AwaitingConfirmationMessage);
            }
            else if (waiting && (target is WorkTaskStatus.ToDo or WorkTaskStatus.InProgress))
            {
                sendingBack = true;
            }
            else if (target == WorkTaskStatus.Done && !waiting && !isReviewer
                && WorkTaskReviewRule.NeedsConfirmation(task)
                && await WorkTaskReviewRule.AnyReviewerAsync(context, task, cancellationToken))
            {
                target = WorkTaskStatus.AwaitingConfirmation;
            }

            string? reason = null;
            if (sendingBack)
            {
                reason = request.Reason?.Trim();
                if (string.IsNullOrEmpty(reason))
                    return Result<WorkTaskDto>.Failure(WorkTaskReviewRule.SendBackReasonRequiredMessage);
                if (reason.Length > WorkTask.SentBackReasonMaxLength)
                    return Result<WorkTaskDto>.Failure(WorkTaskReviewRule.SendBackReasonTooLongMessage);
            }

            if (task.Status != target)
            {
                var now = DateTime.UtcNow;
                if (target == WorkTaskStatus.Done)
                {
                    task.CompletedAtUtc = now;
                    task.ConfirmedById = request.CallerUserId;
                    task.SentBackReason = null;
                    task.SentBackAtUtc = null;
                }
                else
                {
                    task.CompletedAtUtc = null;
                    task.ConfirmedById = null;
                }
                if (sendingBack)
                {
                    task.SentBackReason = reason;
                    task.SentBackAtUtc = now;
                }
                task.Status = target;
                task.UpdatedAtUtc = now;
                await context.SaveChangesAsync(cancellationToken);
            }

            return await DtoAsync(task.Id, request, cancellationToken);
        }

        private async Task<Result<WorkTaskDto>> DtoAsync(int id, Command request, CancellationToken cancellationToken) =>
            Result<WorkTaskDto>.Success(
                await WorkTaskProjection.LoadDtoAsync(context, id, request.CallerUserId, cancellationToken, callerManages: !request.AssignedOnly));
    }
}
```

`emailService` and `logger` are unused until Task 3; that is fine.

- [ ] **Step 5: DTO and request.** In `WorkTaskDto` after `CanChangeStatus`:

```csharp
    /// <summary>The task waits for confirmation and the caller may give it: see WorkTaskReviewRule.IsReviewer.</summary>
    public bool CanConfirm { get; set; }

    /// <summary>Who confirmed it; null unless Done.</summary>
    public string? ConfirmedByName { get; set; }

    /// <summary>The last send-back, kept until the task is confirmed.</summary>
    public string? SentBackReason { get; set; }
    public DateTime? SentBackAtUtc { get; set; }
```

In `UpdateWorkTaskStatusRequest`:

```csharp
    /// <summary>Required when a reviewer sends a waiting task back.</summary>
    public string? Reason { get; set; }
```

- [ ] **Step 6: Projection.** In `WorkTaskProjection.Project`, after `CanChangeStatus`:

```csharp
            // Mirrors WorkTaskReviewRule.IsReviewer: visible means in scope.
            CanConfirm = t.Status == WorkTaskStatus.AwaitingConfirmation
                && (t.CreatedById == callerUserId || (callerManages && !t.Assignees.Any(a => a.UserId == callerUserId))),
            ConfirmedByName = t.ConfirmedBy == null ? null
                : !string.IsNullOrWhiteSpace(t.ConfirmedBy.DisplayName) ? t.ConfirmedBy.DisplayName : (t.ConfirmedBy.Email ?? ""),
            SentBackReason = t.SentBackReason,
            SentBackAtUtc = t.SentBackAtUtc,
```

- [ ] **Step 7: Controller.** In `WorkTasksController.UpdateWorkTaskStatus`, pass the reason:

```csharp
            Id = id, CallerUserId = CallerUserId, Status = request.Status, Reason = request.Reason, AssignedOnly = AssignedOnly,
```

- [ ] **Step 8: Update the existing tests.**
  - `WorkTaskCommandTests.SetStatus`: make it non-static and construct `new UpdateWorkTaskStatus.Handler(db, _email, NullLogger<UpdateWorkTaskStatus.Handler>.Instance)`.
  - `The_assignee_may_move_the_status_and_done_stamps_completion`: after the assignee's Done assert `WorkTaskStatus.AwaitingConfirmation` and `Null(CompletedAtUtc)`; then `var confirmed = await SetStatus(db, id, Hr, WorkTaskStatus.Done);` assert `NotNull(confirmed.Value!.CompletedAtUtc)`; the reopen step then calls `SetStatus(db, id, SalesManager, WorkTaskStatus.InProgress)` and asserts `Null(CompletedAtUtc)`. Rename the test `The_assignee_may_move_the_status_and_confirmed_done_stamps_completion`.
  - `Any_assignee_may_move_the_status`: assert `WorkTaskStatus.AwaitingConfirmation` instead of `Done`.
  - `WorkTaskEmployeeTests.An_employee_moves_the_status_of_their_own_task_only`: construct the handler with `_email` and `NullLogger<UpdateWorkTaskStatus.Handler>.Instance` (make `Move` use the instance field; add `using Microsoft.Extensions.Logging.Abstractions;` if missing), and assert `WorkTaskStatus.AwaitingConfirmation`.
  - Search for any other `new UpdateWorkTaskStatus.Handler(` in `Tests/` and update it the same way.

- [ ] **Step 9: Run — expect PASS:** `dotnet test Tests/WorkTrack.Tests --filter FullyQualifiedName~WorkTrack.Tests.WorkTasks`

- [ ] **Step 10: Commit**

```bash
git add Application/WorkTasks API/Controllers/WorkTasksController.cs Tests/WorkTrack.Tests/WorkTasks
git commit -m "Hold an assignee's Done for a reviewer to confirm or send back"
```

---

### Task 3: Review emails

**Files:**
- Create: `Application/WorkTasks/WorkTaskReviewNotification.cs`
- Modify: `Application/WorkTasks/Commands/UpdateWorkTaskStatus.cs`
- Test: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskReviewTests.cs`

**Interfaces:**
- Consumes: `WorkTaskReviewRule.CoveringReviewerIdsAsync`, the handler from Task 2.
- Produces: `WorkTaskReviewNotification.SubmittedSubjectPrefix = "Task to confirm: "`, `.SentBackSubjectPrefix = "Task sent back: "`; `SubmittedAsync(AppDbContext, IEmailService, ILogger, WorkTask, string doneByUserId, CancellationToken)`; `SentBackAsync(AppDbContext, IEmailService, ILogger, WorkTask, string reviewerUserId, string reason, CancellationToken)`.

- [ ] **Step 1: Write the failing tests** — append to `WorkTaskReviewTests`:

```csharp
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
```

Check the `SentEmail` record's property names in `TestSupport.cs` (`Recipient`, `Subject`, `TextBody` or similar) and match them.

- [ ] **Step 2: Run — expect FAIL** (`WorkTaskReviewNotification` missing).

- [ ] **Step 3: Create the notification** — `Application/WorkTasks/WorkTaskReviewNotification.cs`:

```csharp
using Application.Core;
using Application.WorkTasks.Support;
using Domain;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks;

/// <summary>
/// The two review emails. "Task to confirm" goes to the creator when an assignee's Done
/// puts a task in AwaitingConfirmation — or, with the creator deactivated, to the
/// Managers and HR Administrators covering the department who are not on it. "Task
/// sent back" goes to every assignee but the reviewer, with the reason. Confirming
/// and withdrawing send nothing. Honours EmailNotificationsEnabled and never throws.
/// </summary>
public static class WorkTaskReviewNotification
{
    public const string SubmittedSubjectPrefix = "Task to confirm: ";
    public const string SentBackSubjectPrefix = "Task sent back: ";

    public static async Task SubmittedAsync(
        AppDbContext context, IEmailService emailService, ILogger logger, WorkTask task,
        string doneByUserId, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnabledAsync(context, cancellationToken))
                return;
            var creatorActive = await context.Users.AnyAsync(u => u.Id == task.CreatedById && u.IsActive, cancellationToken);
            var recipients = creatorActive
                ? [task.CreatedById]
                : await WorkTaskReviewRule.CoveringReviewerIdsAsync(context, task, cancellationToken);
            recipients.Remove(doneByUserId);

            var people = await PeopleAsync(context, recipients.Append(doneByUserId), cancellationToken);
            var doneByName = people.GetValueOrDefault(doneByUserId)?.Name ?? "A colleague";
            var departmentName = await DepartmentNameAsync(context, task, cancellationToken);

            foreach (var id in recipients)
            {
                if (people.GetValueOrDefault(id) is not { Email: { Length: > 0 } email } person)
                    continue;
                var body = NotificationEmail
                    .To(person.Name)
                    .Sentence($"{doneByName} has marked a task as done: {task.Title}.")
                    .Detail("Department", departmentName)
                    .Detail("Last sent back for", task.SentBackReason)
                    .Closing("Please log in and open Tasks to confirm it or send it back.")
                    .Build();
                await emailService.SendEmailAsync(email, SubmittedSubjectPrefix + task.Title, body.Html, body.Text, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Task confirmation email for task {TaskId} could not be sent", task.Id);
        }
    }

    public static async Task SentBackAsync(
        AppDbContext context, IEmailService emailService, ILogger logger, WorkTask task,
        string reviewerUserId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnabledAsync(context, cancellationToken))
                return;
            var recipients = task.Assignees.Select(a => a.UserId).Where(id => id != reviewerUserId).Distinct().ToList();
            var people = await PeopleAsync(context, recipients.Append(reviewerUserId), cancellationToken);
            var reviewerName = people.GetValueOrDefault(reviewerUserId)?.Name ?? "A colleague";

            foreach (var id in recipients)
            {
                if (people.GetValueOrDefault(id) is not { Email: { Length: > 0 } email } person)
                    continue;
                var body = NotificationEmail
                    .To(person.Name)
                    .Sentence($"{reviewerName} has sent a task back to you: {task.Title}.")
                    .Detail("Reason", reason)
                    .Closing("Please log in and open Tasks to pick it up again.")
                    .Build();
                await emailService.SendEmailAsync(email, SentBackSubjectPrefix + task.Title, body.Html, body.Text, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Task send-back email for task {TaskId} could not be sent", task.Id);
        }
    }

    private sealed record Person(string? Email, string Name);

    private static async Task<bool> EnabledAsync(AppDbContext context, CancellationToken cancellationToken) =>
        (await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new AppSettings()).EmailNotificationsEnabled;

    private static async Task<Dictionary<string, Person>> PeopleAsync(
        AppDbContext context, IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await context.Users.AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => new Person(u.Email, !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : (u.Email ?? "")),
                cancellationToken);
    }

    private static Task<string?> DepartmentNameAsync(AppDbContext context, WorkTask task, CancellationToken cancellationToken) =>
        context.Departments.AsNoTracking().Where(d => d.Id == task.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken);
}
```

If `NotificationEmail.Sentence` rejects a `FormattableString` built this way, copy the exact call shape from `WorkTaskAssignmentNotification`. If `ToDictionaryAsync` with a projection into a record trips EF translation, select `new { u.Id, u.Email, u.DisplayName }` to a list first and build the dictionary in memory.

- [ ] **Step 4: Call it from the handler**, right after `await context.SaveChangesAsync(cancellationToken);` in `UpdateWorkTaskStatus`:

```csharp
                if (target == WorkTaskStatus.AwaitingConfirmation)
                    await WorkTaskReviewNotification.SubmittedAsync(context, emailService, logger, task, request.CallerUserId, cancellationToken);
                else if (sendingBack)
                    await WorkTaskReviewNotification.SentBackAsync(context, emailService, logger, task, request.CallerUserId, reason!, cancellationToken);
```

- [ ] **Step 5: Run — expect PASS:** `dotnet test Tests/WorkTrack.Tests --filter WorkTaskReviewTests`

- [ ] **Step 6: Commit**

```bash
git add Application/WorkTasks Tests/WorkTrack.Tests/WorkTasks/WorkTaskReviewTests.cs
git commit -m "Email the reviewer when a task waits and the assignees when it is sent back"
```

---

### Task 4: Timesheets and the idle panel read the new status

**Files:**
- Modify: `Application/Timesheets/Support/TimesheetEntryTaskRule.cs:48`
- Modify: `Application/WorkTasks/Queries/GetTimesheetTaskOptions.cs:74`
- Test: `Tests/WorkTrack.Tests/TimesheetEntryTaskRuleTests.cs`, `Tests/WorkTrack.Tests/WorkTasks/GetIdleTaskPeopleTests.cs`

**Interfaces:**
- Consumes: `WorkTaskStatus.AwaitingConfirmation`.

- [ ] **Step 1: Write the failing tests.** In `TimesheetEntryTaskRuleTests`, add a constant `private const int WaitingTask = 104;`, add `Task(WaitingTask, ProjectA, OwnerUserId, WorkTaskStatus.AwaitingConfirmation)` to the `AddRange` in `SeedWorld`, and:

```csharp
    [Fact]
    public async Task A_task_waiting_for_confirmation_can_still_be_logged()
    {
        using var db = SeedWorld();
        await ControllerFor(db).AddEntry(TimesheetId, Entry(WaitingTask), CancellationToken.None);
        Assert.Equal(WaitingTask, (await db.TimesheetEntries.SingleAsync()).WorkTaskId);
    }
```

Check that no other test in that file counts the seeded tasks (for instance an options test expecting an exact list); if one does, add `WaitingTask` to its expectation — it is an open task now.

In `GetIdleTaskPeopleTests`:

```csharp
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
```

- [ ] **Step 2: Run — expect the timesheet test to FAIL** with `ClosedMessage`; the idle test should already pass (the query counts only ToDo and InProgress) — it pins the behaviour.

- [ ] **Step 3: Implement.** `TimesheetEntryTaskRule.cs:48`:

```csharp
        // Waiting for confirmation is still open: hours from that week may be logged after the Done.
        if (task.Status is not (WorkTaskStatus.ToDo or WorkTaskStatus.InProgress or WorkTaskStatus.AwaitingConfirmation)) return ClosedMessage;
```

`GetTimesheetTaskOptions.cs:74`:

```csharp
                        && (t.Status == WorkTaskStatus.ToDo || t.Status == WorkTaskStatus.InProgress
                            || t.Status == WorkTaskStatus.AwaitingConfirmation)
```

- [ ] **Step 4: Run — expect PASS:** `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~TimesheetEntryTaskRuleTests|FullyQualifiedName~GetIdleTaskPeopleTests|FullyQualifiedName~GetTimesheetTaskOptionsTests"`

- [ ] **Step 5: Commit**

```bash
git add Application/Timesheets/Support/TimesheetEntryTaskRule.cs Application/WorkTasks/Queries/GetTimesheetTaskOptions.cs Tests/WorkTrack.Tests
git commit -m "Keep a task waiting for confirmation open for timesheet logging"
```

---

### Task 5: Client types, API and task helpers

**Files:**
- Modify: `client/src/lib/types/work-task.ts`
- Modify: `client/src/lib/api/work-tasks.ts:42`
- Modify: `client/src/lib/work-tasks.ts`
- Modify: `client/src/components/tasks/statusStyles.ts`
- Test: `client/src/lib/work-tasks.test.ts` (create if absent; otherwise append)

**Interfaces:**
- Produces:
  - `WorkTaskStatus` includes `'AwaitingConfirmation'`
  - `WorkTask.canConfirm?: boolean`, `.confirmedByName?: string | null`, `.sentBackReason?: string | null`, `.sentBackAtUtc?: string | null` (optional: an older API omits them)
  - `updateWorkTaskStatus(id: number, status: WorkTaskStatus, reason?: string)` — sends `{ status }` or `{ status, reason }`
  - `SETTABLE_STATUSES: WorkTaskStatus[]` = `['ToDo', 'InProgress', 'Done', 'Cancelled']` — what a menu or select may offer
  - `isAwaitingConfirmation(task: WorkTask): boolean`
  - `taskStats(...)` gains `awaitingMyConfirmation: number` (tasks with `canConfirm`)
  - `SEND_BACK_REASON_MAX = 500`

- [ ] **Step 1: Write the failing tests** — `client/src/lib/work-tasks.test.ts` (reuse an existing `base` task factory if the file exists):

```ts
import { describe, expect, it } from 'vitest'
import type { WorkTask } from './types'
import { SETTABLE_STATUSES, STATUS_LABELS, filterTasks, isAwaitingConfirmation, isOpenTask, isOverdue, nextStatusAction, taskStats } from './work-tasks'

const task = (over: Partial<WorkTask> = {}): WorkTask => ({
    id: 1, title: 'T', description: null, departmentId: 1, departmentName: 'Sales',
    projectId: 10, projectName: 'CRM', projectCode: 'CRM', projectColorKey: 'p1',
    assignees: [{ userId: 'me', displayName: 'Me' }], createdById: 'boss', createdByName: 'Boss',
    dueDate: '2020-01-01', targetHours: null, loggedHours: 0, isBillable: true, priority: 'Normal', status: 'ToDo',
    createdAtUtc: '2026-09-01T08:00:00', updatedAtUtc: '2026-09-01T08:00:00', completedAtUtc: null,
    canEdit: false, canChangeStatus: true, ...over,
})

describe('awaiting confirmation', () => {
    it('is open, but never overdue — the work is in', () => {
        const waiting = task({ status: 'AwaitingConfirmation' })
        expect(isAwaitingConfirmation(waiting)).toBe(true)
        expect(isOpenTask(waiting)).toBe(true)
        expect(isOverdue(waiting, '2026-09-29')).toBe(false)
    })

    it('is labelled, and is never offered as a status to pick', () => {
        expect(STATUS_LABELS.AwaitingConfirmation).toBe('Awaiting confirmation')
        expect(SETTABLE_STATUSES).toEqual(['ToDo', 'InProgress', 'Done', 'Cancelled'])
    })

    it("offers the assignee Withdraw as the card's next step", () => {
        expect(nextStatusAction('AwaitingConfirmation')).toEqual({ label: 'Withdraw', icon: '↺', to: 'InProgress' })
    })

    it('counts the tasks waiting for the viewer, and filters to waiting ones', () => {
        const tasks = [task({ id: 1, status: 'AwaitingConfirmation', canConfirm: true }), task({ id: 2, status: 'AwaitingConfirmation' }), task({ id: 3 })]
        expect(taskStats(tasks, 'me', '2026-09-29').awaitingMyConfirmation).toBe(1)
        expect(filterTasks(tasks, { tab: 'all', status: 'AwaitingConfirmation', departmentId: null, userId: 'me', priority: 'any', search: '' }).map((t) => t.id)).toEqual([1, 2])
    })
})
```

Check `filterTasks`'s actual filter-object shape (around line 70 of `work-tasks.ts`) and match it.

- [ ] **Step 2: Run — expect FAIL:** from `client/`, `node_modules/.bin/vitest run src/lib/work-tasks.test.ts`

- [ ] **Step 3: Implement.**

`types/work-task.ts`:

```ts
export type WorkTaskStatus = 'ToDo' | 'InProgress' | 'Done' | 'Cancelled' | 'AwaitingConfirmation'
```

and in `WorkTask` after `canChangeStatus`:

```ts
    /** Waiting for confirmation, and the caller may confirm it or send it back. Missing from an older API. */
    canConfirm?: boolean
    /** Who confirmed it; set only on a Done task. */
    confirmedByName?: string | null
    /** The last send-back, kept until the task is confirmed. */
    sentBackReason?: string | null
    sentBackAtUtc?: string | null
```

`api/work-tasks.ts`:

```ts
/** The server decides what Done means: an assignee's lands in AwaitingConfirmation. `reason` is required to send a waiting task back. */
export async function updateWorkTaskStatus(id: number, status: WorkTaskStatus, reason?: string) {
    const response = await apiClient.patch<WorkTask>(`/worktasks/${id}/status`, reason === undefined ? { status } : { status, reason })
```

(keep the rest of the function body as it is).

`work-tasks.ts`:

```ts
export const STATUS_LABELS: Record<WorkTaskStatus, string> = {
    ToDo: 'To do',
    InProgress: 'In progress',
    AwaitingConfirmation: 'Awaiting confirmation',
    Done: 'Done',
    Cancelled: 'Cancelled',
}

/** What a Status menu or select may offer. Awaiting confirmation is reached by marking a task done, never picked. */
export const SETTABLE_STATUSES: WorkTaskStatus[] = ['ToDo', 'InProgress', 'Done', 'Cancelled']

/** Mirrors WorkTask.SentBackReasonMaxLength. */
export const SEND_BACK_REASON_MAX = 500
```

In `nextStatusAction` add `case 'AwaitingConfirmation': return { label: 'Withdraw', icon: '↺', to: 'InProgress' }`.

```ts
export function isAwaitingConfirmation(task: WorkTask): boolean {
    return task.status === 'AwaitingConfirmation'
}

export function isOpenTask(task: WorkTask): boolean {
    return task.status === 'ToDo' || task.status === 'InProgress' || task.status === 'AwaitingConfirmation'
}

/** Due dates are `YYYY-MM-DD`, so a string comparison is a date comparison. A task waiting for confirmation is handed in, so it is never late. */
export function isOverdue(task: WorkTask, today: string): boolean {
    return isOpenTask(task) && !isAwaitingConfirmation(task) && task.dueDate != null && task.dueDate.slice(0, 10) < today
}
```

Update the `StatusFilter` comment to "`open` is To do, In progress and Awaiting confirmation". In `taskStats` add `awaitingMyConfirmation: tasks.filter((t) => t.canConfirm === true).length,`.

`statusStyles.ts` — add to `STATUS_COLORS`:

```ts
    AwaitingConfirmation: { bg: softBg('warning'), fg: 'warning.dark', dot: 'warning.main' },
```

- [ ] **Step 4: Fix the type fallout.** Run `npx tsc -b` from `client/` (or `npm run build`); every `Record<WorkTaskStatus, …>` and exhaustive `switch` must now handle the new value. Replace `Object.keys(STATUS_LABELS) as WorkTaskStatus[]` with `SETTABLE_STATUSES` in the `TasksPage` Status menu and the `TaskDialog` status select (leave the page's **filter** on `STATUS_LABELS`, so "Awaiting confirmation" is filterable). In `TaskDialog`, when the task's current status is `AwaitingConfirmation`, render that value as a disabled extra `MenuItem` so the select is not blank.

- [ ] **Step 5: Run — expect PASS:** `node_modules/.bin/vitest run src/lib/work-tasks.test.ts src/components/tasks`

- [ ] **Step 6: Commit**

```bash
git add client/src/lib client/src/components/tasks/statusStyles.ts client/src/components/tasks/TaskDialog.tsx client/src/components/tasks/TasksPage.tsx
git commit -m "Teach the client the Awaiting confirmation status"
```

---

### Task 6: The card's Confirm, Send back and Withdraw

**Files:**
- Modify: `client/src/components/tasks/TasksPage.tsx` (`TasksPage` mutation and tiles, `TaskCard`, `StatusControls`)
- Create: `client/src/components/tasks/SendBackDialog.tsx`
- Test: `client/src/components/tasks/TasksPage.test.tsx`

**Interfaces:**
- Consumes: `updateWorkTaskStatus(id, status, reason?)`, `SEND_BACK_REASON_MAX`, `isAwaitingConfirmation`, `taskStats().awaitingMyConfirmation`.
- Produces: `SendBackDialog({ open, taskTitle, pending, error, onCancel, onSubmit: (reason: string) => void })`.

- [ ] **Step 1: Write the failing tests** — append inside the existing `describe` in `TasksPage.test.tsx`:

```tsx
    it('gives a reviewer Confirm and Send back on a waiting task', async () => {
        api.updateWorkTaskStatus.mockResolvedValue({} as never)
        api.getWorkTasks.mockResolvedValue([{ ...TASKS[1], status: 'AwaitingConfirmation', canConfirm: true }])
        renderPage()
        showView('created')
        const card = await cardFor('I asked for this')

        fireEvent.click(within(card).getByRole('button', { name: /Confirm/ }))
        await waitFor(() => expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(2, 'Done'))
    })

    it('asks the reviewer why before sending a task back', async () => {
        api.updateWorkTaskStatus.mockResolvedValue({} as never)
        api.getWorkTasks.mockResolvedValue([{ ...TASKS[1], status: 'AwaitingConfirmation', canConfirm: true }])
        renderPage()
        showView('created')
        const card = await cardFor('I asked for this')

        fireEvent.click(within(card).getByRole('button', { name: /Send back/ }))
        const dialog = await screen.findByRole('dialog')
        const send = within(dialog).getByRole('button', { name: 'Send back' })
        expect(send).toBeDisabled()
        fireEvent.change(within(dialog).getByRole('textbox', { name: /Reason/ }), { target: { value: '   ' } })
        expect(send).toBeDisabled()
        fireEvent.change(within(dialog).getByRole('textbox', { name: /Reason/ }), { target: { value: 'Totals are off' } })
        fireEvent.click(send)

        await waitFor(() => expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(2, 'InProgress', 'Totals are off'))
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    })

    it('tells the assignee who will confirm, and lets them withdraw', async () => {
        api.updateWorkTaskStatus.mockResolvedValue({} as never)
        api.getWorkTasks.mockResolvedValue([{ ...base, status: 'AwaitingConfirmation', canConfirm: false }])
        renderPage()
        const card = await cardFor('Mine to do')

        expect(within(card).getByText('Waiting for Boss to confirm')).toBeInTheDocument()
        expect(within(card).queryByRole('button', { name: /Confirm/ })).not.toBeInTheDocument()
        fireEvent.click(within(card).getByRole('button', { name: /Withdraw/ }))
        await waitFor(() => expect(api.updateWorkTaskStatus).toHaveBeenCalledWith(1, 'InProgress'))
    })

    it('shows why a task was sent back', async () => {
        api.getWorkTasks.mockResolvedValue([{ ...base, status: 'InProgress', sentBackReason: 'Totals are off' }])
        renderPage()
        expect(within(await cardFor('Mine to do')).getByText('Sent back: Totals are off')).toBeInTheDocument()
    })

    it('counts the tasks waiting for my confirmation in a tile', async () => {
        api.getWorkTasks.mockResolvedValue([{ ...TASKS[1], status: 'AwaitingConfirmation', canConfirm: true }])
        renderPage()
        expect(within(await screen.findByTestId('stat-confirm')).getByText('1')).toBeInTheDocument()
    })
```

Also update the existing test `offers every status from the Status menu, marking the current one` only if its expected list changes — it must stay `['To do', 'In progress', 'Done', 'Cancelled']`.

- [ ] **Step 2: Run — expect FAIL:** `node_modules/.bin/vitest run src/components/tasks/TasksPage.test.tsx`

- [ ] **Step 3: Create `SendBackDialog.tsx`:**

```tsx
import { useEffect, useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'
import { SEND_BACK_REASON_MAX } from '../../lib/work-tasks'

/** Why a reviewer is returning a waiting task. The reason is required and reaches the assignees by email. */
export default function SendBackDialog({ open, taskTitle, pending, error, onCancel, onSubmit }: {
    open: boolean
    taskTitle: string
    pending: boolean
    error: string | null
    onCancel: () => void
    onSubmit: (reason: string) => void
}) {
    const [reason, setReason] = useState('')
    useEffect(() => { if (open) setReason('') }, [open])
    const trimmed = reason.trim()

    return (
        <Dialog open={open} onClose={pending ? undefined : onCancel} fullWidth maxWidth="sm">
            <DialogTitle>Send back "{taskTitle}"</DialogTitle>
            <DialogContent>
                {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
                <TextField
                    label="Reason"
                    required
                    fullWidth
                    multiline
                    minRows={3}
                    autoFocus
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    slotProps={{ htmlInput: { maxLength: SEND_BACK_REASON_MAX } }}
                    helperText={`${reason.length}/${SEND_BACK_REASON_MAX} · the assignees are emailed this`}
                    sx={{ mt: 1 }}
                />
            </DialogContent>
            <DialogActions>
                <Button onClick={onCancel} disabled={pending}>Cancel</Button>
                <Button variant="contained" color="warning" disabled={pending || trimmed.length === 0} onClick={() => onSubmit(trimmed)}>
                    Send back
                </Button>
            </DialogActions>
        </Dialog>
    )
}
```

- [ ] **Step 4: Wire the page.** In `TasksPage`:

```tsx
    const moveStatus = useMutation({
        mutationFn: ({ id, next, reason }: { id: number; next: WorkTaskStatus; reason?: string }) =>
            reason === undefined ? updateWorkTaskStatus(id, next) : updateWorkTaskStatus(id, next, reason),
        onSuccess: refresh,
    })
    // The waiting task a reviewer is sending back, while its reason dialog is open.
    const [sendingBack, setSendingBack] = useState<WorkTask | null>(null)
```

Render once, near the other dialogs:

```tsx
            <SendBackDialog
                open={sendingBack != null}
                taskTitle={sendingBack?.title ?? ''}
                pending={moveStatus.isPending}
                error={sendingBack && moveStatus.error ? apiErrorMessage(moveStatus.error) : null}
                onCancel={() => { setSendingBack(null); moveStatus.reset() }}
                onSubmit={(reason) => moveStatus.mutate(
                    { id: sendingBack!.id, next: 'InProgress', reason },
                    { onSuccess: () => setSendingBack(null) },
                )}
            />
```

Use whatever helper the page already uses to turn `mutationError` into text in place of `apiErrorMessage` (search the file for how `mutationError` is rendered).

Pass `onSendBack={() => { moveStatus.reset(); setSendingBack(task) }}` to each of the three `TaskCard` renderings, and add `onSendBack: () => void` to `TaskCard`'s props.

Add a tile, shown to Managers and HR only (`manages`), before the Overdue tile; widen the grid by one column when it shows (`repeat(${(isHr ? 3 : 4) + (manages ? 1 : 0)}, 1fr)`):

```tsx
                {manages && <Box data-testid="stat-confirm">
                    <StatCard
                        label="🕓 To Confirm"
                        value={String(stats.awaitingMyConfirmation)}
                        valueColor={stats.awaitingMyConfirmation > 0 ? 'warning.main' : undefined}
                        sub="done, waiting for you"
                    />
                </Box>}
```

- [ ] **Step 5: The card footer.** Replace the `task.canChangeStatus ? <StatusControls …/> : …` block in `TaskCard`:

```tsx
                {task.canConfirm ? (
                    <ReviewControls pending={statusPending} onConfirm={() => onStatus('Done')} onSendBack={onSendBack} />
                ) : task.canChangeStatus ? (
                    <StatusControls status={task.status} pending={statusPending} onStatus={onStatus} />
                ) : (
                    <Box sx={{ fontSize: 11, color: 'text.disabled' }}>Only the creator and assignees change the status</Box>
                )}
```

In `StatusControls`, when `status === 'AwaitingConfirmation'`, render only the main button (which `nextStatusAction` already makes "Withdraw") — no Status menu, since the server refuses every other move from an assignee:

```tsx
    const waiting = status === 'AwaitingConfirmation'
    …
            {!waiting && (
                <>
                    {/* the existing Status button and Menu, with SETTABLE_STATUSES */}
                </>
            )}
```

Add, beside `StatusControls`:

```tsx
/** A reviewer's two answers to a task marked done. */
function ReviewControls({ pending, onConfirm, onSendBack }: { pending: boolean; onConfirm: () => void; onSendBack: () => void }) {
    const btn = {
        display: 'inline-flex', alignItems: 'center', gap: '6px', borderRadius: '6px', px: '12px', py: '6px',
        fontSize: 12, fontWeight: 600, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
        '&:disabled': { opacity: 0.6, cursor: 'default' },
    } as const
    return (
        <>
            <Box component="button" type="button" disabled={pending} onClick={onConfirm}
                sx={{ ...btn, bgcolor: 'success.main', color: '#fff', border: 'none', '&:hover': { bgcolor: 'success.dark' } }}>
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>✓</Box>Confirm
            </Box>
            <Box component="button" type="button" disabled={pending} onClick={onSendBack}
                sx={{ ...btn, bgcolor: 'background.paper', color: 'warning.dark', border: '1px solid', borderColor: 'warning.main', '&:hover': { bgcolor: softBg('warning') } }}>
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>↩</Box>Send back
            </Box>
        </>
    )
}
```

- [ ] **Step 6: The card's notes.** Directly after the Overdue banner in `TaskCard`, add two banners in the same style:

```tsx
            {isAwaitingConfirmation(task) && !task.canConfirm && (
                <Box sx={{
                    p: '8px 18px', borderBottom: '1px solid', borderBottomColor: 'divider',
                    bgcolor: softBg('warning'), borderLeft: '3px solid', borderLeftColor: 'warning.main',
                    fontSize: 11, color: 'warning.dark', fontWeight: 600,
                }}>
                    Waiting for {task.createdByName} to confirm
                </Box>
            )}
            {isOpenTask(task) && !isAwaitingConfirmation(task) && task.sentBackReason && (
                <Box sx={{
                    p: '8px 18px', borderBottom: '1px solid', borderBottomColor: 'divider',
                    bgcolor: softBg('error'), borderLeft: '3px solid', borderLeftColor: 'error.main',
                    fontSize: 11, color: 'error.dark', fontWeight: 600, whiteSpace: 'pre-wrap',
                }}>
                    Sent back: {task.sentBackReason}
                </Box>
            )}
```

On a waiting task the reviewer sees "Last sent back" context through the details dialog; keep the card to these two notes.

- [ ] **Step 7: Run — expect PASS:** `node_modules/.bin/vitest run src/components/tasks`, then `npm run lint` from `client/`.

- [ ] **Step 8: Commit**

```bash
git add client/src/components/tasks
git commit -m "Let a reviewer confirm or send back a task from its card"
```

---

### Task 7: The bell lists tasks waiting for the caller

**Files:**
- Modify: `client/src/components/layout/Topbar.tsx`
- Test: `client/src/components/layout/Topbar.test.tsx`

**Interfaces:**
- Consumes: `getWorkTasks()` (already in `lib/api`), `WorkTask.canConfirm`, `uiStore.navigateToTasks()`.
- Produces: localStorage key prefix `worktrack:manager-task-confirm-read:` (follow the naming of the existing `managerTsReadPrefix` constant — check its exact spelling at the top of the file and mirror it); read key `taskConfirmReadKey(id, updatedAtUtc) = \`${id}:${updatedAtUtc}\``.

- [ ] **Step 1: Write the failing test** — in `Topbar.test.tsx`: add `navigateToTasks: vi.fn()` to the mocked `uiStore` (lift it to a `const navigateToTasks = vi.fn()` like `navigateToAdminSection`), add `api.getWorkTasks.mockResolvedValue([])` to `beforeEach`, add a `MANAGER` user (`{ ...EMPLOYEE, id: 'u-mgr', roles: ['Manager'] }`), and:

```tsx
    it("lists a Manager's tasks waiting for their confirmation, and opens Tasks", async () => {
        api.getWorkTasks.mockResolvedValue([
            { id: 5, title: 'Chase notes', status: 'AwaitingConfirmation', canConfirm: true, updatedAtUtc: recent, assignees: [{ userId: 'u-emp', displayName: 'Maria Georgiou' }] },
            { id: 6, title: 'Not mine to confirm', status: 'AwaitingConfirmation', canConfirm: false, updatedAtUtc: recent, assignees: [] },
        ] as never)
        renderTopbarAs(MANAGER)

        fireEvent.click(await screen.findByRole('button', { name: /notifications/i }))
        const item = await screen.findByText('Chase notes is waiting for your confirmation')
        expect(screen.queryByText(/Not mine to confirm/)).not.toBeInTheDocument()

        fireEvent.click(item)
        expect(navigateToTasks).toHaveBeenCalled()
    })

    it('does not fetch tasks for an Employee or a System Administrator', async () => {
        renderTopbarAs(EMPLOYEE)
        await screen.findByRole('button', { name: /notifications/i })
        expect(api.getWorkTasks).not.toHaveBeenCalled()
    })
```

Match the bell button's accessible name to how the existing tests open it.

- [ ] **Step 2: Run — expect FAIL:** `node_modules/.bin/vitest run src/components/layout/Topbar.test.tsx`

- [ ] **Step 3: Implement** in `Topbar.tsx`, following the timesheet items exactly:
  - Import `getWorkTasks` alongside the other API calls, and add a key `const managerTaskKey = \`${managerTaskReadPrefix}${authStore.user?.id ?? ''}\`` with `const managerTaskReadPrefix = 'worktrack:manager-task-confirm-read:'` (mirror the neighbouring prefixes' style).
  - Query, under the same key the Tasks page uses so SignalR invalidation and the page's mutations refresh it:

```tsx
    const { data: workTasks, isLoading: isLoadingTasks } = useQuery({
        queryKey: ['work-tasks'],
        queryFn: getWorkTasks,
        enabled: authStore.isAuthenticated && shouldUseManagerNotifications,
        refetchInterval: authStore.isAuthenticated && shouldUseManagerNotifications ? notificationRefreshMs : false,
        refetchIntervalInBackground: true,
    })
```

  - Items, read state and pruning:

```tsx
    // Keyed by id and last update, so a task sent back and marked done again is news again.
    const taskConfirmReadKey = (t: { id: number; updatedAtUtc: string }) => `${t.id}:${t.updatedAtUtc}`
    const managerTasksToConfirm = (workTasks ?? [])
        .filter((t) => t.canConfirm === true)
        .sort((a, b) => tsTime(b.updatedAtUtc) - tsTime(a.updatedAtUtc))
    const [readManagerTaskKeys, setReadManagerTaskKeys] = useState<string[]>(() => getStoredIds(managerTaskKey))
    const readManagerTaskSet = useMemo(() => new Set(readManagerTaskKeys), [readManagerTaskKeys])
    const unreadManagerTasks = managerTasksToConfirm.filter((t) => !readManagerTaskSet.has(taskConfirmReadKey(t)))
    const managerTaskNotifications = unreadManagerTasks.slice(0, 6)
```

  Add `unreadManagerTasks.length` to the manager branch of `unreadCount`, `isLoadingTasks` to the manager branch of `isLoading`, `managerTaskNotifications.length === 0` to the manager "No notifications yet" condition, a `useEffect(() => { setReadManagerTaskKeys(getStoredIds(managerTaskKey)) }, [managerTaskKey])`, and a pruning effect mirroring the timesheet one (keep only keys still present in `managerTasksToConfirm`).

  - Click handler and menu items (after the timesheet items):

```tsx
    const handleManagerTaskClick = (key: string) => {
        const updated = Array.from(new Set([...readManagerTaskKeys, key]))
        setReadManagerTaskKeys(updated)
        window.localStorage.setItem(managerTaskKey, JSON.stringify(updated))
        setAnchorEl(null)
        uiStore.navigateToTasks()
    }
```

```tsx
                {!isLoading && shouldUseManagerNotifications && managerTaskNotifications.map((task) => (
                    <MenuItem key={`task-${task.id}`} onClick={() => handleManagerTaskClick(taskConfirmReadKey(task))}>
                        <ListItemIcon>
                            <CircleRoundedIcon sx={{ fontSize: 10, color: 'warning.main' }} />
                        </ListItemIcon>
                        <ListItemText
                            primary={`${task.title} is waiting for your confirmation`}
                            secondary={`Marked done ${formatChangedAt(task.updatedAtUtc)}`}
                            slotProps={{ primary: { sx: { whiteSpace: 'normal', wordBreak: 'break-word' } } }}
                        />
                    </MenuItem>
                ))}
```

  Update the "Three bells" comment to say the manager's and HR's bell also lists tasks waiting for their confirmation.

- [ ] **Step 4: Run — expect PASS:** `node_modules/.bin/vitest run src/components/layout`, then `npm run lint`.

- [ ] **Step 5: Commit**

```bash
git add client/src/components/layout/Topbar.tsx client/src/components/layout/Topbar.test.tsx
git commit -m "List tasks waiting for confirmation in the Manager and HR bell"
```

---

### Task 8: Document it and verify the whole branch

**Files:**
- Modify: `CLAUDE.md` (the **Tasks** bullet under Authorization, and the `WorkTask` row of the Domain Model table)

- [ ] **Step 1: CLAUDE.md.** In the **Tasks** bullet, after the sentence about who moves status, add:

> **Done on a task somebody else handed you is a request, not the end** (`WorkTaskReviewRule`, migration `AddWorkTaskConfirmation`): an assignee's Done moves it to `AwaitingConfirmation` (appended as 4), and a reviewer — the creator, or a Manager or HR Administrator in scope who is **not on the task** — Confirms it (Done, `ConfirmedById`, `CompletedAtUtc`) or Sends it back to In Progress with a required reason (`SentBackReason`/`SentBackAtUtc`, 500 chars, kept until confirmed). The client always asks for Done and the server decides; asking for `AwaitingConfirmation` is refused (`StageIsDerivedMessage`), a repeat Done from the assignee is a no-op, and the assignee's only other move while it waits is Withdraw (back to In Progress). A task whose only assignee is its creator, or with nobody left who could review it, closes directly. `WorkTaskReviewNotification` emails the creator (or, with the creator deactivated, the covering reviewers) when a task starts waiting, and the assignees when it is sent back. The status counts as open for timesheet logging and as not working for the idle panel. On the client: `canConfirm` puts Confirm / Send back (`SendBackDialog`) on the card, a "To Confirm" tile for Managers and HR, and the Manager/HR bell lists the waiting tasks (read state keyed by id + `updatedAtUtc`). `WorkTaskReviewTests` pins the server.

In the `WorkTask` table row, change `Status` (ToDo/InProgress/Done/Cancelled) to `Status` (ToDo/InProgress/Done/Cancelled/AwaitingConfirmation), and add `ConfirmedById` to the list of Restrict FKs `DeleteAdminUser` unpicks.

- [ ] **Step 2: Full server test run:** `dotnet test Tests/WorkTrack.Tests` — expect all green; report any failure verbatim.

- [ ] **Step 3: Client checks, per directory:** from `client/`, `npm run lint`, `npx tsc -b`, then `node_modules/.bin/vitest run src/lib`, `… src/components/tasks`, `… src/components/layout`, `… src/components/timesheets` (the timesheet Task picker reads task status).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "Document task confirmation"
```
