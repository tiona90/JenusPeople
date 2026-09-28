# Timesheet Entries Logged Against Tasks — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A timesheet row can name one of the owner's tasks. The task card then shows how much of its target has been logged and how much is left.

**Architecture:** A nullable `TimesheetEntry.WorkTaskId` FK (SetNull), checked by a new `TimesheetEntryTaskRule`. It is called from the two write actions of `TimesheetEntriesController`, and only when the task or the project changes. `WorkTaskDto.LoggedHours` is summed in memory from the linked entries on every read (SQLite cannot aggregate `decimal`, the same reason `GetProjectList` sums in memory). A new `GET /api/worktasks/timesheet-options` feeds the Task picker in `NewTimesheetPage` and the titles in `TimesheetDailyBreakdown`.

**Tech Stack:** ASP.NET Core 10, EF Core (SQL Server; SQLite/in-memory in tests), MediatR, xUnit; React 19 + TypeScript, MUI 7, React Query, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-28-timesheet-task-link-design.md`

**Deviation from the spec, found while planning:** `GenerateDraft` rebuilds a draft from **attendance** (one entry per day on a fixed project). It never copies entries, so it has no task to carry and needs no change. Only the client's "Copy to rest of week" carries tasks.

## Global Constraints

- Every saved entry counts toward `LoggedHours`, in any timesheet status. Nothing is stored; it is summed on every read.
- Over target never blocks a save.
- The rule checks against the **timesheet owner** (`Timesheet.EmployeeProfileId` → `EmployeeProfile.UserId`), never the caller.
- An entry whose task **and** project are both unchanged is never re-checked.
- Messages, verbatim: `"That task is not one of yours."`, `"That task is closed."`, `"That task belongs to another project."`
- A task that doesn't exist gets the "not one of yours" message, the same as a task the owner isn't on.
- Open = `WorkTaskStatus.ToDo` or `WorkTaskStatus.InProgress`.
- FK: `TimesheetEntry.WorkTaskId` → `WorkTask.Id`, `DeleteBehavior.SetNull`. Migration name: `LinkTimesheetEntriesToTasks`.
- Endpoint: `GET /api/worktasks/timesheet-options?timesheetId={id}`, with `timesheetId` optional. Without it, the owner is the caller (a week with no sheet yet).
- Client query key: `['work-tasks', 'timesheet-options', timesheetId ?? 'mine']`. Saving the sheet invalidates `['work-tasks']`.
- Wording: `describeTaskProgress(target, logged)` → `"8h logged · 16h left"`, `"4h over"`, `"8h logged"` (no target), `"nothing logged"` (no target, nothing logged). Hours are formatted with at most one decimal and no trailing `.0`.
- EF collections used with `Contains` must be typed `List<int>`/`IReadOnlyList<int>`, never an array (C# 14 span overload breaks EF — see memory).
- Test commands: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~<Name>"`. If the running API locks the build output, add `--artifacts-path C:\Users\user\AppData\Local\Temp\wt-artifacts`. Client: `cd client && node_modules/.bin/vitest run <path>`, never bare `npx vitest`.

## Review Focus

1. **A week with no timesheet yet.** The picker must still list the caller's tasks (no `timesheetId`). Pinned in Task 4.
2. **A row whose task was later closed, or whose owner was taken off it, is re-saved with only its hours changed.** It must save. Pinned in Task 2.
3. **An HR Administrator edits an employee's sheet.** The task is judged against the employee's assignments, not HR's (HR is never an assignee). Pinned in Task 2.
4. **Half hours.** 1.5h logged against a 24h target reads `"1.5h logged · 22.5h left"`, not `"1.50h"` or `"22.50h"`. Pinned in Task 5.
5. **The row's project changes while the task stays.** The server refuses it with "belongs to another project", and the client clears the task. Pinned in Tasks 2 and 7.

---

### Task 1: `WorkTaskId` on `TimesheetEntry`, FK and migration

**Files:**
- Modify: `Domain/TimesheetEntry.cs`
- Modify: `Domain/WorkTask.cs` (add the `TimesheetEntries` collection; rewrite the `TargetHours` comment)
- Modify: `Persistence/AppDbContext.cs:140-193` (the `Entity<TimesheetEntry>` block)
- Create: `Persistence/Migrations/<timestamp>_LinkTimesheetEntriesToTasks.cs` (generated)
- Test: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskTimesheetLinkTests.cs`

**Interfaces:**
- Produces: `TimesheetEntry.WorkTaskId : int?`, `TimesheetEntry.WorkTask : WorkTask?`, `WorkTask.TimesheetEntries : ICollection<TimesheetEntry>`

- [ ] **Step 1: Write the failing test**

```csharp
using Domain;
using Microsoft.EntityFrameworkCore;
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
    internal static async Task<Timesheet> AddSheetAsync(Persistence.AppDbContext db, string userId)
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
```

(Later tasks reuse this helper through `WorkTaskTimesheetLinkTests.AddSheetAsync`.)

- [ ] **Step 2: Run the test and confirm it fails**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskTimesheetLinkTests"`
Expected: build error, `'TimesheetEntry' does not contain a definition for 'WorkTaskId'`.

- [ ] **Step 3: Add the property, navigation and config**

In `Domain/TimesheetEntry.cs`, after `ProjectComponent`:

```csharp
    // Which of the owner's tasks the hours went on. Optional, and nullable for
    // every row predating the link. Counted towards WorkTask.TargetHours on every
    // read; set to null (the hours stay) when the task is deleted.
    public int? WorkTaskId { get; set; }
    public WorkTask? WorkTask { get; set; }
```

In `Domain/WorkTask.cs`, replace the `TargetHours` summary with:

```csharp
    /// <summary>
    /// The plan: how many hours of work it should take. Optional. Measured against
    /// the timesheet entries linked to the task (<see cref="TimesheetEntries"/>),
    /// summed on every read and never stored. Going over is allowed; the card says so.
    /// </summary>
```

and add, after `CompletedAtUtc`:

```csharp
    /// <summary>Timesheet rows logged against this task, in any timesheet status.</summary>
    public ICollection<TimesheetEntry> TimesheetEntries { get; set; } = new List<TimesheetEntry>();
```

In `Persistence/AppDbContext.cs`, inside `Entity<TimesheetEntry>` after the `ProjectComponent` relationship:

```csharp
            // SetNull, unlike the catalogue links above: a task is a to-do, not a
            // classification, and deleting one must not delete or block the hours
            // logged against it. WorkTask's own foreign keys are all Restrict, so
            // this adds no second cascade path.
            entity.HasOne(e => e.WorkTask)
                .WithMany(t => t.TimesheetEntries)
                .HasForeignKey(e => e.WorkTaskId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.WorkTaskId);
```

- [ ] **Step 4: Generate the migration**

Run: `dotnet ef migrations add LinkTimesheetEntriesToTasks --project Persistence --startup-project API`. If the API is running and locks `bin/Debug`, run `dotnet build API -c Migrate` first, then `dotnet ef migrations add LinkTimesheetEntriesToTasks --project Persistence --startup-project API --configuration Migrate --no-build`.
Expected: the migration adds the `WorkTaskId` int NULL column, `IX_TimesheetEntries_WorkTaskId`, and an FK with `onDelete: ReferentialAction.SetNull`. Read the file; it must add nothing else.

