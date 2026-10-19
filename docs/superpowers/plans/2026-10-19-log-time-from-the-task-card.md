# Log Time from the Task Card — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An assignee logs hours on an open task straight from its card or details dialog; the server finds or creates that week's Draft timesheet, adds to the task's entry for that day or creates one, and the card's "logged" figure updates — so task work never requires opening the timesheet editor.

**Architecture:** One server rule change (two entries on the same project and day are no longer an overlap when they point at different tasks), one new MediatR command `LogTaskTime` that reuses `TimesheetEntryValidator` and `TimesheetEntryTaskRule`, one new action on `WorkTasksController`, and a client `LogTimeButton` popover rendered by the card's action bar and the details dialog, with a snackbar that links to the week in the editor. `AppDbContext` is injected into the handler, as every handler does; no repository layer.

**Tech Stack:** .NET 10, MediatR, EF Core (handler tests on `TransactionalTestDb`, SQLite in-memory, for real unique indexes), xUnit; React 19, TypeScript, MUI 7, TanStack Query, Vitest + Testing Library. Client tests run with `node_modules/.bin/vitest` from `client/` (never bare `npx vitest`). Server tests: see Task 5 for the `dotnet test` invocation that works on this machine.

**Spec:** `docs/superpowers/specs/2026-10-19-simpler-tasks-and-timesheets-design.md`, section "Phase 3 — Log time from the task card".

## Global Constraints

- Route: `POST /api/worktasks/{id}/log-time`, gated like the rest of `WorkTasksController` (`AppRoles.LeaveAndTimeRoles` on the class, nothing extra on the action). Body `{ "date": "2026-10-19", "hours": 2, "notes": "optional" }`.
- Only an **assignee** of the task logs against it (`NotAnAssigneeMessage`) — a creator who is not on the task does not. A task outside the caller's visibility answers 404 (`WorkTaskAccess.NotFoundMessage`), like every other task action.
- `Done` and `Cancelled` refuse (`TaskClosedMessage`); `ToDo`, `InProgress` and `AwaitingConfirmation` are open.
- A task with no project refuses (`TaskHasNoProjectMessage`).
- `hours` is a multiple of 0.5 between 0.5 and 24; `date` is not in the future by the same reading `TimesheetEntryValidator` uses (UTC today). The command carries a nullable `NowUtc` test seam, as the attendance and leave-status queries do.
- Weeks run Monday to Sunday; `PeriodStart` is the Monday at midnight, `PeriodEnd` the Sunday, matching what the editor creates. A missing sheet is created in `Draft` with the caller's profile `DepartmentId`. A `Draft` or `Rejected` sheet is written to; `Submitted`, `Approved` or `Resubmitted` refuses with `WeekSubmittedMessage` = `"This week's timesheet has already been submitted. Open it from Timesheets to change it."`.
- Type, Component and Activity on a **new** entry are filled by Phase 2's rule: the one option the project leaves, else null (the server mirror of `fillSingleOptions` in `client/src/lib/timesheet-columns.ts`). An existing entry's narrowing fields are never touched.
- Same task, same date: hours are **added** to the existing entry and a note is appended on a new line. Otherwise a new entry with `WorkTaskId` set.
- Nobody is emailed. `notificationsUpdated` fans out exactly as other task writes do (`NotifyAsync`).
- Result kinds: not found → `Failure` (404); not an assignee → `Forbidden` (403); closed task, no project, submitted week → `Conflict` (409); bad hours/date and any `TimesheetEntryValidator` refusal → `Invalid` (400).
- Client copy: button **Log time**; popover fields **Date**, **Hours**, **Note**, button **Log**; snackbar `"{h}h logged to the week of {d MMM} · {h}h on {Ddd d}"` with action **Open timesheet**.
- Commit after every task, message in the repository's style, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

Inputs the spec implies but does not spell out, each pinned by a test in the task that owns the code:

1. **Two logs on the same task and day** must add up on one entry (2h then 1.5h → one entry of 3.5h), not refuse as a duplicate or create a second row. (Task 2)
2. **Logging on a different task of the same project, same day, with no type or component** must create a second entry — the case Phase 2 made common and the old overlap rule refused. (Tasks 1 and 2)
3. **A week whose sheet was Rejected** must accept the log (the employee is fixing it), while a Submitted one refuses with the message that sends them to Timesheets. (Task 2)
4. **The day would pass 24 hours** (8h already on another project, log 17h) must refuse with the validator's message and write nothing — no half-created sheet left behind. (Task 2)
5. **A date on a Sunday** must land in the week that started the Monday before, not the following one. (Task 2)

---

### Task 1: The entry's task becomes part of its identity