- [ ] **Step 5: Run the test and confirm it passes**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskTimesheetLinkTests|FullyQualifiedName~WorkTaskDeleteCleanupTests"`
Expected: PASS. `WorkTaskDeleteCleanupTests` covers a leaver's tasks being deleted, which now runs through the new FK.

- [ ] **Step 6: Commit**

```bash
git add Domain/TimesheetEntry.cs Domain/WorkTask.cs Persistence/AppDbContext.cs Persistence/Migrations Tests/WorkTrack.Tests/WorkTasks/WorkTaskTimesheetLinkTests.cs
git commit -m "Link timesheet entries to tasks"
```

---

### Task 2: `TimesheetEntryTaskRule` and wiring it into the entry endpoints

**Files:**
- Create: `Application/Timesheets/Support/TimesheetEntryTaskRule.cs`
- Modify: `API/Controllers/TimesheetEntriesController.cs` (`CreateEntryRequest`, `AddEntry`, `UpdateEntry`)
- Test: `Tests/WorkTrack.Tests/TimesheetEntryTaskRuleTests.cs`

**Interfaces:**
- Consumes: `TimesheetEntry.WorkTaskId` (Task 1)
- Produces: `TimesheetEntryTaskRule.CheckAsync(AppDbContext context, string timesheetId, TimesheetEntry candidate, TimesheetEntry? stored, CancellationToken ct) : Task<string?>` (null = fine), plus the constants `NotYoursMessage`, `ClosedMessage`, `OtherProjectMessage`; `CreateEntryRequest.WorkTaskId : int?`

- [ ] **Step 1: Write the failing tests**

This test goes through the controller, the way `TimesheetEntryComponentScopeTests` does. It uses `TestDb` (in-memory) because it asserts no constraint.

```csharp
using System.Security.Claims;
using API.Controllers;
using Application.Timesheets.Support;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A row may name one of its owner's open tasks on the same project. The rule is
/// asked only when the task or the project changes, so closing a task or taking
/// someone off it never strands the hours already logged.
/// </summary>
public class TimesheetEntryTaskRuleTests
{
    private const string OwnerUserId = "owner-u";
    private const string OtherUserId = "other-u";
    private const string HrUserId = "hr-u";
    private const string TimesheetId = "ts-1";
    private const int ProjectA = 1;
    private const int ProjectB = 2;
    private const int OpenTask = 100;
    private const int DoneTask = 101;
    private const int SomeoneElsesTask = 102;
    private const int OtherProjectTask = 103;
    private static readonly DateTime Day = new(2024, 1, 2);

    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = ProjectA, Name = "Apollo", Code = "APL" });
        db.Projects.Add(new Project { Id = ProjectB, Name = "Borealis", Code = "BOR" });
        db.Roles.Add(new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() });
        foreach (var id in new[] { OwnerUserId, OtherUserId, HrUserId })
            db.Users.Add(new User { Id = id, UserName = id, Email = $"{id}@t" });
        db.UserRoles.Add(new UserRole { UserId = HrUserId, RoleId = "r-hr" });
        db.UserDepartments.Add(new UserDepartment { UserId = HrUserId, DepartmentId = 1 });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "owner-p", UserId = OwnerUserId, DepartmentId = 1 });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "hr-p", UserId = HrUserId, DepartmentId = null });

        WorkTask Task(int id, int projectId, string assignee, WorkTaskStatus status = WorkTaskStatus.InProgress) => new()
        {
            Id = id, Title = $"t{id}", DepartmentId = 1, ProjectId = projectId, CreatedById = OtherUserId,
            Status = status, Assignees = [new WorkTaskAssignee { UserId = assignee }],
        };
        db.WorkTasks.AddRange(
            Task(OpenTask, ProjectA, OwnerUserId),
            Task(DoneTask, ProjectA, OwnerUserId, WorkTaskStatus.Done),
            Task(SomeoneElsesTask, ProjectA, OtherUserId),
            Task(OtherProjectTask, ProjectB, OwnerUserId));

        db.Timesheets.Add(new Timesheet
        {
            Id = TimesheetId, EmployeeProfileId = "owner-p", DepartmentId = 1,
            PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7), Status = TimesheetStatus.Draft,
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static TimesheetEntriesController ControllerFor(AppDbContext db, string userId = OwnerUserId, string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new TimesheetEntriesController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    private static CreateEntryRequest Entry(int? taskId, int projectId = ProjectA) => new()
    {
        ProjectId = projectId, Date = Day, HoursWorked = 4m, WorkTaskId = taskId,
    };

    private static async Task<string> RefusalAsync(AppDbContext db, CreateEntryRequest request, string userId = OwnerUserId, string? role = null) =>
        (await Assert.ThrowsAsync<ArgumentException>(() =>
            ControllerFor(db, userId, role).AddEntry(TimesheetId, request, CancellationToken.None))).Message;

    [Fact]
    public async Task An_open_task_the_owner_is_on_is_accepted_and_stored()
    {
        using var db = SeedWorld();
        await ControllerFor(db).AddEntry(TimesheetId, Entry(OpenTask), CancellationToken.None);
        Assert.Equal(OpenTask, (await db.TimesheetEntries.SingleAsync()).WorkTaskId);
    }

    [Fact]
    public async Task No_task_is_always_fine()
    {
        using var db = SeedWorld();
        await ControllerFor(db).AddEntry(TimesheetId, Entry(null), CancellationToken.None);
        Assert.Null((await db.TimesheetEntries.SingleAsync()).WorkTaskId);
    }

    [Fact]
    public async Task A_task_the_owner_is_not_on_is_refused() =>
        Assert.Equal(TimesheetEntryTaskRule.NotYoursMessage, await RefusalAsync(SeedWorld(), Entry(SomeoneElsesTask)));

    [Fact]
    public async Task A_task_that_does_not_exist_reads_the_same_as_not_yours() =>
        Assert.Equal(TimesheetEntryTaskRule.NotYoursMessage, await RefusalAsync(SeedWorld(), Entry(9999)));

    [Fact]
    public async Task A_closed_task_is_refused() =>
        Assert.Equal(TimesheetEntryTaskRule.ClosedMessage, await RefusalAsync(SeedWorld(), Entry(DoneTask)));

    [Fact]
    public async Task A_task_on_another_project_is_refused() =>
        Assert.Equal(TimesheetEntryTaskRule.OtherProjectMessage, await RefusalAsync(SeedWorld(), Entry(OtherProjectTask, ProjectA)));

    [Fact]
    public async Task HR_writing_on_behalf_is_judged_against_the_owner()
    {
        using var db = SeedWorld();
        // HR is on no task at all; the owner's task is accepted all the same.
        await ControllerFor(db, HrUserId, AppRoles.HrAdministrator).AddEntry(TimesheetId, Entry(OpenTask), CancellationToken.None);
        Assert.Equal(OpenTask, (await db.TimesheetEntries.SingleAsync()).WorkTaskId);
    }

    [Fact]
    public async Task Keeping_a_since_closed_task_while_changing_the_hours_saves()
    {
        using var db = SeedWorld();
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            Id = "e-1", TimesheetId = TimesheetId, ProjectId = ProjectA, Date = Day, HoursWorked = 2m, WorkTaskId = DoneTask,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var edited = new TimesheetEntry
        {
            Id = "e-1", TimesheetId = TimesheetId, ProjectId = ProjectA, Date = Day, HoursWorked = 3m, WorkTaskId = DoneTask,
        };
        var result = await ControllerFor(db).UpdateEntry(TimesheetId, "e-1", edited, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(3m, (await db.TimesheetEntries.AsNoTracking().SingleAsync()).HoursWorked);
    }

    [Fact]
    public async Task Moving_the_row_to_another_project_under_the_same_task_is_refused()
    {
        using var db = SeedWorld();
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            Id = "e-1", TimesheetId = TimesheetId, ProjectId = ProjectA, Date = Day, HoursWorked = 2m, WorkTaskId = OpenTask,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var moved = new TimesheetEntry
        {
            Id = "e-1", TimesheetId = TimesheetId, ProjectId = ProjectB, Date = Day, HoursWorked = 2m, WorkTaskId = OpenTask,
        };
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            ControllerFor(db).UpdateEntry(TimesheetId, "e-1", moved, CancellationToken.None));
        Assert.Equal(TimesheetEntryTaskRule.OtherProjectMessage, error.Message);
    }
}
```

The HR case depends on `TimesheetAccess.AuthorizeWriteAsync` admitting HR through the `UserDepartment` row. If that access check needs anything else seeded (it resolves scope through `ManagerAccessScopeResolver`), copy the HR seeding from `TimesheetEntryOwnershipTests`. The test is about the task rule, not access.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~TimesheetEntryTaskRuleTests"`
Expected: build error, no `WorkTaskId` on `CreateEntryRequest` / no `TimesheetEntryTaskRule`.

- [ ] **Step 3: Write the rule**

`Application/Timesheets/Support/TimesheetEntryTaskRule.cs`:

```csharp
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Support;

/// <summary>
/// Which task a timesheet row may be logged against: one its <b>owner</b> is
/// assigned to (not the caller — an HR Administrator writes on somebody's behalf
/// and is never an assignee), still open, on the row's own project.
///
/// Asked only when the row's task or project changes. A row that keeps both is
/// never re-checked, so closing a task or taking somebody off it leaves the hours
/// already logged where they are — the same "check what changed" shape as
/// WorkTaskAssigneeRule.
/// </summary>
public static class TimesheetEntryTaskRule
{
    /// <summary>Also the answer for a task that does not exist, so an id cannot be probed.</summary>
    public const string NotYoursMessage = "That task is not one of yours.";
    public const string ClosedMessage = "That task is closed.";
    public const string OtherProjectMessage = "That task belongs to another project.";

    /// <param name="stored">The row as saved, or null when <paramref name="candidate"/> is new.</param>
    /// <returns>Null when the row may be saved, otherwise the refusal.</returns>
    public static async Task<string?> CheckAsync(
        AppDbContext context,
        string timesheetId,
        TimesheetEntry candidate,
        TimesheetEntry? stored,
        CancellationToken cancellationToken)
    {
        if (candidate.WorkTaskId is not { } taskId) return null;
        if (stored is not null && stored.WorkTaskId == taskId && stored.ProjectId == candidate.ProjectId) return null;

        var ownerUserId = await context.Timesheets
            .Where(t => t.Id == timesheetId)
            .Join(context.EmployeeProfiles.IgnoreQueryFilters(), t => t.EmployeeProfileId, p => p.Id, (t, p) => p.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        var task = await context.WorkTasks
            .AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.Status, t.ProjectId, Assigned = t.Assignees.Any(a => a.UserId == ownerUserId) })
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null || !task.Assigned) return NotYoursMessage;
        if (task.Status is not (WorkTaskStatus.ToDo or WorkTaskStatus.InProgress)) return ClosedMessage;
        if (task.ProjectId != candidate.ProjectId) return OtherProjectMessage;
        return null;
    }
}
```

- [ ] **Step 4: Wire it into the controller**

In `CreateEntryRequest` add:

```csharp
    /// <summary>One of the owner's open tasks on this project, or null — see TimesheetEntryTaskRule.</summary>
    public int? WorkTaskId { get; set; }
```

In `AddEntry`, add `WorkTaskId = request.WorkTaskId,` to the `new TimesheetEntry { … }` initialiser. Right after the `if (!validation.IsValid) throw …` line, add:

```csharp
        var taskRefusal = await TimesheetEntryTaskRule.CheckAsync(_context, timesheetId, entry, stored: null, cancellationToken);
        if (taskRefusal is not null)
            throw new ArgumentException(taskRefusal);
```

In `UpdateEntry`, after its `if (!validation.IsValid) throw …` line, add:

```csharp
        var taskRefusal = await TimesheetEntryTaskRule.CheckAsync(
            _context, timesheetId, entry, existing.Single(e => e.Id == entryId), cancellationToken);
        if (taskRefusal is not null)
            throw new ArgumentException(taskRefusal);
```

(`UpdateEntry` binds the whole `TimesheetEntry`, so `WorkTaskId` reaches the save without further changes. A client that leaves it out clears the link, which is correct for a full replace.)

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~TimesheetEntry"`
Expected: PASS, including the existing `TimesheetEntry*ScopeTests` and `TimesheetEntryOwnershipTests`.

- [ ] **Step 6: Commit**

```bash
git add Application/Timesheets/Support/TimesheetEntryTaskRule.cs API/Controllers/TimesheetEntriesController.cs Tests/WorkTrack.Tests/TimesheetEntryTaskRuleTests.cs
git commit -m "Let a timesheet row name one of its owner's open tasks"
```

---

### Task 3: `WorkTaskDto.LoggedHours`

**Files:**
- Modify: `Application/WorkTasks/DTOs/WorkTaskDto.cs`
- Modify: `Application/WorkTasks/Support/WorkTaskProjection.cs`
- Modify: `Application/WorkTasks/Queries/GetWorkTaskList.cs`
- Modify: every caller of `WorkTaskProjection.LoadDtoAsync` (`CreateWorkTask`, `UpdateWorkTask`, `UpdateWorkTaskStatus`; confirm with `grep -rn LoadDtoAsync Application`)
- Test: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskLoggedHoursTests.cs`

**Interfaces:**
- Consumes: `TimesheetEntry.WorkTaskId` (Task 1), `WorkTaskTimesheetLinkTests.AddSheetAsync` (Task 1)
- Produces: `WorkTaskDto.LoggedHours : decimal`; `WorkTaskProjection.LoggedHoursAsync(AppDbContext context, IReadOnlyList<int> taskIds, CancellationToken ct) : Task<Dictionary<int, decimal>>`

- [ ] **Step 1: Write the failing test**

```csharp
using Application.WorkTasks.Queries;
using Domain;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

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
```

`AddSheetAsync` builds its sheet with `DepartmentId = Sales` and period 21–27 Sep. Each call needs a different user, because of the one-sheet-per-employee-per-period unique index. That's why the second sheet belongs to `Employee`.

- [ ] **Step 2: Run the test and confirm it fails**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskLoggedHoursTests"`
Expected: build error, no `LoggedHours`.

- [ ] **Step 3: Implement**

`WorkTaskDto`, after `TargetHours`:

```csharp
    /// <summary>
    /// Hours on every timesheet row linked to the task, whatever the sheet's status.
    /// Summed on every read, never stored. May pass <see cref="TargetHours"/>.
    /// </summary>
    public decimal LoggedHours { get; set; }
```

`WorkTaskProjection`, new members:

```csharp
    /// <summary>
    /// Logged hours per task. Summed in memory, as GetProjectList does, because
    /// SQLite (the tests' provider) cannot aggregate a decimal column.
    /// </summary>
    public static async Task<Dictionary<int, decimal>> LoggedHoursAsync(
        AppDbContext context, IReadOnlyList<int> taskIds, CancellationToken cancellationToken)
    {
        var rows = await context.TimesheetEntries
            .AsNoTracking()
            .Where(e => e.WorkTaskId != null && taskIds.Contains(e.WorkTaskId.Value))
            .Select(e => new { TaskId = e.WorkTaskId!.Value, e.HoursWorked })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.Sum(r => r.HoursWorked));
    }

    public static async Task<List<WorkTaskDto>> WithLoggedHoursAsync(
        AppDbContext context, List<WorkTaskDto> tasks, CancellationToken cancellationToken)
    {
        var logged = await LoggedHoursAsync(context, tasks.Select(t => t.Id).ToList(), cancellationToken);
        foreach (var task in tasks) task.LoggedHours = logged.GetValueOrDefault(task.Id);
        return tasks;
    }
```

Change `LoadDtoAsync` to fill it:

```csharp
    public static async Task<WorkTaskDto> LoadDtoAsync(
        AppDbContext context, int id, string callerUserId, CancellationToken cancellationToken, bool callerManages = true)
    {
        var dto = await Project(context.WorkTasks.AsNoTracking().Where(t => t.Id == id), callerUserId, callerManages)
            .SingleAsync(cancellationToken);
        return (await WithLoggedHoursAsync(context, [dto], cancellationToken))[0];
    }
```

(Its signature is unchanged, so the command handlers need no edit. Check each one still compiles and still `await`s it.)

In `GetWorkTaskList.Handler`, replace the return with:

```csharp
            await WorkTaskProjection.WithLoggedHoursAsync(context, tasks, cancellationToken);
            return Result<List<WorkTaskDto>>.Success(WorkTaskProjection.Sort(tasks));
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTrack.Tests.WorkTasks"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Application/WorkTasks Tests/WorkTrack.Tests/WorkTasks/WorkTaskLoggedHoursTests.cs
git commit -m "Report the hours logged against each task"
```

---

### Task 4: `GetTimesheetTaskOptions` and `GET /api/worktasks/timesheet-options`

**Files:**
- Create: `Application/WorkTasks/Queries/GetTimesheetTaskOptions.cs`
- Modify: `Application/WorkTasks/DTOs/WorkTaskDto.cs` (add `TimesheetTaskOptionDto`)
- Modify: `API/Controllers/WorkTasksController.cs`
- Test: `Tests/WorkTrack.Tests/WorkTasks/GetTimesheetTaskOptionsTests.cs`
- Modify: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskSurfaceTests.cs` (only if it enumerates every action; if so, add this one under the class gate with no extra action gate)

**Interfaces:**
- Consumes: `WorkTaskProjection.LoggedHoursAsync` (Task 3), `TimesheetScope.ApplyAsync(context, query, userId, isAdmin, isManager, ct, isHrAdministrator:)`
- Produces: `TimesheetTaskOptionDto { int Id; string Title; int? ProjectId; string? ProjectCode; int? TargetHours; decimal LoggedHours; bool IsClosed }`; `GetTimesheetTaskOptions.Query { string CallerUserId; string? TimesheetId; bool IsAdmin; bool IsManager; bool IsHrAdministrator }` → `Result<List<TimesheetTaskOptionDto>>`

- [ ] **Step 1: Write the failing tests**

```csharp
using Application.WorkTasks.Queries;
using Domain;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

public class GetTimesheetTaskOptionsTests
{
    private static Task<Application.Core.Result<List<Application.WorkTasks.DTOs.TimesheetTaskOptionDto>>> Run(
        Persistence.AppDbContext db, string caller, string? timesheetId = null, bool isManager = false, bool isHr = false) =>
        new GetTimesheetTaskOptions.Handler(db).Handle(new GetTimesheetTaskOptions.Query
        {
            CallerUserId = caller, TimesheetId = timesheetId, IsManager = isManager || isHr, IsHrAdministrator = isHr,
        }, CancellationToken.None);

    [Fact]
    public async Task Without_a_sheet_the_callers_own_open_tasks_are_offered()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.WorkTasks.AddRange(
            NewTask(Sales, Hr, Employee, "mine open"),
            NewTask(Sales, Hr, Employee, "mine done", WorkTaskStatus.Done),
            NewTask(Sales, Hr, SalesManager, "not mine"));
        await db.SaveChangesAsync();

        var result = await Run(db, Employee);