**Files:**
- Modify: `Application/Timesheets/Support/TimesheetEntryValidator.cs` (the overlap check, ~lines 92–104, and the doc comment's bullet list ~line 28)
- Test: `Tests/WorkTrack.Tests/TimesheetEntryWorkTaskOverlapTests.cs` (create)

**Interfaces:**
- Consumes: nothing new.
- Produces: `TimesheetEntryValidator.Validate` refuses a same-date row only when project, type, component **and `WorkTaskId`** all match. The message stays `"An entry for this project, type and component on this date already exists."` so existing tests keep passing.

- [ ] **Step 1: Write the failing tests**

Create `Tests/WorkTrack.Tests/TimesheetEntryWorkTaskOverlapTests.cs`:

```csharp
using Application.Timesheets.Support;
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Two rows on the same project, type and component on one date are a duplicate —
/// unless they point at different tasks. Two pieces of work on one project are two
/// facts, and Phase 2 made the "no type, no component" row the common case, so
/// without this a second task on the same project could not be logged that day.
/// </summary>
public class TimesheetEntryWorkTaskOverlapTests
{
    private static readonly DateTime Day = new(2026, 10, 19);
    private static readonly DateTime Today = new(2026, 10, 20);

    private static TimesheetEntry Row(string id, int? taskId, decimal hours = 2m) => new()
    {
        Id = id, TimesheetId = "ts", ProjectId = 10, Date = Day, HoursWorked = hours, WorkTaskId = taskId,
    };

    [Fact]
    public void Two_tasks_on_the_same_project_and_day_are_two_entries()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: 2), [Row("a", taskId: 1)], Today);
        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void A_task_row_beside_a_row_with_no_task_is_not_a_duplicate()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: 1), [Row("a", taskId: null)], Today);
        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void The_same_task_twice_on_one_day_is_still_a_duplicate()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: 1), [Row("a", taskId: 1)], Today);
        Assert.False(result.IsValid);
        Assert.Contains("already exists", result.Error);
    }

    [Fact]
    public void Two_rows_with_no_task_still_collide_as_before()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: null), [Row("a", taskId: null)], Today);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Different_tasks_still_share_the_24_hour_day()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: 2, hours: 17m), [Row("a", taskId: 1, hours: 8m)], Today);
        Assert.False(result.IsValid);
        Assert.Contains("daily cap", result.Error);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_ROOT="/c/Program Files/dotnet" DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests --artifacts-path /c/Users/user/AppData/Local/Temp/claude/c--Practice-Own-2026-WorkTrack/04097ed7-82cc-4cd5-8652-a1163cfd7dad/scratchpad/artifacts --filter "FullyQualifiedName~TimesheetEntryWorkTaskOverlapTests"
```

Expected: 2 FAIL (`Two_tasks_on_the_same_project_and_day_are_two_entries`, `A_task_row_beside_a_row_with_no_task_is_not_a_duplicate`), 3 PASS. The `--artifacts-path` keeps the running API's locked `bin/` out of the way; the two environment variables work around this machine's testhost quirks (see `MEMORY.md`).

- [ ] **Step 3: Implement**

In `TimesheetEntryValidator.Validate`, change the overlap predicate to include the task, and update the comment above it:

```csharp
        // Project, type and component together, because one project can
        // legitimately be worked on as two kinds of engagement, or on two of its
        // components, in a single day — 2h of Support on DM and 3h of Support on
        // Lasernet are two facts, not a duplicate. The task is part of it too:
        // two tasks on one project are two pieces of work, and with the editor
        // leaving type and component blank whenever the catalogue has nothing to
        // choose, the task is often the only thing telling two rows apart. Rows
        // that leave all of them blank still collide, which is the pre-type
        // behaviour unchanged.
        if (sameDayEntries.Any(e =>
                e.ProjectId == candidate.ProjectId
                && e.ProjectTypeId == candidate.ProjectTypeId
                && e.ProjectComponentId == candidate.ProjectComponentId
                && e.WorkTaskId == candidate.WorkTaskId))
            return ValidationResult.Fail("An entry for this project, type and component on this date already exists.");
```

In the doc comment's bullet list, change `duplicating an existing project, type *and component* on the same date (overlap),` to `duplicating an existing project, type, component *and task* on the same date (overlap),`.

- [ ] **Step 4: Run the tests to verify they pass, and the existing overlap tests**

```bash
DOTNET_ROOT="/c/Program Files/dotnet" DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests --artifacts-path /c/Users/user/AppData/Local/Temp/claude/c--Practice-Own-2026-WorkTrack/04097ed7-82cc-4cd5-8652-a1163cfd7dad/scratchpad/artifacts --filter "FullyQualifiedName~TimesheetEntry"
```

Expected: PASS — the new 5 plus every existing `TimesheetEntry*` test (`TimesheetEntryProjectTypeScopeTests`, `TimesheetEntryComponentScopeTests`, `TimesheetEntryTaskRuleTests`, …), none of which sets `WorkTaskId` on both sides of a collision.

- [ ] **Step 5: Commit**

```bash
git add Application/Timesheets/Support/TimesheetEntryValidator.cs Tests/WorkTrack.Tests/TimesheetEntryWorkTaskOverlapTests.cs
git commit -m "Timesheet entries: two tasks on one project and day are two entries, not a duplicate

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The `LogTaskTime` command

**Files:**
- Create: `Application/WorkTasks/DTOs/LogTaskTimeDtos.cs`
- Create: `Application/Timesheets/Support/TimesheetEntryDefaults.cs`
- Create: `Application/WorkTasks/Commands/LogTaskTime.cs`
- Test: `Tests/WorkTrack.Tests/WorkTasks/LogTaskTimeTests.cs` (create)

**Interfaces:**
- Consumes: Task 1's overlap rule; `TimesheetEntryValidator.Validate`, `TimesheetEntryTaskRule.CheckAsync`, `WorkTaskAccess.FindVisibleAsync` / `NotFoundMessage`, `WorkTaskProjection.LoadDtoAsync`.
- Produces:
  - `public class LogTaskTimeRequest { DateOnly Date; decimal Hours; string? Notes }`
  - `public class LogTaskTimeResult { WorkTaskDto Task; string TimesheetId; string EntryId; DateOnly WeekStart; decimal HoursOnEntry; decimal HoursOnDay; string TimesheetStatus }`
  - `LogTaskTime.Command { int TaskId; string CallerUserId; bool AssignedOnly; DateOnly Date; decimal Hours; string? Notes; DateTime? NowUtc }` → `Result<LogTaskTimeResult>`; handler `LogTaskTime.Handler(AppDbContext context)`.
  - Messages on `LogTaskTime`: `NotAnAssigneeMessage`, `TaskClosedMessage`, `TaskHasNoProjectMessage`, `WeekSubmittedMessage`, `HoursMessage`, `FutureDateMessage`, `NoProfileMessage`.
  - `TimesheetEntryDefaults.SingleOptionsAsync(AppDbContext, int projectId, CancellationToken)` → `(int? ProjectTypeId, int? ProjectComponentId, int? ActivityTypeId)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/WorkTrack.Tests/WorkTasks/LogTaskTimeTests.cs`:

```csharp
using Application.Core;
using Application.WorkTasks.Commands;
using Application.WorkTasks.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// An assignee logs hours on an open task from its card: the week's Draft sheet is
/// found or made, the task's entry for that day is added to or created, and every
/// timesheet rule still holds.
/// </summary>
public class LogTaskTimeTests
{
    // Monday 19 October 2026; "now" is the Tuesday after.
    private static readonly DateOnly Monday = new(2026, 10, 19);
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static Task<Result<LogTaskTimeResult>> Log(AppDbContext db, int taskId, string caller, DateOnly date, decimal hours,
        string? notes = null, bool assignedOnly = false) =>
        new LogTaskTime.Handler(db).Handle(new LogTaskTime.Command
        {
            TaskId = taskId, CallerUserId = caller, AssignedOnly = assignedOnly,
            Date = date, Hours = hours, Notes = notes, NowUtc = Now,
        }, CancellationToken.None);

    private static async Task<int> Seeded(AppDbContext db, WorkTask task)
    {
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task.Id;
    }

    private static async Task<Timesheet> SheetFor(AppDbContext db, string userId, TimesheetStatus status)
    {
        var sheet = new Timesheet
        {
            Id = Guid.NewGuid().ToString(), EmployeeProfileId = $"p-{userId}", DepartmentId = Sales,
            PeriodStart = Monday.ToDateTime(TimeOnly.MinValue), PeriodEnd = Monday.AddDays(6).ToDateTime(TimeOnly.MinValue),
            Status = status,
        };
        db.Timesheets.Add(sheet);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return sheet;
    }

    [Fact]
    public async Task Creates_the_weeks_draft_and_an_entry_on_the_task()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 2m, "Chased the client", assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        var sheet = await db.Timesheets.AsNoTracking().SingleAsync();
        Assert.Equal($"p-{Employee}", sheet.EmployeeProfileId);
        Assert.Equal(Sales, sheet.DepartmentId);
        Assert.Equal(TimesheetStatus.Draft, sheet.Status);
        Assert.Equal(Monday.ToDateTime(TimeOnly.MinValue), sheet.PeriodStart);
        Assert.Equal(Monday.AddDays(6).ToDateTime(TimeOnly.MinValue), sheet.PeriodEnd);
        Assert.Equal(2m, sheet.TotalHours);
        var entry = await db.TimesheetEntries.AsNoTracking().SingleAsync();
        Assert.Equal(id, entry.WorkTaskId);
        Assert.Equal(SalesProject, entry.ProjectId);
        Assert.Equal("Chased the client", entry.Notes);
        Assert.Equal(Monday, result.Value!.WeekStart);
        Assert.Equal(2m, result.Value.HoursOnEntry);
        Assert.Equal(2m, result.Value.HoursOnDay);
        Assert.Equal("Draft", result.Value.TimesheetStatus);
        Assert.Equal(2m, result.Value.Task.LoggedHours);
    }

    [Fact]
    public async Task A_second_log_on_the_same_task_and_day_adds_to_the_entry()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        await Log(db, id, Employee, Monday, 2m, "Morning", assignedOnly: true);
        var result = await Log(db, id, Employee, Monday, 1.5m, "Afternoon", assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        var entry = await db.TimesheetEntries.AsNoTracking().SingleAsync();
        Assert.Equal(3.5m, entry.HoursWorked);
        Assert.Equal("Morning\nAfternoon", entry.Notes);
        Assert.Equal(3.5m, result.Value!.HoursOnEntry);
        Assert.Equal(3.5m, (await db.Timesheets.AsNoTracking().SingleAsync()).TotalHours);
    }

    [Fact]
    public async Task A_second_task_on_the_same_project_and_day_gets_its_own_entry()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var first = await Seeded(db, NewTask(Sales, Hr, Employee, "first", WorkTaskStatus.InProgress));
        var second = await Seeded(db, NewTask(Sales, Hr, Employee, "second", WorkTaskStatus.ToDo));

        await Log(db, first, Employee, Monday, 2m, assignedOnly: true);
        var result = await Log(db, second, Employee, Monday, 3m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, await db.TimesheetEntries.CountAsync());
        Assert.Equal(5m, result.Value!.HoursOnDay);
    }

    [Fact]
    public async Task Writes_to_a_rejected_week()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));
        var sheet = await SheetFor(db, Employee, TimesheetStatus.Rejected);

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(sheet.Id, result.Value!.TimesheetId);
        Assert.Equal("Rejected", result.Value.TimesheetStatus);
    }

    [Theory]
    [InlineData(TimesheetStatus.Submitted)]
    [InlineData(TimesheetStatus.Approved)]
    [InlineData(TimesheetStatus.Resubmitted)]
    public async Task Refuses_a_week_already_submitted(TimesheetStatus status)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));
        await SheetFor(db, Employee, status);

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(LogTaskTime.WeekSubmittedMessage, result.Error);
        Assert.Equal(0, await db.TimesheetEntries.CountAsync());
    }

    [Fact]
    public async Task A_sunday_belongs_to_the_week_that_started_the_monday_before()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));
        var sunday = new DateOnly(2026, 10, 18);

        var result = await Log(db, id, Employee, sunday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new DateOnly(2026, 10, 12), result.Value!.WeekStart);
    }

    [Fact]
    public async Task Refuses_a_day_past_24_hours_and_leaves_nothing_behind()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var first = await Seeded(db, NewTask(Sales, Hr, Employee, "first", WorkTaskStatus.InProgress));
        var second = await Seeded(db, NewTask(Sales, Hr, Employee, "second", WorkTaskStatus.InProgress));
        await Log(db, first, Employee, Monday, 8m, assignedOnly: true);

        var result = await Log(db, second, Employee, Monday, 17m, assignedOnly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("daily cap", result.Error);
        Assert.Equal(1, await db.TimesheetEntries.CountAsync());
        Assert.Equal(8m, (await db.Timesheets.AsNoTracking().SingleAsync()).TotalHours);
    }

    [Fact]
    public async Task A_refused_first_log_creates_no_sheet()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 25m, assignedOnly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, await db.Timesheets.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(1.3)]
    [InlineData(24.5)]
    public async Task Refuses_hours_that_are_not_half_hours_between_half_and_24(double hours)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, (decimal)hours, assignedOnly: true);

        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Equal(LogTaskTime.HoursMessage, result.Error);
    }

    [Fact]
    public async Task Refuses_a_future_date()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, new DateOnly(2026, 10, 21), 1m, assignedOnly: true);

        Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
        Assert.Equal(LogTaskTime.FutureDateMessage, result.Error);
    }

    [Fact]
    public async Task Refuses_a_creator_who_is_not_on_the_task()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, SalesManager, Monday, 1m);

        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
        Assert.Equal(LogTaskTime.NotAnAssigneeMessage, result.Error);
    }

    [Fact]
    public async Task Answers_not_found_for_a_task_the_caller_cannot_see()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Ops, OpsManager, OpsManager, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Done)]
    [InlineData(WorkTaskStatus.Cancelled)]
    public async Task Refuses_a_closed_task(WorkTaskStatus status)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: status));

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(LogTaskTime.TaskClosedMessage, result.Error);
    }

    [Fact]
    public async Task Accepts_a_task_waiting_for_confirmation()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.AwaitingConfirmation));

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Refuses_a_task_with_no_project()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress);
        task.ProjectId = null;
        var id = await Seeded(db, task);

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(LogTaskTime.TaskHasNoProjectMessage, result.Error);
    }

    [Fact]
    public async Task Fills_the_one_type_the_project_carries_and_leaves_a_choice_blank()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.ProjectTypes.Add(new ProjectType { Id = 1, Name = "Support" });
        db.ProjectTypes.Add(new ProjectType { Id = 2, Name = "Issue" });
        db.ProjectTypeAssignments.Add(new ProjectTypeAssignment { ProjectId = SalesProject, ProjectTypeId = 1 });
        db.ProjectComponents.Add(new ProjectComponent { Id = 1, Name = "DM" });
        db.ProjectComponents.Add(new ProjectComponent { Id = 2, Name = "jDocs" });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        var result = await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        var entry = await db.TimesheetEntries.AsNoTracking().SingleAsync();
        Assert.Equal(1, entry.ProjectTypeId);           // the project carries one type
        Assert.Null(entry.ProjectComponentId);          // declares none, two in the catalogue: a choice
        Assert.Null(entry.ActivityTypeId);              // empty catalogue: nothing to say
    }

    [Fact]
    public async Task Fills_a_single_entry_catalogue_for_a_project_that_declared_none()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        db.ProjectComponents.Add(new ProjectComponent { Id = 1, Name = "DM" });
        db.ProjectComponents.Add(new ProjectComponent { Id = 2, Name = "Retired", IsActive = false });
        await db.SaveChangesAsync();
        var id = await Seeded(db, NewTask(Sales, Hr, Employee, status: WorkTaskStatus.InProgress));

        await Log(db, id, Employee, Monday, 1m, assignedOnly: true);

        Assert.Equal(1, (await db.TimesheetEntries.AsNoTracking().SingleAsync()).ProjectComponentId);
    }
}
```

Two checks before running: `ProjectType`, `ProjectComponent` may have more required properties than `Id`/`Name` — if SQLite refuses the insert, set whatever the entity marks required (look at `Domain/ProjectType.cs` and `Domain/ProjectComponent.cs`; `Icon`/`ColorKey` default to strings). `WorkTask.ProjectId` is `int?`, so `task.ProjectId = null` compiles.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_ROOT="/c/Program Files/dotnet" DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests --artifacts-path /c/Users/user/AppData/Local/Temp/claude/c--Practice-Own-2026-WorkTrack/04097ed7-82cc-4cd5-8652-a1163cfd7dad/scratchpad/artifacts --filter "FullyQualifiedName~LogTaskTimeTests"
```