        var option = Assert.Single(result.Value!);
        Assert.Equal("mine open", option.Title);
        Assert.Equal(SalesProject, option.ProjectId);
        Assert.Equal("CRM", option.ProjectCode);
        Assert.False(option.IsClosed);
    }

    [Fact]
    public async Task A_closed_task_already_on_the_sheet_is_offered_and_flagged()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var done = NewTask(Sales, Hr, Employee, "done", WorkTaskStatus.Done);
        db.WorkTasks.Add(done);
        await db.SaveChangesAsync();
        var sheet = await WorkTaskTimesheetLinkTests.AddSheetAsync(db, Employee);
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            TimesheetId = sheet.Id, ProjectId = SalesProject, Date = new DateTime(2026, 9, 21), HoursWorked = 2m, WorkTaskId = done.Id,
        });
        await db.SaveChangesAsync();

        var result = await Run(db, Employee, sheet.Id);

        var option = Assert.Single(result.Value!);
        Assert.True(option.IsClosed);
        Assert.Equal(2m, option.LoggedHours);
    }

    [Fact]
    public async Task A_manager_reading_a_team_sheet_gets_the_owners_tasks()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.WorkTasks.AddRange(NewTask(Sales, Hr, Employee, "eve's"), NewTask(Sales, Hr, SalesManager, "sam's"));
        await db.SaveChangesAsync();
        var sheet = await WorkTaskTimesheetLinkTests.AddSheetAsync(db, Employee);

        var result = await Run(db, SalesManager, sheet.Id, isManager: true);

        Assert.Equal(["eve's"], result.Value!.Select(o => o.Title).ToList());
    }

    [Fact]
    public async Task A_sheet_outside_the_callers_scope_is_not_found()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var sheet = await WorkTaskTimesheetLinkTests.AddSheetAsync(db, Employee);

        var result = await Run(db, OpsManager, sheet.Id, isManager: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(Application.Core.ResultErrorKind.NotFound, result.ErrorKind);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~GetTimesheetTaskOptionsTests"`
Expected: build error, no `GetTimesheetTaskOptions`.

- [ ] **Step 3: Add the DTO**

Append to `Application/WorkTasks/DTOs/WorkTaskDto.cs`:

```csharp
/// <summary>One task a timesheet row may be logged against, for the editor's Task picker.</summary>
public class TimesheetTaskOptionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>The picker narrows by it; null only on a task predating required projects.</summary>
    public int? ProjectId { get; set; }
    public string? ProjectCode { get; set; }
    public int? TargetHours { get; set; }
    public decimal LoggedHours { get; set; }
    /// <summary>Done or cancelled: on the list only because a row on the sheet already names it.</summary>
    public bool IsClosed { get; set; }
}
```

- [ ] **Step 4: Write the query**

`Application/WorkTasks/Queries/GetTimesheetTaskOptions.cs`:

```csharp
using Application.Core;
using Application.Timesheets.Support;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Queries;

/// <summary>
/// The tasks a timesheet's rows may be logged against: every open task its owner
/// is assigned to, plus any task a row on the sheet already names (so an old row
/// never opens on a blank picker), the latter flagged closed when it is.
///
/// With no <see cref="Query.TimesheetId"/> the owner is the caller — a week with
/// no sheet yet. With one, the sheet must be inside the caller's timesheet scope,
/// or it is "not found", exactly as GetTimesheetDetail answers.
/// </summary>
public class GetTimesheetTaskOptions
{
    public class Query : IRequest<Result<List<TimesheetTaskOptionDto>>>
    {
        public string CallerUserId { get; set; } = string.Empty;
        public string? TimesheetId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsHrAdministrator { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<TimesheetTaskOptionDto>>>
    {
        public async Task<Result<List<TimesheetTaskOptionDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var ownerUserId = request.CallerUserId;
            List<int> onSheet = [];

            if (!string.IsNullOrEmpty(request.TimesheetId))
            {
                var scoped = await TimesheetScope.ApplyAsync(
                    context,
                    context.Timesheets.AsNoTracking().Where(t => t.Id == request.TimesheetId),
                    request.CallerUserId,
                    request.IsAdmin,
                    request.IsManager,
                    cancellationToken,
                    isHrAdministrator: request.IsHrAdministrator);

                var sheet = await scoped
                    .Join(context.EmployeeProfiles.IgnoreQueryFilters(), t => t.EmployeeProfileId, p => p.Id,
                        (t, p) => new { t.Id, p.UserId })
                    .FirstOrDefaultAsync(cancellationToken);
                if (sheet is null) return Result<List<TimesheetTaskOptionDto>>.Failure("Timesheet not found.");

                ownerUserId = sheet.UserId;
                onSheet = await context.TimesheetEntries
                    .Where(e => e.TimesheetId == sheet.Id && e.WorkTaskId != null)
                    .Select(e => e.WorkTaskId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }

            var options = await context.WorkTasks
                .AsNoTracking()
                .Where(t => onSheet.Contains(t.Id)
                    || ((t.Status == WorkTaskStatus.ToDo || t.Status == WorkTaskStatus.InProgress)
                        && t.Assignees.Any(a => a.UserId == ownerUserId)))
                .OrderBy(t => t.Title)
                .Select(t => new TimesheetTaskOptionDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    ProjectId = t.ProjectId,
                    ProjectCode = t.Project != null ? t.Project.Code : null,
                    TargetHours = t.TargetHours,
                    IsClosed = t.Status == WorkTaskStatus.Done || t.Status == WorkTaskStatus.Cancelled,
                })
                .ToListAsync(cancellationToken);

            var logged = await WorkTaskProjection.LoggedHoursAsync(context, options.Select(o => o.Id).ToList(), cancellationToken);
            foreach (var option in options) option.LoggedHours = logged.GetValueOrDefault(option.Id);

            return Result<List<TimesheetTaskOptionDto>>.Success(options);
        }
    }
}
```

Before writing the controller action, check how `TimesheetsController` builds `GetTimesheetDetail.Query` (`IsAdmin`/`IsManager`), and pass the same flags the same way.

- [ ] **Step 5: Add the action**

In `WorkTasksController`, after `GetWorkTasks`. It gets no extra `[Authorize]`: the class gate already admits every role that files a timesheet.

```csharp
    /// <summary>
    /// The Task picker on a timesheet row: the sheet owner's open tasks. Without a
    /// timesheet id the owner is the caller (a week not yet saved).
    /// </summary>
    [HttpGet("timesheet-options")]
    public async Task<ActionResult<List<TimesheetTaskOptionDto>>> GetTimesheetOptions([FromQuery] string? timesheetId) =>
        HandleResult(await Mediator.Send(new GetTimesheetTaskOptions.Query
        {
            CallerUserId = CallerUserId,
            TimesheetId = timesheetId,
            IsAdmin = User.IsSystemAdministrator(),
            IsManager = User.IsDepartmentScoped(),
            IsHrAdministrator = User.IsHrAdministrator(),
        }));
```

(If `TimesheetsController` sets `IsAdmin`/`IsManager` differently for the detail query, copy its expressions exactly instead of the ones above.)

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTrack.Tests.WorkTasks"`
Expected: PASS, including `WorkTaskSurfaceTests`.

- [ ] **Step 7: Commit**

```bash
git add Application/WorkTasks API/Controllers/WorkTasksController.cs Tests/WorkTrack.Tests/WorkTasks
git commit -m "Offer a timesheet's owner their open tasks to log against"
```

---

### Task 5: Client types, API call and `describeTaskProgress`

**Files:**
- Modify: `client/src/lib/types/work-task.ts`
- Modify: `client/src/lib/types/timesheet-entry.ts`
- Modify: `client/src/lib/api/work-tasks.ts`
- Modify: `client/src/lib/work-tasks.ts`
- Test: `client/src/lib/work-tasks.test.ts`

**Interfaces:**
- Produces:
  - `WorkTask.loggedHours: number`
  - `TimesheetTaskOption { id: number; title: string; projectId: number | null; projectCode: string | null; targetHours: number | null; loggedHours: number; isClosed: boolean }`
  - `TimesheetEntry.workTaskId?: number | null`
  - `getTimesheetTaskOptions(timesheetId?: string): Promise<TimesheetTaskOption[]>`
  - `describeTaskProgress(targetHours: number | null, loggedHours: number): { text: string; over: boolean }`
  - `formatHours(hours: number): string`

- [ ] **Step 1: Write the failing tests**

Append to `client/src/lib/work-tasks.test.ts` (add `describeTaskProgress` to its existing import from `./work-tasks`):

```ts
describe('describeTaskProgress', () => {
    it('says what is logged and what is left', () => {
        expect(describeTaskProgress(24, 8)).toEqual({ text: '8h logged · 16h left', over: false })
    })
    it('keeps half hours to one decimal', () => {
        expect(describeTaskProgress(24, 1.5)).toEqual({ text: '1.5h logged · 22.5h left', over: false })
    })
    it('reads exactly on target as nothing left, not over', () => {
        expect(describeTaskProgress(24, 24)).toEqual({ text: '24h logged · 0h left', over: false })
    })
    it('says how far over', () => {
        expect(describeTaskProgress(24, 28)).toEqual({ text: '4h over', over: true })
    })
    it('has no remaining figure without a target', () => {
        expect(describeTaskProgress(null, 8)).toEqual({ text: '8h logged', over: false })
        expect(describeTaskProgress(null, 0)).toEqual({ text: 'nothing logged', over: false })
    })
    it('reads a missing figure from an older API as nothing logged', () => {
        expect(describeTaskProgress(24, undefined as unknown as number)).toEqual({ text: '0h logged · 24h left', over: false })
    })
})
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `cd client && node_modules/.bin/vitest run src/lib/work-tasks.test.ts`
Expected: FAIL, `describeTaskProgress is not a function` / not exported.

- [ ] **Step 3: Implement**

`client/src/lib/types/work-task.ts`: change the `targetHours` comment to `/** The plan. Measured against loggedHours; going over is allowed. */` and add below it:

```ts
    /** Hours on every timesheet row linked to the task, any sheet status. Missing from an older API. */
    loggedHours: number
```

Append:

```ts
/** A task a timesheet row may be logged against — the owner's open ones, plus any already on the sheet. */
export interface TimesheetTaskOption {
    id: number
    title: string
    projectId: number | null
    projectCode: string | null
    targetHours: number | null
    loggedHours: number
    /** Done or cancelled: listed only because a row on the sheet already names it. */
    isClosed: boolean
}
```

`client/src/lib/types/timesheet-entry.ts`, after `projectComponent`:

```ts
    // Which of the owner's tasks the hours went on; counted towards its target.
    // Null on every entry predating the link, and on rows logged against no task.
    workTaskId?: number | null;
```

`client/src/lib/api/work-tasks.ts`: add `TimesheetTaskOption` to the type import and append:

```ts
// The Task picker on a timesheet row. Without an id: the caller's own (a week not yet saved).
export async function getTimesheetTaskOptions(timesheetId?: string) {
    const response = await apiClient.get<TimesheetTaskOption[]>('/worktasks/timesheet-options', {
        params: timesheetId ? { timesheetId } : undefined,
    })
    return response.data
}
```

`client/src/lib/work-tasks.ts`, append:

```ts
/** `8`, `1.5`, `22.5` — one decimal at most, no trailing `.0`. */
export function formatHours(hours: number): string {
    return `${Math.round(hours * 10) / 10}`
}

/**
 * How far through its target a task is, in the words the card and the timesheet
 * picker share. Over target is reported, never refused. A missing figure (an API
 * predating the field) reads as nothing logged.
 */
export function describeTaskProgress(targetHours: number | null, loggedHours: number): { text: string; over: boolean } {
    const logged = Number(loggedHours) || 0
    if (targetHours == null) return { text: logged > 0 ? `${formatHours(logged)}h logged` : 'nothing logged', over: false }
    const left = targetHours - logged
    if (left < 0) return { text: `${formatHours(-left)}h over`, over: true }
    return { text: `${formatHours(logged)}h logged · ${formatHours(left)}h left`, over: false }
}
```

- [ ] **Step 4: Run the tests and type-check**

Run: `cd client && node_modules/.bin/vitest run src/lib/work-tasks.test.ts && node_modules/.bin/tsc -b --noEmit`
Expected: PASS. tsc may flag test fixtures that build a `WorkTask` literal without `loggedHours`: add `loggedHours: 0` to each flagged fixture (likely `TasksPage.test.tsx`, `TaskDialog.test.tsx`).

- [ ] **Step 5: Commit**

```bash
git add client/src/lib client/src/components/tasks/*.test.tsx
git commit -m "Client: task progress wording and the timesheet task options call"
```

---

### Task 6: The task card's Target tile shows logged and remaining

**Files:**
- Modify: `client/src/components/tasks/TasksPage.tsx:421-425`
- Test: `client/src/components/tasks/TasksPage.test.tsx`

**Interfaces:**
- Consumes: `describeTaskProgress` (Task 5); `CardStat`'s existing `valueColor` prop

- [ ] **Step 1: Write the failing test**

In `TasksPage.test.tsx`, find how existing tests render the page with a mocked `getWorkTasks` and a task fixture (reuse that fixture builder), then add:

```tsx
it('shows the hours logged and left against the target, and how far over', async () => {
    mockTasks([
        { ...taskFixture({ id: 1, title: 'on track' }), targetHours: 24, loggedHours: 8 },
        { ...taskFixture({ id: 2, title: 'overrun' }), targetHours: 10, loggedHours: 14 },
    ])
    renderPage()
    expect(await screen.findByText('8h logged · 16h left')).toBeInTheDocument()
    expect(screen.getByText('4h over')).toBeInTheDocument()
})
```

(`mockTasks`, `taskFixture` and `renderPage` stand for the helpers that file already has. Use their actual names; if the file builds tasks inline, build these two the same way.)

- [ ] **Step 2: Run the test and confirm it fails**

Run: `cd client && node_modules/.bin/vitest run src/components/tasks/TasksPage.test.tsx`
Expected: FAIL, unable to find text `8h logged · 16h left`.

- [ ] **Step 3: Implement**

Add `describeTaskProgress` to `TasksPage.tsx`'s import from `../../lib/work-tasks`, then replace the Target `CardStat`:

```tsx
                {(() => {
                    const progress = describeTaskProgress(task.targetHours, task.loggedHours)
                    return (
                        <CardStat
                            label="Target"
                            value={task.targetHours != null ? `${task.targetHours}h` : '—'}
                            sub={task.targetHours == null && progress.text === 'nothing logged' ? 'no target set' : progress.text}
                            valueColor={progress.over ? 'error.main' : undefined}
                        />
                    )
                })()}
```

If `CardStat`'s `sub` cannot take a colour, the red value plus "4h over" is enough. Don't widen `CardStat` for this.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `cd client && node_modules/.bin/vitest run src/components/tasks`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add client/src/components/tasks
git commit -m "Show logged and remaining hours on the task card"
```

---

### Task 7: Task column in the timesheet editor

**Files:**
- Create: `client/src/lib/timesheet-tasks.ts`
- Test: `client/src/lib/timesheet-tasks.test.ts`
- Modify: `client/src/components/timesheet/NewTimesheetPage.tsx` (row type `Task` at 96-105, `newTask` 131, `buildBucketsFromEntries` 170-190, the queries near 266, `copyToRestOfWeek` 363-392, `updateTask` 394-425, `handleSave` 500-545, `TASK_GRID`/`TASK_HEADERS` 61-69, `DayCard` props near 853 and 911, the grid JSX near 1085)

**Interfaces:**
- Consumes: `TimesheetTaskOption`, `getTimesheetTaskOptions`, `describeTaskProgress` (Task 5)
- Produces:
  - `taskOptionsForRow(options: TimesheetTaskOption[], projectId: string, currentTaskId: string): TimesheetTaskOption[]`
  - `retainedWorkTaskId(workTaskId: string, projectId: string, options: TimesheetTaskOption[]): string`
  - `copyableWorkTaskId(workTaskId: string, options: TimesheetTaskOption[]): string`
  - `taskOptionLabel(option: TimesheetTaskOption): string`

The editor's row type is already called `Task` (a timesheet line), and the new field names the *work* task. Keep the name `workTaskId` everywhere so the two are never confused.

- [ ] **Step 1: Write the failing tests**

`client/src/lib/timesheet-tasks.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { copyableWorkTaskId, retainedWorkTaskId, taskOptionLabel, taskOptionsForRow } from './timesheet-tasks'
import type { TimesheetTaskOption } from './types'

const opt = (o: Partial<TimesheetTaskOption>): TimesheetTaskOption => ({
    id: 1, title: 'sdf', projectId: 7, projectCode: 'PAY-002', targetHours: 24, loggedHours: 8, isClosed: false, ...o,
})
const payroll = opt({ id: 1, projectId: 7 })
const crm = opt({ id: 2, projectId: 8, projectCode: 'CRM', title: 'crm' })
const closed = opt({ id: 3, projectId: 7, isClosed: true, title: 'old' })

describe('taskOptionsForRow', () => {
    it('narrows to the row project', () => {
        expect(taskOptionsForRow([payroll, crm], '7', '').map((o) => o.id)).toEqual([1])
    })
    it('offers every open task while the row has no project', () => {
        expect(taskOptionsForRow([payroll, crm], '', '').map((o) => o.id)).toEqual([1, 2])
    })
    it('hides a closed task unless the row already carries it', () => {
        expect(taskOptionsForRow([payroll, closed], '7', '').map((o) => o.id)).toEqual([1])
        expect(taskOptionsForRow([payroll, closed], '7', '3').map((o) => o.id)).toEqual([1, 3])
    })
})

describe('retainedWorkTaskId', () => {
    it('keeps a task on the new project', () => expect(retainedWorkTaskId('1', '7', [payroll])).toBe('1'))
    it('drops a task the new project does not match', () => expect(retainedWorkTaskId('1', '8', [payroll])).toBe(''))
    it('keeps an unknown id while the options are still loading', () => expect(retainedWorkTaskId('1', '8', [])).toBe('1'))
})

describe('copyableWorkTaskId', () => {
    it('carries an open task', () => expect(copyableWorkTaskId('1', [payroll])).toBe('1'))
    it('drops a closed or unlisted one', () => {
        expect(copyableWorkTaskId('3', [closed])).toBe('')
        expect(copyableWorkTaskId('9', [payroll])).toBe('')
    })
})

describe('taskOptionLabel', () => {
    it('reads code, title and progress', () => expect(taskOptionLabel(payroll)).toBe('PAY-002 · sdf — 16h left'))
    it('marks a closed task', () => expect(taskOptionLabel(closed)).toBe('PAY-002 · old (closed)'))
    it('says over', () => expect(taskOptionLabel(opt({ loggedHours: 30 }))).toBe('PAY-002 · sdf — 6h over'))
    it('says logged with no target', () => expect(taskOptionLabel(opt({ targetHours: null }))).toBe('PAY-002 · sdf — 8h logged'))
})
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `cd client && node_modules/.bin/vitest run src/lib/timesheet-tasks.test.ts`
Expected: FAIL, cannot resolve `./timesheet-tasks`.

- [ ] **Step 3: Write the helpers**

`client/src/lib/timesheet-tasks.ts`:

```ts
import type { TimesheetTaskOption } from './types'
import { describeTaskProgress, formatHours } from './work-tasks'

/*
 * The Task column on a timesheet row. Mirrors TimesheetEntryTaskRule: a row may
 * name one of its owner's open tasks on its own project. The server re-checks
 * only a changed task or project, so a closed task already on a row stays listed
 * for that row, and for no other.
 */

export function taskOptionsForRow(options: TimesheetTaskOption[], projectId: string, currentTaskId: string): TimesheetTaskOption[] {
    return options.filter((o) =>
        (!o.isClosed || String(o.id) === currentTaskId)
        && (!projectId || String(o.projectId) === projectId))
}

/**
 * The row's task after its project moves: kept if it belongs to the new project,
 * otherwise cleared, since the server would refuse it. An id not in the list yet
 * (options still loading) is left alone rather than wiped.
 */
export function retainedWorkTaskId(workTaskId: string, projectId: string, options: TimesheetTaskOption[]): string {
    if (!workTaskId) return ''
    const option = options.find((o) => String(o.id) === workTaskId)
    if (!option) return workTaskId
    return String(option.projectId) === projectId ? workTaskId : ''
}

/** "Copy to rest of week" carries a task only while it is still open and listed. */
export function copyableWorkTaskId(workTaskId: string, options: TimesheetTaskOption[]): string {
    const option = options.find((o) => String(o.id) === workTaskId)
    return option && !option.isClosed ? workTaskId : ''
}

export function taskOptionLabel(option: TimesheetTaskOption): string {
    const name = option.projectCode ? `${option.projectCode} · ${option.title}` : option.title
    if (option.isClosed) return `${name} (closed)`
    const { text } = describeTaskProgress(option.targetHours, option.loggedHours)
    // The picker has room for one figure: what is left, how far over, or what is logged.
    const short = text.includes(' · ') ? text.split(' · ')[1] : text
    return `${name} — ${short === 'nothing logged' ? `${formatHours(0)}h logged` : short}`
}
```

Run the helper tests: `cd client && node_modules/.bin/vitest run src/lib/timesheet-tasks.test.ts`. Expected: PASS.

- [ ] **Step 4: Wire the column into `NewTimesheetPage`**

Make each change below in order.

1. Row type: add `workTaskId: string` to `type Task`. In `newTask()` add `workTaskId: ''`. In `buildBucketsFromEntries` add `workTaskId: entry.workTaskId != null ? String(entry.workTaskId) : '',`.
2. Imports: add `getTimesheetTaskOptions` to the `../../lib/api` import; add `import { copyableWorkTaskId, retainedWorkTaskId, taskOptionLabel, taskOptionsForRow } from '../../lib/timesheet-tasks'`; add `TimesheetTaskOption` to the `../../lib/types` type import.
3. Query, beside the other `useQuery` calls near line 266:

```tsx
    // The Task picker: the owner's open tasks, plus any a row on this sheet names.
    const { data: taskOptions = [] } = useQuery({
        queryKey: ['work-tasks', 'timesheet-options', currentTs?.id ?? 'mine'],
        queryFn: () => getTimesheetTaskOptions(currentTs?.id),
    })
```

4. `updateTask`: inside the `map` callback, after the `projectId`/`projectTypeId` block, add:

```tsx
                    // Picking a task on a row with no project yet fills the project in;
                    // moving the project drops a task that belongs to another one.
                    if (field === 'workTaskId' && value && !next.projectId) {
                        const option = taskOptions.find((o) => String(o.id) === value)
                        if (option?.projectId != null) next.projectId = String(option.projectId)
                    }
                    if (field === 'projectId' || field === 'projectTypeId') {
                        next.workTaskId = retainedWorkTaskId(next.workTaskId, next.projectId, taskOptions)
                    }
```

   Put the `workTaskId` fill **before** the existing activity/component retention block so they run against the filled project. That means moving the `if (field === 'projectId' || field === 'projectTypeId')` retention condition to also fire when `field === 'workTaskId'`. Change that condition to `if (field === 'projectId' || field === 'projectTypeId' || field === 'workTaskId')`.
5. `copyToRestOfWeek`: include `t.workTaskId` in the `pair` key, and add `workTaskId: copyableWorkTaskId(t.workTaskId, taskOptions),` to the copied row.
6. `handleSave`: add `workTaskId: x.task.workTaskId ? Number(x.task.workTaskId) : null,` to both the `createTimesheetEntry` and the `updateTimesheetEntry` payloads. Add `&& String(existing.workTaskId ?? '') === x.task.workTaskId` to the `same` comparison. Add `queryClient.invalidateQueries({ queryKey: ['work-tasks'] }),` to the `Promise.all`.
7. Grid: `TASK_GRID = '1fr 1.3fr 1.3fr 1.1fr 1.1fr 84px 40px'`, and insert `{ label: 'Task' },` after `{ label: 'Project' },` in `TASK_HEADERS`. Update the comment above to "the five pickers".
8. `DayCard`: add the prop `taskOptions: TimesheetTaskOption[]` to its props type and destructuring, and pass `taskOptions={taskOptions}` where it is rendered (near line 669). In the grid JSX, insert between the Project `Select` and the Component `Select`:

```tsx
                                <Select
                                    size="small"
                                    displayEmpty
                                    value={t.workTaskId}
                                    onChange={(e) => onUpdateTask(t._id, 'workTaskId', e.target.value)}
                                    disabled={disabled}
                                    sx={TASK_FIELD_SX}
                                    inputProps={{ 'aria-label': 'Task' }}
                                >
                                    <MenuItem value="">
                                        <Box component="em" sx={{ color: 'text.disabled' }}>No task</Box>
                                    </MenuItem>
                                    {taskOptionsForRow(taskOptions, t.projectId, t.workTaskId).map((o) => (
                                        <MenuItem key={o.id} value={String(o.id)} disabled={o.isClosed}>
                                            {taskOptionLabel(o)}
                                        </MenuItem>
                                    ))}
                                </Select>
```

   A closed task already on the row is rendered as a disabled item. MUI still shows it as the selected value, and it cannot be picked again once changed.

- [ ] **Step 5: Type-check, lint, run the timesheet tests**

Run: `cd client && node_modules/.bin/tsc -b --noEmit && npm run lint && node_modules/.bin/vitest run src/lib/timesheet-tasks.test.ts src/components/timesheet`
Expected: PASS. Any fixture that builds an editor row `Task` literal needs `workTaskId: ''`.

- [ ] **Step 6: Commit**

```bash
git add client/src/lib/timesheet-tasks.ts client/src/lib/timesheet-tasks.test.ts client/src/components/timesheet/NewTimesheetPage.tsx
git commit -m "Add a Task column to the timesheet editor"
```

---

### Task 8: Task titles in the reviewers' day breakdown

**Files:**
- Modify: `client/src/components/timesheet/TimesheetDailyBreakdown.tsx`
- Test: `client/src/components/timesheet/TeamTimesheetPage.test.tsx`

**Interfaces:**
- Consumes: `getTimesheetTaskOptions(timesheetId)` (Task 5), `TimesheetEntry.workTaskId`

- [ ] **Step 1: Write the failing test**

In `TeamTimesheetPage.test.tsx`, find the existing test that opens a sheet's View dialog and asserts a row's project name in the breakdown. Copy it. In the copy:
- give the mocked `getTimesheet` entry `workTaskId: 5`;
- mock `getTimesheetTaskOptions` to resolve `[{ id: 5, title: 'Payroll export', projectId: <that entry's projectId>, projectCode: 'PAY-002', targetHours: 24, loggedHours: 8, isClosed: false }]`;
- assert `expect(await within(dialog).findByText(/Payroll export/)).toBeInTheDocument()`.

The file mocks `../../lib/api` as a module, so add `getTimesheetTaskOptions: vi.fn()` to that mock factory and a default `mockResolvedValue([])` in its `beforeEach`. This keeps every other test in the file passing.

- [ ] **Step 2: Run the test and confirm it fails**

Run: `cd client && node_modules/.bin/vitest run src/components/timesheet/TeamTimesheetPage.test.tsx`
Expected: FAIL, `Payroll export` not found.

- [ ] **Step 3: Implement**

In `TimesheetDailyBreakdown.tsx`, add `getTimesheetTaskOptions` to the api import and:

```tsx
    // Titles for the rows logged against a task. Same endpoint as the editor's
    // picker, scoped to this sheet, so it lists every task a row here names.
    const { data: taskOptions = [] } = useQuery({
        queryKey: ['work-tasks', 'timesheet-options', ts.id],
        queryFn: () => getTimesheetTaskOptions(ts.id),
    })
    const taskById = useMemo(() => new Map(taskOptions.map((o) => [o.id, o])), [taskOptions])
```

Then, where each entry becomes a breakdown `Task` (the `dayEntries.map((e): Task => …)`), fold the title into the `detail` string. That's the line under the project name that already lists type · component · activity. Prefix it:

```tsx
                const workTask = e.workTaskId != null ? taskById.get(e.workTaskId) : undefined
                // …existing detail construction…, then:
                const detailWithTask = workTask ? [`Task: ${workTask.title}`, detail].filter(Boolean).join(' · ') : detail
```

and return `detail: detailWithTask`. (Use the variable name the existing code gives the detail string.)

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `cd client && node_modules/.bin/vitest run src/components/timesheet`
Expected: PASS, including `AllTimesheetsPage.test.tsx`. If that file also mocks `../../lib/api` wholesale, add the same `getTimesheetTaskOptions: vi.fn().mockResolvedValue([])` to it.

- [ ] **Step 5: Commit**

```bash
git add client/src/components/timesheet
git commit -m "Show the task a row was logged against in the day breakdown"
```

---

### Task 9: CLAUDE.md, full test pass, manual check

**Files:**
- Modify: `CLAUDE.md` (the `TimesheetEntry` and `WorkTask` rows of the Domain Model table)

- [ ] **Step 1: Update CLAUDE.md**

In the `TimesheetEntry` row, append: `, and optional WorkTaskId — one of the owner's open tasks on the same project (TimesheetEntryTaskRule, asked only when the task or project changes; SetNull when the task is deleted)`.

In the `WorkTask` row, replace `TargetHours` (optional plan, 1–9,999, quoted only — timesheet entries are not linked to tasks) with `TargetHours` (optional plan, 1–9,999, measured against the timesheet rows linked to the task: `WorkTaskDto.LoggedHours` sums them in any sheet status on every read — in memory, since SQLite cannot aggregate a decimal — and the card reads "8h logged · 16h left" or "4h over" via `describeTaskProgress`; over target is never refused. The editor's Task picker reads `GET /api/worktasks/timesheet-options`, which is the sheet owner's open tasks plus any already on the sheet, and the caller's own when no sheet exists yet).

- [ ] **Step 2: Full backend test run**

Run: `dotnet test Tests/WorkTrack.Tests`
Expected: all pass. Report any failure verbatim rather than skipping it.

- [ ] **Step 3: Client tests in per-directory batches** (a full run OOMs and still exits 0)

Run each and read the summary line, not the exit code:
`cd client && node_modules/.bin/vitest run src/lib`
`cd client && node_modules/.bin/vitest run src/components/tasks`
`cd client && node_modules/.bin/vitest run src/components/timesheet`
Then: `npm run build`.

- [ ] **Step 4: Manual check in the app**

Restart the API (a stale API serves old code), apply the migration (`dotnet ef database update --project Persistence --startup-project API`), then check in the browser:
- **Submit Timesheet:** the Task column lists "PAY-002 · sdf — 24h left" on a Payroll Automation row.
- Picking the task on an empty row fills the project; switching the project clears the task.
- Save 4h, then open **Tasks**: the card reads "4h logged · 20h left".
- **Team Timesheets → View** shows "Task: sdf" on that row.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "Document the timesheet entry to task link in CLAUDE.md"
```