Expected: build FAIL — `LogTaskTime`, `LogTaskTimeResult` do not exist.

- [ ] **Step 3: Implement the DTOs**

Create `Application/WorkTasks/DTOs/LogTaskTimeDtos.cs`:

```csharp
namespace Application.WorkTasks.DTOs;

/// <summary>Hours an assignee logs on a task from its card.</summary>
public class LogTaskTimeRequest
{
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Where the hours went, so the card can say so and link to the week.</summary>
public class LogTaskTimeResult
{
    /// <summary>The task as it now reads — its LoggedHours includes the new hours.</summary>
    public WorkTaskDto Task { get; set; } = new();
    public string TimesheetId { get; set; } = string.Empty;
    public string EntryId { get; set; } = string.Empty;
    /// <summary>The Monday the week starts on.</summary>
    public DateOnly WeekStart { get; set; }
    /// <summary>The task's hours on that date, after this log.</summary>
    public decimal HoursOnEntry { get; set; }
    /// <summary>Everything on the sheet for that date, after this log.</summary>
    public decimal HoursOnDay { get; set; }
    public string TimesheetStatus { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Implement the server mirror of the single-option fill**

Create `Application/Timesheets/Support/TimesheetEntryDefaults.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Support;

/// <summary>
/// The Type, Component and Activity a new row takes when nobody is asked: the one
/// option its project leaves, otherwise none. The server mirror of
/// <c>fillSingleOptions</c> in <c>client/src/lib/timesheet-columns.ts</c> — keep the
/// two in step. A type is the project's own (an unclassified project leaves none);
/// a component or activity is the project's own, or the whole active catalogue for
/// a project that declared none.
/// </summary>
public static class TimesheetEntryDefaults
{
    public static async Task<(int? ProjectTypeId, int? ProjectComponentId, int? ActivityTypeId)> SingleOptionsAsync(
        AppDbContext context, int projectId, CancellationToken cancellationToken)
    {
        var types = await context.ProjectTypeAssignments
            .Where(a => a.ProjectId == projectId)
            .Join(context.ProjectTypes.Where(t => t.IsActive), a => a.ProjectTypeId, t => t.Id, (a, t) => t.Id)
            .ToListAsync(cancellationToken);

        var declaredComponents = await context.ProjectComponentAssignments
            .Where(a => a.ProjectId == projectId).Select(a => a.ComponentId).ToListAsync(cancellationToken);
        var components = await context.ProjectComponents
            .Where(c => c.IsActive && (declaredComponents.Count == 0 || declaredComponents.Contains(c.Id)))
            .Select(c => c.Id).ToListAsync(cancellationToken);

        var assignedActivities = await context.ProjectActivityAssignments
            .Where(a => a.ProjectId == projectId).Select(a => a.ActivityTypeId).ToListAsync(cancellationToken);
        var activities = await context.ProjectActivityTypes
            .Where(a => a.IsActive && (assignedActivities.Count == 0 || assignedActivities.Contains(a.Id)))
            .Select(a => a.Id).ToListAsync(cancellationToken);

        return (Single(types), Single(components), Single(activities));
    }

    private static int? Single(List<int> ids) => ids.Count == 1 ? ids[0] : null;
}
```

`declaredComponents` and `assignedActivities` are `List<int>`, never arrays — `string[]`/`int[].Contains` inside an EF lambda binds to the span overload on .NET 10 and throws at query time (see `MEMORY.md`).

- [ ] **Step 5: Implement the command**

Create `Application/WorkTasks/Commands/LogTaskTime.cs`:

```csharp
using Application.Core;
using Application.Timesheets.Support;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Commands;

/// <summary>
/// An assignee logs hours on an open task without opening the timesheet editor.
/// The week's sheet is found or made (Draft), the task's entry for that date is
/// added to or created, and every rule the editor's own save meets still applies —
/// <see cref="TimesheetEntryValidator"/> and <see cref="TimesheetEntryTaskRule"/>
/// run exactly as <c>TimesheetEntriesController.AddEntry</c> runs them. Emails nobody.
/// </summary>
public class LogTaskTime
{
    public const string NotAnAssigneeMessage = "Only someone working on this task can log time on it.";
    public const string TaskClosedMessage = "This task is closed. Reopen it to log more time.";
    public const string TaskHasNoProjectMessage = "This task has no project, so its hours have nowhere to go on a timesheet.";
    public const string WeekSubmittedMessage = "This week's timesheet has already been submitted. Open it from Timesheets to change it.";
    public const string HoursMessage = "Hours must be in half hours, between 0.5 and 24.";
    public const string FutureDateMessage = "Time can't be logged for a future date.";
    public const string NoProfileMessage = "No employee profile found for the current user.";

    public class Command : IRequest<Result<LogTaskTimeResult>>
    {
        public int TaskId { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        /// <summary>An Employee: only a task they are on or created is visible.</summary>
        public bool AssignedOnly { get; set; }
        public DateOnly Date { get; set; }
        public decimal Hours { get; set; }
        public string? Notes { get; set; }
        /// <summary>Test seam; the controller leaves it null.</summary>
        public DateTime? NowUtc { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<LogTaskTimeResult>>
    {
        public async Task<Result<LogTaskTimeResult>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.TaskId, request.CallerUserId, cancellationToken, request.AssignedOnly);
            if (task is null)
                return Result<LogTaskTimeResult>.Failure(WorkTaskAccess.NotFoundMessage);
            if (!task.Assignees.Any(a => a.UserId == request.CallerUserId))
                return Result<LogTaskTimeResult>.Forbidden(NotAnAssigneeMessage);
            if (task.Status is not (WorkTaskStatus.ToDo or WorkTaskStatus.InProgress or WorkTaskStatus.AwaitingConfirmation))
                return Result<LogTaskTimeResult>.Conflict(TaskClosedMessage);
            if (task.ProjectId is not { } projectId)
                return Result<LogTaskTimeResult>.Conflict(TaskHasNoProjectMessage);

            if (request.Hours < 0.5m || request.Hours > TimesheetEntryValidator.MaxHoursPerDay || request.Hours * 2 != decimal.Truncate(request.Hours * 2))
                return Result<LogTaskTimeResult>.Invalid(HoursMessage);
            var now = request.NowUtc ?? DateTime.UtcNow;
            // The same "future" TimesheetEntryValidator applies: the UTC calendar day.
            if (request.Date > DateOnly.FromDateTime(now))
                return Result<LogTaskTimeResult>.Invalid(FutureDateMessage);

            var profile = await context.EmployeeProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == request.CallerUserId, cancellationToken);
            if (profile is null)
                return Result<LogTaskTimeResult>.Failure(NoProfileMessage);

            // Weeks run Monday to Sunday; a Sunday belongs to the Monday before it.
            var weekStart = request.Date.AddDays(-(((int)request.Date.DayOfWeek + 6) % 7));
            var periodStart = weekStart.ToDateTime(TimeOnly.MinValue);
            var day = request.Date.ToDateTime(TimeOnly.MinValue);

            var sheet = await context.Timesheets
                .FirstOrDefaultAsync(t => t.EmployeeProfileId == profile.Id && t.PeriodStart == periodStart, cancellationToken);
            if (sheet is not null && sheet.Status is not (TimesheetStatus.Draft or TimesheetStatus.Rejected))
                return Result<LogTaskTimeResult>.Conflict(WeekSubmittedMessage);

            // Nothing is added to the context until every rule has passed, so a
            // refusal leaves no half-made sheet behind.
            var isNewSheet = sheet is null;
            sheet ??= new Timesheet
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeProfileId = profile.Id,
                DepartmentId = profile.DepartmentId,
                PeriodStart = periodStart,
                PeriodEnd = weekStart.AddDays(6).ToDateTime(TimeOnly.MinValue),
                Status = TimesheetStatus.Draft,
                CreatedAt = now,
            };

            var existing = isNewSheet
                ? []
                : await context.TimesheetEntries.Where(e => e.TimesheetId == sheet.Id).ToListAsync(cancellationToken);
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            var entry = existing.FirstOrDefault(e => e.WorkTaskId == task.Id && e.Date.Date == day);

            TimesheetEntry candidate;
            if (entry is not null)
            {
                candidate = new TimesheetEntry
                {
                    Id = entry.Id, TimesheetId = entry.TimesheetId, ProjectId = entry.ProjectId, Date = entry.Date,
                    HoursWorked = entry.HoursWorked + request.Hours,
                    Notes = notes is null ? entry.Notes : string.IsNullOrEmpty(entry.Notes) ? notes : $"{entry.Notes}\n{notes}",
                    ProjectTypeId = entry.ProjectTypeId, ProjectComponentId = entry.ProjectComponentId,
                    ActivityTypeId = entry.ActivityTypeId, WorkTaskId = entry.WorkTaskId,
                };
            }
            else
            {
                var (typeId, componentId, activityId) = await TimesheetEntryDefaults.SingleOptionsAsync(context, projectId, cancellationToken);
                candidate = new TimesheetEntry
                {
                    Id = Guid.NewGuid().ToString(), TimesheetId = sheet.Id, ProjectId = projectId, Date = day,
                    HoursWorked = request.Hours, Notes = notes, WorkTaskId = task.Id,
                    ProjectTypeId = typeId, ProjectComponentId = componentId, ActivityTypeId = activityId,
                };
            }

            var validation = TimesheetEntryValidator.Validate(
                candidate, existing, now,
                assignedActivityTypeIds: await context.ProjectActivityAssignments.Where(a => a.ProjectId == projectId).Select(a => a.ActivityTypeId).ToListAsync(cancellationToken),
                assignedProjectTypeIds: await context.ProjectTypeAssignments.Where(a => a.ProjectId == projectId).Select(a => a.ProjectTypeId).ToListAsync(cancellationToken),
                assignedComponentIds: await context.ProjectComponentAssignments.Where(a => a.ProjectId == projectId).Select(a => a.ComponentId).ToListAsync(cancellationToken));
            if (!validation.IsValid)
                return Result<LogTaskTimeResult>.Invalid(validation.Error!);

            // A new sheet is not saved yet, so the rule cannot find its owner through it;
            // the caller is the owner, and FindVisibleAsync plus the assignee check above
            // already established what the rule asks. An existing sheet is asked as usual.
            if (!isNewSheet)
            {
                var refusal = await TimesheetEntryTaskRule.CheckAsync(context, sheet.Id, candidate, entry, cancellationToken);
                if (refusal is not null)
                    return Result<LogTaskTimeResult>.Invalid(refusal);
            }

            if (isNewSheet) context.Timesheets.Add(sheet);
            if (entry is not null)
            {
                entry.HoursWorked = candidate.HoursWorked;
                entry.Notes = candidate.Notes;
            }
            else
            {
                context.TimesheetEntries.Add(candidate);
            }
            var others = existing.Where(e => e.Id != candidate.Id).Sum(e => e.HoursWorked);
            sheet.TotalHours = others + candidate.HoursWorked;

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (isNewSheet)
            {
                // Two logs for an unsaved week raced: the unique index on
                // (EmployeeProfileId, PeriodStart) let one through. Ask again.
                return Result<LogTaskTimeResult>.Conflict("Your timesheet for this week was just created. Please try again.");
            }

            var hoursOnDay = existing.Where(e => e.Id != candidate.Id && e.Date.Date == day).Sum(e => e.HoursWorked) + candidate.HoursWorked;
            return Result<LogTaskTimeResult>.Success(new LogTaskTimeResult
            {
                Task = await WorkTaskProjection.LoadDtoAsync(context, task.Id, request.CallerUserId, cancellationToken, callerManages: !request.AssignedOnly),
                TimesheetId = sheet.Id,
                EntryId = candidate.Id,
                WeekStart = weekStart,
                HoursOnEntry = candidate.HoursWorked,
                HoursOnDay = hoursOnDay,
                TimesheetStatus = sheet.Status.ToString(),
            });
        }
    }
}
```

Notes for the implementer:
- `TimesheetEntryValidator.Validate`'s `today` parameter is a `DateTime?`; passing `now` makes the validator's own future check agree with the command's.
- `sheet.TotalHours` is recomputed from the rows in hand rather than by `SumAsync`, because SQLite (the tests' provider) cannot aggregate a decimal — the same reason `WorkTaskProjection.LoggedHoursAsync` sums in memory.
- If the `ProjectId`-equality check inside `TimesheetEntryTaskRule` ever disagrees with the task (a task's project changed after an entry was logged), the existing-entry branch keeps `entry.ProjectId`; the rule then refuses with `OtherProjectMessage`, which is right — the hours should go on a fresh row the user edits in the editor.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
DOTNET_ROOT="/c/Program Files/dotnet" DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests --artifacts-path /c/Users/user/AppData/Local/Temp/claude/c--Practice-Own-2026-WorkTrack/04097ed7-82cc-4cd5-8652-a1163cfd7dad/scratchpad/artifacts --filter "FullyQualifiedName~LogTaskTimeTests"
```

Expected: PASS, 23 test cases (Theories expanded).

- [ ] **Step 7: Commit**

```bash
git add Application/WorkTasks/DTOs/LogTaskTimeDtos.cs Application/Timesheets/Support/TimesheetEntryDefaults.cs Application/WorkTasks/Commands/LogTaskTime.cs Tests/WorkTrack.Tests/WorkTasks/LogTaskTimeTests.cs
git commit -m "Tasks: an assignee logs hours on an open task into the week's draft timesheet

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The endpoint

**Files:**
- Modify: `API/Controllers/WorkTasksController.cs` (add an action after `UpdateWorkTaskStatus`, ~line 95)
- Modify: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskSurfaceTests.cs`

**Interfaces:**
- Consumes: `LogTaskTime.Command`, `LogTaskTimeRequest`, `LogTaskTimeResult` from Task 2; the controller's private `CallerUserId`, `AssignedOnly`, `NotifyAsync`.
- Produces: `POST /api/worktasks/{id}/log-time` → `200 LogTaskTimeResult`; action name `LogTime`.

- [ ] **Step 1: Write the failing test**

In `WorkTaskSurfaceTests`, add:

```csharp
    [Fact]
    public void Logging_time_is_a_post_on_the_task_open_to_every_task_role()
    {
        var action = typeof(WorkTasksController).GetMethod(nameof(WorkTasksController.LogTime))!;
        var post = action.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>();
        Assert.Equal("{id:int}/log-time", post!.Template);
        // No gate of its own: the class's Leave & Time roles, as for status moves.
        Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>());
    }
```

- [ ] **Step 2: Run it to verify it fails**

```bash
DOTNET_ROOT="/c/Program Files/dotnet" DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests --artifacts-path /c/Users/user/AppData/Local/Temp/claude/c--Practice-Own-2026-WorkTrack/04097ed7-82cc-4cd5-8652-a1163cfd7dad/scratchpad/artifacts --filter "FullyQualifiedName~WorkTaskSurfaceTests"
```

Expected: build FAIL — `WorkTasksController.LogTime` does not exist.

- [ ] **Step 3: Implement**

In `WorkTasksController`, after `UpdateWorkTaskStatus`:

```csharp
    /// <summary>
    /// An assignee logs hours on the task: into the week's Draft timesheet, on the
    /// task's entry for that date. See <see cref="LogTaskTime"/>.
    /// </summary>
    [HttpPost("{id:int}/log-time")]
    public async Task<ActionResult<LogTaskTimeResult>> LogTime(int id, LogTaskTimeRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new LogTaskTime.Command
        {
            TaskId = id,
            CallerUserId = CallerUserId,
            AssignedOnly = AssignedOnly,
            Date = request.Date,
            Hours = request.Hours,
            Notes = request.Notes,
        }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!.Task, cancellationToken);
        return HandleResult(result);
    }
```

`LogTaskTime` lives in `Application.WorkTasks.Commands` and the DTOs in `Application.WorkTasks.DTOs`, both already imported.

- [ ] **Step 4: Run the surface tests and the route table**

```bash
DOTNET_ROOT="/c/Program Files/dotnet" DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests --artifacts-path /c/Users/user/AppData/Local/Temp/claude/c--Practice-Own-2026-WorkTrack/04097ed7-82cc-4cd5-8652-a1163cfd7dad/scratchpad/artifacts --filter "FullyQualifiedName~WorkTaskSurfaceTests|FullyQualifiedName~MediatRHandlerRegistrationTests|FullyQualifiedName~SystemAdministrationSurfaceTests"
```

Expected: PASS. `An_employee_can_reach_every_other_action` now also covers `LogTime`; the route-table tests boot the real API and would fail on an ambiguous route.

- [ ] **Step 5: Commit**

```bash
git add API/Controllers/WorkTasksController.cs Tests/WorkTrack.Tests/WorkTasks/WorkTaskSurfaceTests.cs
git commit -m "Tasks: POST /api/worktasks/{id}/log-time

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The Log time control

**Files:**
- Modify: `client/src/lib/types/work-task.ts` (add `LogTaskTimeRequest`, `LogTaskTimeResult`)
- Modify: `client/src/lib/api/work-tasks.ts` (add `logTaskTime`)
- Modify: `client/src/lib/work-tasks.ts` (add `canLogTime`, `describeLoggedTime`)
- Modify: `client/src/lib/work-tasks.test.ts`
- Create: `client/src/components/tasks/LogTimeButton.tsx`
- Modify: `client/src/components/tasks/TaskCard.tsx` (`TaskItemProps`, `TaskActions`)
- Modify: `client/src/components/tasks/TaskDetailsDialog.tsx` (props, `DialogActions`)
- Modify: `client/src/components/tasks/TasksPage.tsx` (`itemProps`, the details dialog, a snackbar)
- Modify: `client/src/components/tasks/TasksPage.test.tsx`, `client/src/components/tasks/TaskDetailsDialog.test.tsx`

**Interfaces:**
- Consumes: `POST /api/worktasks/{id}/log-time` from Task 3; `isOpenTask`, `formatHours`, `todayIso` already in `lib/work-tasks.ts`; `uiStore.navigateToNewTimesheet(weekStart?: string)`.
- Produces:
  - `interface LogTaskTimeRequest { date: string; hours: number; notes: string | null }`
  - `interface LogTaskTimeResult { task: WorkTask; timesheetId: string; entryId: string; weekStart: string; hoursOnEntry: number; hoursOnDay: number; timesheetStatus: string }`
  - `logTaskTime(id: number, request: LogTaskTimeRequest): Promise<LogTaskTimeResult>`
  - `canLogTime(task: WorkTask, viewerId: string): boolean`
  - `describeLoggedTime(hours: number, date: string, result: LogTaskTimeResult): string`
  - `TaskItemProps.viewerId: string` and `TaskItemProps.onLogged: (result: LogTaskTimeResult, hours: number, date: string) => void` — the control reports the hours and date typed, which the snackbar quotes
  - `TaskDetailsDialog` props gain `viewerId: string` and the same `onLogged`
  - `<LogTimeButton task={WorkTask} today={string} onLogged={(result, hours, date) => void} dense?={boolean} />`

- [ ] **Step 1: Write the failing unit tests**

In `client/src/lib/work-tasks.test.ts`, add `canLogTime, describeLoggedTime` to the import from `./work-tasks`, and append:

```ts
describe('canLogTime', () => {
    it('lets an assignee log on an open task with a project', () => {
        expect(canLogTime(t({ status: 'InProgress' }), 'me')).toBe(true)
        expect(canLogTime(t({ status: 'AwaitingConfirmation' }), 'me')).toBe(true)
    })

    it('does not offer it to somebody who is not on the task, on a closed task, or with no project', () => {
        expect(canLogTime(t({}), 'someone-else')).toBe(false)
        expect(canLogTime(t({ status: 'Done' }), 'me')).toBe(false)
        expect(canLogTime(t({ status: 'Cancelled' }), 'me')).toBe(false)
        expect(canLogTime(t({ projectId: null }), 'me')).toBe(false)
    })
})

describe('describeLoggedTime', () => {
    it('says how much went where, and the day total', () => {
        const result = { weekStart: '2026-10-19', hoursOnDay: 3, hoursOnEntry: 3 } as never
        expect(describeLoggedTime(2, '2026-10-20', result)).toBe('2h logged to the week of 19 Oct · 3h on Tue 20')
    })
})
```

The base `t` in that file has `assignees: [{ userId: 'me', … }]`, `status: 'ToDo'` and `projectId: 10`.

- [ ] **Step 2: Run them to verify they fail**

```bash
node_modules/.bin/vitest run src/lib/work-tasks.test.ts
```

Expected: FAIL — `canLogTime` and `describeLoggedTime` are not exported.

- [ ] **Step 3: Implement the types, API call and helpers**

In `client/src/lib/types/work-task.ts`, append:

```ts
/** Hours an assignee logs on a task from its card (POST /worktasks/{id}/log-time). */
export interface LogTaskTimeRequest {
    /** yyyy-mm-dd, the viewer's calendar date. */
    date: string
    hours: number
    notes: string | null
}

/** Where the hours went: the task as it now reads, and the week they landed in. */
export interface LogTaskTimeResult {
    task: WorkTask
    timesheetId: string
    entryId: string
    /** yyyy-mm-dd, the Monday the week starts on. */
    weekStart: string
    /** The task's hours on that date, after this log. */
    hoursOnEntry: number
    /** Everything on the sheet for that date, after this log. */
    hoursOnDay: number
    timesheetStatus: string
}
```

In `client/src/lib/api/work-tasks.ts`, add `LogTaskTimeRequest, LogTaskTimeResult` to the type import and append:

```ts
/** Logs hours on a task the caller is on, into that week's draft timesheet. The server refuses a submitted week. */
export async function logTaskTime(id: number, request: LogTaskTimeRequest) {
    const response = await apiClient.post<LogTaskTimeResult>(`/worktasks/${id}/log-time`, request)
    return response.data
}
```

In `client/src/lib/work-tasks.ts`, add `LogTaskTimeResult` to the type import from `./types` and append:

```ts
/**
 * Whether the viewer may log hours on the task from its card: they are on it, it
 * is still open, and it has a project for the hours to go on. Mirrors the refusals
 * of LogTaskTime on the server; a submitted week is only known there, so it is
 * answered there.
 */
export function canLogTime(task: WorkTask, viewerId: string): boolean {
    return isOpenTask(task) && task.projectId != null && task.assignees.some((a) => a.userId === viewerId)
}

/** "2h logged to the week of 19 Oct · 3h on Tue 20" — the snackbar after a log. */
export function describeLoggedTime(hours: number, date: string, result: LogTaskTimeResult): string {
    const local = (iso: string) => {
        const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
        return new Date(y, m - 1, d)
    }
    const week = local(result.weekStart).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
    const day = local(date).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric' })
    return `${formatHours(hours)}h logged to the week of ${week} · ${formatHours(result.hoursOnDay)}h on ${day}`
}
```

Dates are built from their calendar parts, never `new Date('2026-10-19')`, which reads as UTC midnight and shows the previous day west of Greenwich.

- [ ] **Step 4: Run the unit tests to verify they pass**

```bash
node_modules/.bin/vitest run src/lib/work-tasks.test.ts
```

Expected: PASS.

- [ ] **Step 5: Write the failing component tests**

In `client/src/components/tasks/TasksPage.test.tsx`:

1. Add `logTaskTime: vi.fn(),` to the `vi.mock('../../lib/api', …)` object.
2. In `renderPage`, give the store a `uiStore`, and keep it reachable:

```tsx
const navigateToNewTimesheet = vi.fn()

function renderPage(roles: string[] = ['Manager']) {
    mobx.useStore.mockReturnValue({ authStore: { user: { id: 'me', roles } }, uiStore: { navigateToNewTimesheet } } as never)
    …
```

(and in the one test that builds its own store, `'ignores an active priority filter…'`, add the same `uiStore`.)

3. Append:

```tsx
describe('TasksPage log time', () => {
    const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}` })()

    it('offers Log time on a task the viewer is on, and not on one they are not', async () => {
        renderPage(['Employee'])
        expect(within(await cardFor('Mine to do')).getByRole('button', { name: /Log time/ })).toBeInTheDocument()
        expect(within(await cardFor('I asked for this')).queryByRole('button', { name: /Log time/ })).toBeNull()
    })

    it('logs the hours, says where they went, and opens the week from the message', async () => {
        api.logTaskTime.mockResolvedValue({
            task: { ...base, loggedHours: 2 }, timesheetId: 'ts', entryId: 'e', weekStart: '2026-10-19',
            hoursOnEntry: 2, hoursOnDay: 3, timesheetStatus: 'Draft',
        })
        renderPage(['Employee'])
        fireEvent.click(within(await cardFor('Mine to do')).getByRole('button', { name: /Log time/ }))
        fireEvent.change(screen.getByLabelText('Hours'), { target: { value: '2' } })
        fireEvent.change(screen.getByLabelText('Note'), { target: { value: 'Call' } })
        fireEvent.click(screen.getByRole('button', { name: 'Log' }))

        await waitFor(() => expect(api.logTaskTime).toHaveBeenCalledWith(1, { date: today, hours: 2, notes: 'Call' }))
        expect(await screen.findByText(/2h logged to the week of 19 Oct · 3h on/)).toBeInTheDocument()
        // The card refreshes from the server.
        await waitFor(() => expect(api.getWorkTasks).toHaveBeenCalledTimes(2))

        fireEvent.click(screen.getByRole('button', { name: 'Open timesheet' }))
        expect(navigateToNewTimesheet).toHaveBeenCalledWith('2026-10-19')
    })

    it("shows the server's refusal in the popover and keeps it open", async () => {
        api.logTaskTime.mockRejectedValue(Object.assign(new Error('409'), {
            isAxiosError: true,
            response: { status: 409, data: { message: "This week's timesheet has already been submitted. Open it from Timesheets to change it." } },
        }))
        renderPage(['Employee'])
        fireEvent.click(within(await cardFor('Mine to do')).getByRole('button', { name: /Log time/ }))
        fireEvent.click(screen.getByRole('button', { name: 'Log' }))

        expect(await screen.findByText(/already been submitted/)).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Log' })).toBeInTheDocument()
    })

    it('does not open the details for a click on Log time', async () => {
        renderPage(['Employee'])
        fireEvent.click(within(await cardFor('Mine to do')).getByRole('button', { name: /Log time/ }))
        expect(screen.queryByRole('dialog')).toBeNull()
    })
})
```

`axios.isAxiosError` checks the `isAxiosError` flag on the object, which is why the rejection carries it.

In `client/src/components/tasks/TaskDetailsDialog.test.tsx`: add `logTaskTime: vi.fn(),` to the mock; pass `viewerId="u-sam" onLogged={vi.fn()}` in `renderDialog`; append:

```tsx
    it('offers Log time to an assignee of an open task', async () => {
        renderDialog()
        expect(await screen.findByRole('button', { name: /Log time/ })).toBeInTheDocument()
    })

    it('does not offer Log time on a closed task', async () => {
        renderDialog({ ...task, status: 'Done', completedAtUtc: '2026-09-20T08:00:00' })
        await screen.findByRole('dialog')
        expect(screen.queryByRole('button', { name: /Log time/ })).toBeNull()
    })
```

(`task.assignees` there is `u-sam`, so `viewerId="u-sam"` makes the viewer an assignee.)

- [ ] **Step 6: Run them to verify they fail**

```bash
node_modules/.bin/vitest run src/components/tasks
```

Expected: the new tests FAIL — no "Log time" button. TypeScript may also flag `viewerId`/`onLogged` as unknown props on `TaskDetailsDialog`; that is expected until Step 7.

- [ ] **Step 7: Implement the control and wire it**

Create `client/src/components/tasks/LogTimeButton.tsx`:

```tsx
import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Popover from '@mui/material/Popover'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import { logTaskTime } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import type { LogTaskTimeResult, WorkTask } from '../../lib/types'

/**
 * Hours on a task without opening the timesheet: a date (today by default, never
 * after it), hours in half hours and an optional note. The server puts them on the
 * week's draft and refuses a week already submitted; its message shows here.
 */
export default function LogTimeButton({ task, today, onLogged, dense = false }: {
    task: WorkTask
    today: string
    onLogged: (result: LogTaskTimeResult, hours: number, date: string) => void
    dense?: boolean
}) {
    const [anchor, setAnchor] = useState<HTMLElement | null>(null)
    const [date, setDate] = useState(today)
    const [hours, setHours] = useState('1')
    const [notes, setNotes] = useState('')

    const h = Number(hours)
    const hoursValid = Number.isFinite(h) && h >= 0.5 && h <= 24 && Number.isInteger(h * 2)
    const dateValid = /^\d{4}-\d{2}-\d{2}$/.test(date) && date <= today

    const log = useMutation({
        mutationFn: () => logTaskTime(task.id, { date, hours: h, notes: notes.trim() || null }),
        onSuccess: (result) => {
            setAnchor(null)
            onLogged(result, h, date)
        },
    })

    const open = (e: React.MouseEvent<HTMLElement>) => {
        setDate(today); setHours('1'); setNotes(''); log.reset()
        setAnchor(e.currentTarget)
    }

    return (
        <>
            <Box
                component="button"
                type="button"
                onClick={open}
                sx={{
                    display: 'inline-flex', alignItems: 'center', gap: '6px',
                    bgcolor: 'background.paper', color: 'text.primary',
                    border: '1px solid', borderColor: 'divider', borderRadius: '6px', px: dense ? '8px' : '10px', py: '6px',
                    fontSize: 12, fontWeight: 500, cursor: 'pointer', fontFamily: 'inherit', whiteSpace: 'nowrap',
                    '&:hover': { borderColor: 'primary.main', color: 'primary.main' },
                }}
            >
                <Box component="span" aria-hidden sx={{ fontSize: 11 }}>⏱</Box>Log time
            </Box>
            <Popover
                open={anchor != null}
                anchorEl={anchor}
                onClose={() => { if (!log.isPending) setAnchor(null) }}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
                slotProps={{ paper: { sx: { p: 2, width: 300, borderRadius: '10px', mt: '4px' } } }}
            >
                <Stack spacing={1.5} component="form" onSubmit={(e) => { e.preventDefault(); if (hoursValid && dateValid) log.mutate() }}>
                    {log.error && <Alert severity="error" sx={{ fontSize: 12 }}>{getApiErrorMessage(log.error, 'The time could not be logged.')}</Alert>}
                    <Stack direction="row" spacing={1}>
                        <TextField
                            label="Date" type="date" size="small" value={date}
                            onChange={(e) => setDate(e.target.value)}
                            error={!dateValid}
                            slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: today } }}
                            sx={{ flex: 1 }}
                        />
                        <TextField
                            label="Hours" type="number" size="small" value={hours}
                            onChange={(e) => setHours(e.target.value)}
                            error={!hoursValid}
                            slotProps={{ htmlInput: { step: 0.5, min: 0.5, max: 24 } }}
                            sx={{ width: 90 }}
                        />
                    </Stack>
                    <TextField label="Note" size="small" value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="What did you work on?" />
                    <Stack direction="row" spacing={1} justifyContent="flex-end">
                        <Button size="small" onClick={() => setAnchor(null)} disabled={log.isPending}>Cancel</Button>
                        <Button size="small" variant="contained" type="submit" disabled={log.isPending || !hoursValid || !dateValid}>Log</Button>
                    </Stack>
                </Stack>
            </Popover>
        </>
    )
}
```

The popover renders in a portal, but React still bubbles its clicks through the component tree, so `TaskActions`' existing `onClick={(e) => e.stopPropagation()}` keeps them from opening the card's details.

In `client/src/components/tasks/TaskCard.tsx`:

- Import `LogTimeButton` and `canLogTime`; add `LogTaskTimeResult` to the types import.
- Add to `TaskItemProps`:

```ts
    /** The viewer, to tell whether they are on the task (Log time is for assignees). */
    viewerId: string
    /** Hours were logged from the card. */
    onLogged: (result: LogTaskTimeResult, hours: number, date: string) => void
```

- `TaskActions` already receives everything but `today`, `settings` and `onOpen`. Change its `Omit<TaskItemProps, 'today' | 'settings' | 'onOpen'>` to `Omit<TaskItemProps, 'settings' | 'onOpen'>` so it gets `today`, destructure `today, viewerId, onLogged`, and render the button after the status controls, before the spacer:

```tsx
            {canLogTime(task, viewerId) && <LogTimeButton task={task} today={today} onLogged={onLogged} dense={dense} />}
            <Box sx={{ flex: 1 }} />
```

`TaskCard` and `TaskList`'s `TaskRow` already spread every prop into `<TaskActions {...props} … />`, so both get it.

In `client/src/components/tasks/TaskDetailsDialog.tsx`:

- Props gain `viewerId: string` and `onLogged: (result: LogTaskTimeResult, hours: number, date: string) => void`; pass both to `Details`.
- In `DialogActions`, before Close:

```tsx
                {canLogTime(task, viewerId) && (
                    <Box sx={{ mr: 'auto', pl: 1 }}><LogTimeButton task={task} today={today} onLogged={onLogged} /></Box>
                )}
```

In `client/src/components/tasks/TasksPage.tsx`:

- `const { authStore, uiStore } = useStore()`.
- Import `Snackbar` from `@mui/material/Snackbar`, `Button` from `@mui/material/Button`, `describeLoggedTime` from `../../lib/work-tasks`, `LogTaskTimeResult` type.
- State and handler:

```tsx
    // The last log from a card: what to say, and the week to open.
    const [logged, setLogged] = useState<{ text: string; weekStart: string } | null>(null)
    const onLogged = (result: LogTaskTimeResult, hours: number, date: string) => {
        setLogged({ text: describeLoggedTime(hours, date, result), weekStart: result.weekStart })
        void queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
        void queryClient.invalidateQueries({ queryKey: ['timesheets'] })
        void queryClient.invalidateQueries({ queryKey: ['timesheet', result.timesheetId] })
    }
```

`['timesheets']` is a prefix, as the editor's own save uses; it never asks the bell's list for attendance.

- `itemProps` gains `viewerId: userId, onLogged,`.
- `<TaskDetailsDialog … viewerId={userId} onLogged={onLogged} />`.
- Before the closing `</Box>` of the page:

```tsx
            <Snackbar
                open={logged != null}
                autoHideDuration={6000}
                onClose={() => setLogged(null)}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
            >
                <Alert
                    severity="success"
                    variant="filled"
                    onClose={() => setLogged(null)}
                    action={
                        <Button color="inherit" size="small" onClick={() => { const w = logged!.weekStart; setLogged(null); uiStore.navigateToNewTimesheet(w) }}>
                            Open timesheet
                        </Button>
                    }
                    sx={{ fontSize: 13 }}
                >
                    {logged?.text}
                </Alert>
            </Snackbar>
```

An `Alert` with an `action` renders that action instead of its close icon — that is fine; the snackbar auto-hides.

- [ ] **Step 8: Run the tests to verify they pass**

```bash
node_modules/.bin/vitest run src/components/tasks src/lib/work-tasks.test.ts
npm run lint
```

Expected: PASS; lint 0 errors (the 5 known warnings in `TaskSettingsPanel.tsx` and `TaskDialog.tsx` only).

- [ ] **Step 9: Commit**

```bash
git add client/src/lib/types/work-task.ts client/src/lib/api/work-tasks.ts client/src/lib/work-tasks.ts client/src/lib/work-tasks.test.ts client/src/components/tasks/LogTimeButton.tsx client/src/components/tasks/TaskCard.tsx client/src/components/tasks/TaskDetailsDialog.tsx client/src/components/tasks/TasksPage.tsx client/src/components/tasks/TasksPage.test.tsx client/src/components/tasks/TaskDetailsDialog.test.tsx
git commit -m "Tasks: Log time on the card and in the details dialog, with a link to the week

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: CLAUDE.md, full server and client runs, build

**Files:**
- Modify: `CLAUDE.md` (the **Tasks** bullet under Backend Patterns; the `TimesheetEntry` row of the Domain Model table)

- [ ] **Step 1: CLAUDE.md**

In the Tasks bullet, after the sentence that ends `` `TasksPage.test.tsx` and `work-tasks.test.ts` pin it. `` (the Employee's-page paragraph from Phase 1), insert:

```
**Log time from the card** (`LogTaskTime`, `POST /api/worktasks/{id}/log-time`, no gate beyond the class's): an **assignee** of an open task (To Do, In Progress or Awaiting confirmation) with a project logs hours in half hours, 0.5–24, on a date not after the UTC today `TimesheetEntryValidator` uses (`NowUtc` test seam). The week (Monday to Sunday) is the caller's sheet with that `PeriodStart`, created in Draft with their profile's department when missing; Draft and Rejected are written to, Submitted, Approved and Resubmitted refuse (`WeekSubmittedMessage`, 409). The task's entry for that date is added to (notes appended on a new line), otherwise a new one is made with Type, Component and Activity filled by `TimesheetEntryDefaults.SingleOptionsAsync` — the server mirror of Phase 2's `fillSingleOptions`, keep the two in step. `TimesheetEntryValidator` and `TimesheetEntryTaskRule` run as `AddEntry` runs them, and nothing is written until every rule passes. Nobody is emailed. The client offers it as **Log time** (`LogTimeButton`, a popover) on the card's action bar, list rows and the details dialog wherever `canLogTime` holds, and a snackbar (`describeLoggedTime`) links to the week in the editor. `LogTaskTimeTests` and `TasksPage.test.tsx` pin it.
```

In the `TimesheetEntry` row, find `One entry per project **+ type + component** per date (the task is not part of that key).` and replace it with `One entry per project **+ type + component + task** per date — two tasks on one project are two entries (`TimesheetEntryWorkTaskOverlapTests`); two rows with no task still collide.`

Also add the missing full stop the Phase 2 review flagged in the same row: `…so the hours stay **The editor asks…**` becomes `…so the hours stay. **The editor asks…**`.

- [ ] **Step 2: Full server suite**

```bash
DOTNET_ROOT="/c/Program Files/dotnet" DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests --artifacts-path /c/Users/user/AppData/Local/Temp/claude/c--Practice-Own-2026-WorkTrack/04097ed7-82cc-4cd5-8652-a1163cfd7dad/scratchpad/artifacts
```

Expected: every test PASS. The API being run from `dotnet run` locks `API/bin/Debug`; `--artifacts-path` builds elsewhere, so it can stay running.

- [ ] **Step 3: Client batches, lint, build**

From `client/`, one directory per process (a single run exhausts memory and still exits 0):

```bash
for d in src/components/admin src/components/annual-leave src/components/attendance src/components/auth src/components/layout src/components/tasks src/components/timesheet src/lib; do node_modules/.bin/vitest run $d || echo "FAILED: $d"; done
npm run lint
npm run build
```

Expected: no `FAILED:` line; lint 0 errors; build exit 0.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "CLAUDE.md: logging time from the task card, and the task in an entry's identity

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Restart the API**

The running API serves the old code until restarted (see `MEMORY.md`): stop the background `dotnet run --project API` and start it again, then check `http://localhost:5000/health` answers 200 before reporting the work done.

---

## Self-review notes

- **Spec coverage.** 3.1 steps 1–7 → Task 2 (visibility 404, assignee 403, closed 409, AwaitingConfirmation allowed, half-hour and future checks, week found/created/refused, project from the task, single-option fill, add-to-or-create by task and date, validator and task rule as `AddEntry` runs them, `TotalHours`, the result fields); "emails nobody, fans out `notificationsUpdated`" → Task 3. 3.2 → Task 1. 3.3 → Task 4 (button on card and dialog for an assignee of an open task, popover fields and defaults, snackbar wording and link, inline refusal, invalidations, `logTaskTime`). Testing section: every server case listed in the spec is a test in Task 2 or Task 1; the client cases are in Task 4.
- **Deviation from the spec, deliberate.** The spec puts the snackbar's link on "Open timesheet" to `/new-timesheet` for that week; `uiStore.navigateToNewTimesheet(weekStart)` is exactly that route with the week handed over. The spec's result shape listed `{ timesheetId, entryId, hoursOnEntry, hoursOnDay, timesheetStatus }`; this plan adds `task` (so the controller can notify the task's audience and the client could update the card) and `weekStart` (so the link and the message need no client-side week arithmetic).
- **`TimesheetEntryTaskRule` on a new sheet.** The rule finds the owner through the sheet, which does not exist yet; the command skips it for a new sheet because what it asks — the owner is assigned, the task is open, the task's project is the row's — is already established from the task itself. Called out in the code.
- **Type consistency.** `LogTaskTime.Command`/`Handler`, `LogTaskTimeRequest`, `LogTaskTimeResult`, `TimesheetEntryDefaults.SingleOptionsAsync`, the seven messages, `WorkTasksController.LogTime`, `logTaskTime`, `canLogTime`, `describeLoggedTime`, `LogTimeButton`, `viewerId`, `onLogged(result, hours, date)` are named identically everywhere they appear.
- **Review Focus.** 1 → `A_second_log_on_the_same_task_and_day_adds_to_the_entry`. 2 → `A_second_task_on_the_same_project_and_day_gets_its_own_entry` and `Two_tasks_on_the_same_project_and_day_are_two_entries`. 3 → `Writes_to_a_rejected_week` and `Refuses_a_week_already_submitted`. 4 → `Refuses_a_day_past_24_hours_and_leaves_nothing_behind` and `A_refused_first_log_creates_no_sheet`. 5 → `A_sunday_belongs_to_the_week_that_started_the_monday_before`.
- **Known limit, recorded for the reviewer.** "Future" is the UTC calendar day, as everywhere else timesheets are judged. Between local midnight and 03:00 in Cyprus the popover's default date (local today) is refused with `FutureDateMessage`; the editor's own save has the same edge today. Changing it means changing `TimesheetEntryValidator` for every caller, which is outside this phase.
