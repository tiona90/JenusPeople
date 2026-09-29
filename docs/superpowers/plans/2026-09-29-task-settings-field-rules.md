# Task Settings: Field Rules, Confirmation Switch, Attachment Limits — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the System Administrator a Task Settings page, organisation-wide, that decides which task fields are required, optional or hidden, whether an assignee's Done needs confirmation, and what files a task may carry. Managers, HR Administrators and Employees then work within those rules.

**Architecture:** A one-row `WorkTaskSettings` table, seeded by migration with today's behaviour. The task handlers load it through `WorkTaskSettingsStore.LoadAsync`, and a pure `WorkTaskFieldRules` class applies it on create, edit and status change. On the client, `lib/task-settings.ts` mirrors the rules. `TaskSettingsPanel` edits them at `/admin/task-settings`, and the task dialog, card, CSV and attachment picker read them.

**Tech Stack:** ASP.NET Core 10, EF Core (SQL Server; SQLite/in-memory in tests), MediatR, FluentValidation, xUnit. React 19 + TypeScript, MUI 7, React Query, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-29-task-settings-field-rules-design.md`

## Deviations from the spec, decided while planning

These are deliberate. Each is simpler than what the spec says and changes nothing a user sees:

- **Where the required-field checks run.** They run in the create/update handlers (`WorkTaskFieldRules.Check`), not inside `UpsertWorkTaskRequestValidator`. The validator is constructed with `new` by its wrapper validators and has no `AppDbContext`. Every other configuration-reading rule in this codebase (`AttachmentPolicyRule`, `NoticePeriodRule`) is called from the handler too.
- **How the allowed attachment kinds are stored.** They are four bool columns (`AllowImages`, `AllowPdf`, `AllowWord`, `AllowExcel`) rather than a flags enum. The API serialises enums as strings, and a flags value would reach the client as `"Images, Pdf"`.
- **The client query key.** It is `['work-tasks', 'settings']`, so the existing `notificationsUpdated` handler in `App.tsx`, which already invalidates `['work-tasks']`, refreshes it.

## Global Constraints

- Defaults must reproduce today's behaviour exactly:
  - Description, Due date, Target hours, Attachments: `Optional`
  - Project: `Required`
  - Billable: `Required`
  - Priority: shown
  - Confirmation: on
  - Attachment limits: 10 files, 10 MB, all four kinds allowed
- Allowed values per field:
  - Description, Due date, Target hours, Attachments: Required / Optional / Hidden
  - Project: Required / Optional
  - Billable: Required / Hidden
  - Priority: shown / hidden (bool)
- `MaxAttachmentsPerTask` is 1–20. `MaxAttachmentSizeMb` is 1–10. At least one attachment kind must be allowed.
- Title and Department are always required and are not configurable.
- `GET /api/worktasksettings` is open to any signed-in user. `PUT /api/worktasksettings` is System Administrator only.
- `/tasks` stays closed to the System Administrator (`TASK_ROLES` unchanged).
- The seeder does not run on the deployed host, so the settings row must be created by the migration (`HasData`), not by `DbInitializer`.
- An older API with no settings endpoint must read as today's defaults on the client.
- Error messages are exact strings, shared by server and client:
  - `"Description is required."`
  - `"Due date is required."`
  - `"Target hours are required."`
  - `"Project is required."`
  - `"Say whether the task is billable."`
  - `"Attach a file before marking this task done."`
- Run .NET tests with the workarounds in memory:
  - Set `DOTNET_ROOT="C:\Program Files\dotnet"` and `DOTNET_EnableWriteXorExecute=0`.
  - Pass `--artifacts-path` if the API is running and locks `bin/`.
- Run client tests with `client/node_modules/.bin/vitest run <path>`, never bare `npx vitest`, one directory at a time. The full run runs out of memory and still exits 0.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Stage only the files each task names. The working tree has unrelated uncommitted timesheet CSV changes that must not be committed.

## Review Focus

These are input classes the spec implies but no task's tests would otherwise cover. Each now has a test in the task named:

1. **An edit of a task created while a field was Hidden, after it becomes Required.** For example, due date was hidden, then made required, then the task's title is fixed. The edit must be refused with "Due date is required." It must not be silently kept null. (Task 3, `Unhiding_a_field_as_required_holds_old_tasks_to_it`.)
2. **The confirmation switch turned off while a task is waiting and has been sent back before.** The sweep must clear `SentBackReason` and `SentBackAtUtc`, and must email nobody. (Task 2, `Switching_confirmation_off_closes_waiting_tasks`.)
3. **An upload after the admin lowers the size limit or drops a kind.** A 3 MB PDF under a 2 MB limit, or a PNG with Images off, must be refused by the server with StoreFile's own messages. (Task 4.)
4. **A task already holding more files than a newly lowered count.** It keeps its files but accepts no more. (Task 4, `A_task_over_a_lowered_count_keeps_its_files_but_takes_no_more`.)
5. **The client with an older API.** `GET /worktasksettings` returns 404, and the dialog must still offer every field as today, with Project and Billable required. (Task 6, `falls back to today's rules when the settings cannot be read`.)

---

## File Structure

**Server — create**
- `Domain/WorkTaskSettings.cs`: the entity, plus the `FieldRequirement` enum and the limit constants.
- `Persistence/Migrations/<timestamp>_AddWorkTaskSettings.cs`: generated.
- `Application/TaskSettings/WorkTaskSettingsStore.cs`: loads the row, or defaults if there is none.
- `Application/TaskSettings/WorkTaskSettingsDto.cs`: the GET response and PUT body, with mapping.
- `Application/TaskSettings/Queries/GetWorkTaskSettings.cs`
- `Application/TaskSettings/Commands/UpdateWorkTaskSettings.cs`: saves the settings, and runs the confirmation-off sweep.
- `Application/TaskSettings/Validators/UpdateWorkTaskSettingsValidator.cs`
- `Application/WorkTasks/Support/WorkTaskFieldRules.cs`: the required checks, hidden handling and attachment gate.
- `API/Controllers/WorkTaskSettingsController.cs`
- Tests:
  - `Tests/WorkTrack.Tests/WorkTasks/WorkTaskSettingsTests.cs`: storage, API and sweep.
  - `Tests/WorkTrack.Tests/WorkTasks/WorkTaskFieldRuleTests.cs`: create/edit rules.
  - `Tests/WorkTrack.Tests/WorkTasks/WorkTaskCompletionSettingsTests.cs`: confirmation switch, attachment gate and limits.

**Server — modify**
- `Persistence/AppDbContext.cs`: the DbSet, the entity config and `HasData`.
- `Application/WorkTasks/Validators/UpsertWorkTaskRequestValidator.cs`: drop the hard project and billable rules.
- `Application/WorkTasks/Commands/CreateWorkTask.cs`, `UpdateWorkTask.cs`, `UpdateWorkTaskStatus.cs`, `AddWorkTaskAttachment.cs`
- `Application/Files/Commands/StoreFile.cs`: narrowing overrides.
- `Tests/WorkTrack.Tests/WorkTasks/WorkTaskValidatorTests.cs`: the two tests pinning the dropped rules.
- `Tests/WorkTrack.Tests/SystemAdministrationSurfaceTests.cs`

**Client — create**
- `client/src/lib/api/work-task-settings.ts`: the types, API calls, and the `useWorkTaskSettings` hook.
- `client/src/lib/task-settings.ts`: defaults, `fieldRequirementErrors`, `isShown`, `needsAttachmentBeforeDone` and `attachmentLimits`.
- `client/src/lib/task-settings.test.ts`
- `client/src/components/admin/TaskSettingsPanel.tsx`, `TaskSettingsPanel.test.tsx`

**Client — modify**
- `client/src/lib/api/index.ts`
- `client/src/components/admin/index.ts`
- `client/src/components/annual-leave/DashboardHome.tsx`: the section route.
- `client/src/components/layout/Sidebar.tsx`, `Topbar.tsx`: the entry and the page title.
- `client/src/lib/task-attachments.ts`, `client/src/components/tasks/TaskAttachments.tsx`: the limits become parameters.
- `client/src/components/tasks/TaskDialog.tsx`, `TaskDialog.test.tsx`
- `client/src/lib/work-tasks.ts`: `tasksToCsv` takes the settings.
- `client/src/components/tasks/TasksPage.tsx`, `TasksPage.test.tsx`
- `CLAUDE.md`

---

### Task 1: `WorkTaskSettings` entity, migration and loader

**Files:**
- Create: `Domain/WorkTaskSettings.cs`
- Create: `Application/TaskSettings/WorkTaskSettingsStore.cs`
- Modify: `Persistence/AppDbContext.cs` (DbSet near line 49; entity config after the `WorkTaskAttachment` block, around line 628)
- Create (generated): `Persistence/Migrations/<timestamp>_AddWorkTaskSettings.cs`
- Test: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskSettingsTests.cs`

**Interfaces:**
- Produces: `Domain.FieldRequirement { Optional = 0, Required = 1, Hidden = 2 }`, and `Domain.WorkTaskSettings` with these properties and constants:
  - `int Id`
  - the six `FieldRequirement` properties: `DescriptionRequirement`, `DueDateRequirement`, `TargetHoursRequirement`, `AttachmentsRequirement`, `ProjectRequirement`, `BillableRequirement`
  - `bool ShowPriority`, `bool RequireCompletionConfirmation`
  - `int MaxAttachmentsPerTask`, `int MaxAttachmentSizeMb`
  - `bool AllowImages`, `bool AllowPdf`, `bool AllowWord`, `bool AllowExcel`
  - constants `SingletonId = 1`, `MaxAttachmentsCeiling = 20`, `MaxAttachmentSizeMbCeiling = 10`
- Produces: `Application.TaskSettings.WorkTaskSettingsStore.LoadAsync(AppDbContext context, CancellationToken ct) : Task<WorkTaskSettings>`. It returns the untracked row, or `new WorkTaskSettings()` (the defaults) when the table is empty, which is what the in-memory `TestDb` has.

- [ ] **Step 1: Write the failing test**

`Tests/WorkTrack.Tests/WorkTasks/WorkTaskSettingsTests.cs`:

```csharp
using Application.TaskSettings;
using Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// The System Administrator's Task Settings: one row, created by the migration with
/// today's behaviour, so nothing changes until somebody edits it.
/// </summary>
public class WorkTaskSettingsTests
{
    [Fact]
    public async Task The_database_starts_with_one_row_of_todays_behaviour()
    {
        await using var db = await TransactionalTestDb.CreateAsync();

        var row = Assert.Single(await db.WorkTaskSettings.AsNoTracking().ToListAsync());

        Assert.Equal(WorkTaskSettings.SingletonId, row.Id);
        Assert.Equal(FieldRequirement.Optional, row.DescriptionRequirement);
        Assert.Equal(FieldRequirement.Optional, row.DueDateRequirement);
        Assert.Equal(FieldRequirement.Optional, row.TargetHoursRequirement);
        Assert.Equal(FieldRequirement.Optional, row.AttachmentsRequirement);
        Assert.Equal(FieldRequirement.Required, row.ProjectRequirement);
        Assert.Equal(FieldRequirement.Required, row.BillableRequirement);
        Assert.True(row.ShowPriority);
        Assert.True(row.RequireCompletionConfirmation);
        Assert.Equal(10, row.MaxAttachmentsPerTask);
        Assert.Equal(10, row.MaxAttachmentSizeMb);
        Assert.True(row.AllowImages && row.AllowPdf && row.AllowWord && row.AllowExcel);
    }

    [Fact]
    public async Task With_no_row_the_loader_reads_the_defaults()
    {
        await using var db = TestDb.Create(); // in-memory: HasData is not applied without EnsureCreated

        var settings = await WorkTaskSettingsStore.LoadAsync(db, CancellationToken.None);

        Assert.Equal(FieldRequirement.Required, settings.ProjectRequirement);
        Assert.True(settings.RequireCompletionConfirmation);
        Assert.Equal(10, settings.MaxAttachmentsPerTask);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskSettingsTests"`
Expected: build FAIL, because `WorkTaskSettings`, `FieldRequirement` and `AppDbContext.WorkTaskSettings` don't exist yet.

- [ ] **Step 3: Write the entity**

`Domain/WorkTaskSettings.cs`:

```csharp
namespace Domain;

/// <summary>How a configurable task field is treated on the task dialog.</summary>
public enum FieldRequirement
{
    Optional = 0,
    Required = 1,
    Hidden = 2,
}

/// <summary>
/// The System Administrator's organisation-wide rules for tasks: which fields are
/// asked, whether an assignee's Done waits for confirmation, and what files a task may
/// carry. One row (<see cref="SingletonId"/>), created by migration AddWorkTaskSettings
/// with today's behaviour — the seeder does not run on the deployed host. Title and
/// Department are always required and are not here.
/// </summary>
public class WorkTaskSettings
{
    public const int SingletonId = 1;
    public const int MaxAttachmentsCeiling = 20;
    /// <summary>StoreFile's ceiling for a task attachment; the setting may only narrow it.</summary>
    public const int MaxAttachmentSizeMbCeiling = 10;

    public int Id { get; set; } = SingletonId;

    public FieldRequirement DescriptionRequirement { get; set; } = FieldRequirement.Optional;
    public FieldRequirement DueDateRequirement { get; set; } = FieldRequirement.Optional;
    public FieldRequirement TargetHoursRequirement { get; set; } = FieldRequirement.Optional;
    /// <summary>Required gates completion, not creation: the create dialog uploads after the task exists.</summary>
    public FieldRequirement AttachmentsRequirement { get; set; } = FieldRequirement.Optional;
    /// <summary>Required or Optional only: the timesheet's Task column sets a row's project from the task.</summary>
    public FieldRequirement ProjectRequirement { get; set; } = FieldRequirement.Required;
    /// <summary>Required or Hidden only: a yes/no answer is asked or not asked.</summary>
    public FieldRequirement BillableRequirement { get; set; } = FieldRequirement.Required;
    /// <summary>Priority always carries a value; this only decides whether it is shown.</summary>
    public bool ShowPriority { get; set; } = true;

    public bool RequireCompletionConfirmation { get; set; } = true;

    public int MaxAttachmentsPerTask { get; set; } = 10;
    public int MaxAttachmentSizeMb { get; set; } = MaxAttachmentSizeMbCeiling;
    public bool AllowImages { get; set; } = true;
    public bool AllowPdf { get; set; } = true;
    public bool AllowWord { get; set; } = true;
    public bool AllowExcel { get; set; } = true;
}
```

- [ ] **Step 4: Register it in `AppDbContext`**

Add the DbSet after `WorkTaskAttachments` (line 49):

```csharp
    public DbSet<WorkTaskSettings> WorkTaskSettings { get; set; }
```

Add this after the `builder.Entity<WorkTaskAttachment>(...)` block:

```csharp
        builder.Entity<WorkTaskSettings>(entity =>
        {
            // One row, and the migration inserts it: the seeder does not run on the deployed host.
            entity.Property(s => s.Id).ValueGeneratedNever();
            entity.HasData(new WorkTaskSettings { Id = WorkTaskSettings.SingletonId });
        });
```

- [ ] **Step 5: Write the loader**

`Application/TaskSettings/WorkTaskSettingsStore.cs`:

```csharp
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.TaskSettings;

/// <summary>
/// Reads the Task Settings row. A database without one (the in-memory test provider,
/// which never runs HasData) reads as the defaults — today's behaviour — rather than
/// failing every task write.
/// </summary>
public static class WorkTaskSettingsStore
{
    public static async Task<WorkTaskSettings> LoadAsync(AppDbContext context, CancellationToken cancellationToken) =>
        await context.WorkTaskSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new WorkTaskSettings();
}
```

- [ ] **Step 6: Generate the migration**

The API may be running. Per memory, build with the Migrate configuration so the `bin/Debug` lock doesn't matter:

```bash
dotnet build API -c Migrate
dotnet ef migrations add AddWorkTaskSettings --project Persistence --startup-project API --configuration Migrate --no-build
```

Open the generated file. Confirm it creates table `WorkTaskSettings` and calls `migrationBuilder.InsertData` with `Id = 1` and the default values: requirements `0`/`1`, `ShowPriority` true, `RequireCompletionConfirmation` true, `MaxAttachmentsPerTask` 10, `MaxAttachmentSizeMb` 10, and all four `Allow*` flags true. If `InsertData` is missing, `HasData` wasn't picked up. Fix Step 4 before continuing.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskSettingsTests"`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add Domain/WorkTaskSettings.cs Application/TaskSettings/WorkTaskSettingsStore.cs Persistence/AppDbContext.cs Persistence/Migrations/*AddWorkTaskSettings* Persistence/Migrations/AppDbContextModelSnapshot.cs Tests/WorkTrack.Tests/WorkTasks/WorkTaskSettingsTests.cs
git commit -m "Add the Task Settings row, seeded with today's behaviour

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Settings API, validation, and the confirmation-off sweep

**Files:**
- Create: `Application/TaskSettings/WorkTaskSettingsDto.cs`
- Create: `Application/TaskSettings/Queries/GetWorkTaskSettings.cs`
- Create: `Application/TaskSettings/Commands/UpdateWorkTaskSettings.cs`
- Create: `Application/TaskSettings/Validators/UpdateWorkTaskSettingsValidator.cs`
- Create: `API/Controllers/WorkTaskSettingsController.cs`
- Modify: `Tests/WorkTrack.Tests/SystemAdministrationSurfaceTests.cs`: add one `[InlineData]` to the System-Administrator-only theory (around line 54), plus one new fact.
- Test: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskSettingsTests.cs` (extend)

**Interfaces:**
- Consumes: `WorkTaskSettings`, `FieldRequirement` and `WorkTaskSettingsStore.LoadAsync` (Task 1).
- Produces: `WorkTaskSettingsDto`, with the same property names and types as the entity minus `Id`. It has `static WorkTaskSettingsDto From(WorkTaskSettings s)` and `void ApplyTo(WorkTaskSettings s)`.
- Produces: `GetWorkTaskSettings.Query : IRequest<Result<WorkTaskSettingsDto>>`.
- Produces: `UpdateWorkTaskSettings.Command { WorkTaskSettingsDto Settings; DateTime? NowUtc } : IRequest<Result<WorkTaskSettingsDto>>`.
- Produces: routes `GET /api/worktasksettings` (`[Authorize]`) and `PUT /api/worktasksettings` (`[Authorize(Roles = AppRoles.SystemAdministrator)]`), with actions `GetSettings` and `UpdateSettings`.

- [ ] **Step 1: Write the failing tests**

Add these to `WorkTaskSettingsTests.cs`. Add usings: `Application.Core`, `Application.TaskSettings.Commands`, `Application.TaskSettings.Validators`, `Application.TaskSettings`, `static WorkTrack.Tests.WorkTasks.WorkTaskWorld`.

```csharp
    private static Task<Result<WorkTaskSettingsDto>> Save(Persistence.AppDbContext db, WorkTaskSettingsDto dto) =>
        new UpdateWorkTaskSettings.Handler(db).Handle(
            new UpdateWorkTaskSettings.Command { Settings = dto, NowUtc = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc) },
            CancellationToken.None);

    private static WorkTaskSettingsDto Defaults() => WorkTaskSettingsDto.From(new WorkTaskSettings());

    [Fact]
    public async Task Saving_replaces_every_setting()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        var dto = Defaults();
        dto.DueDateRequirement = FieldRequirement.Required;
        dto.BillableRequirement = FieldRequirement.Hidden;
        dto.MaxAttachmentsPerTask = 3;
        dto.AllowExcel = false;

        var result = await Save(db, dto);

        Assert.True(result.IsSuccess, result.Error);
        var row = await db.WorkTaskSettings.AsNoTracking().SingleAsync();
        Assert.Equal(FieldRequirement.Required, row.DueDateRequirement);
        Assert.Equal(FieldRequirement.Hidden, row.BillableRequirement);
        Assert.Equal(3, row.MaxAttachmentsPerTask);
        Assert.False(row.AllowExcel);
    }

    [Theory]
    [InlineData(nameof(WorkTaskSettingsDto.ProjectRequirement), FieldRequirement.Hidden)]
    [InlineData(nameof(WorkTaskSettingsDto.BillableRequirement), FieldRequirement.Optional)]
    public void A_field_refuses_a_value_outside_its_set(string field, FieldRequirement value)
    {
        var dto = Defaults();
        typeof(WorkTaskSettingsDto).GetProperty(field)!.SetValue(dto, value);

        var result = new UpdateWorkTaskSettingsValidator().Validate(new UpdateWorkTaskSettings.Command { Settings = dto });

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(21, 10, true)]
    [InlineData(10, 0, true)]
    [InlineData(10, 11, true)]
    [InlineData(10, 10, false)] // no kind allowed
    public void Limits_are_bounded(int count, int mb, bool kinds)
    {
        var dto = Defaults();
        dto.MaxAttachmentsPerTask = count;
        dto.MaxAttachmentSizeMb = mb;
        if (!kinds) dto.AllowImages = dto.AllowPdf = dto.AllowWord = dto.AllowExcel = false;

        var result = new UpdateWorkTaskSettingsValidator().Validate(new UpdateWorkTaskSettings.Command { Settings = dto });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Switching_confirmation_off_closes_waiting_tasks()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var waiting = NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation);
        waiting.SentBackReason = "Missing the totals";
        waiting.SentBackAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc);
        var open = NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress);
        db.WorkTasks.AddRange(waiting, open);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var dto = Defaults();
        dto.RequireCompletionConfirmation = false;

        var result = await Save(db, dto);

        Assert.True(result.IsSuccess, result.Error);
        var closed = await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == waiting.Id);
        Assert.Equal(WorkTaskStatus.Done, closed.Status);
        Assert.Equal(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), closed.CompletedAtUtc);
        Assert.Null(closed.ConfirmedById);
        Assert.Null(closed.SentBackReason);
        Assert.Null(closed.SentBackAtUtc);
        Assert.Equal(WorkTaskStatus.InProgress, (await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == open.Id)).Status);
    }

    [Fact]
    public async Task Switching_confirmation_on_moves_nothing()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var row = await db.WorkTaskSettings.SingleAsync();
        row.RequireCompletionConfirmation = false;
        var done = NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.Done);
        db.WorkTasks.Add(done);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Save(db, Defaults()); // confirmation back on

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.Done, (await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == done.Id)).Status);
    }
```

Add these to `SystemAdministrationSurfaceTests.cs`. Add an `[InlineData]` to the System-Administrator-only theory, beside the `SettingsController` rows:

```csharp
    [InlineData(typeof(WorkTaskSettingsController), nameof(WorkTaskSettingsController.UpdateSettings))]
```

Add this fact at the end of the class:

```csharp
    /// <summary>The task dialog of every role reads the rules; only the System Administrator writes them.</summary>
    [Fact]
    public void Task_settings_are_read_by_anyone_signed_in()
    {
        var gates = GatesOn(typeof(WorkTaskSettingsController), nameof(WorkTaskSettingsController.GetSettings)).ToList();

        Assert.NotEmpty(gates);
        Assert.All(gates, g => Assert.Null(g.Roles));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskSettingsTests|FullyQualifiedName~SystemAdministrationSurfaceTests"`
Expected: build FAIL, because `WorkTaskSettingsDto`, `UpdateWorkTaskSettings` and `WorkTaskSettingsController` don't exist yet.

- [ ] **Step 3: Write the DTO**

`Application/TaskSettings/WorkTaskSettingsDto.cs`:

```csharp
using Domain;

namespace Application.TaskSettings;

/// <summary>The Task Settings as read and as saved — a full replace, like every other settings save.</summary>
public class WorkTaskSettingsDto
{
    public FieldRequirement DescriptionRequirement { get; set; }
    public FieldRequirement DueDateRequirement { get; set; }
    public FieldRequirement TargetHoursRequirement { get; set; }
    public FieldRequirement AttachmentsRequirement { get; set; }
    public FieldRequirement ProjectRequirement { get; set; }
    public FieldRequirement BillableRequirement { get; set; }
    public bool ShowPriority { get; set; }
    public bool RequireCompletionConfirmation { get; set; }
    public int MaxAttachmentsPerTask { get; set; }
    public int MaxAttachmentSizeMb { get; set; }
    public bool AllowImages { get; set; }
    public bool AllowPdf { get; set; }
    public bool AllowWord { get; set; }
    public bool AllowExcel { get; set; }

    public static WorkTaskSettingsDto From(WorkTaskSettings s) => new()
    {
        DescriptionRequirement = s.DescriptionRequirement,
        DueDateRequirement = s.DueDateRequirement,
        TargetHoursRequirement = s.TargetHoursRequirement,
        AttachmentsRequirement = s.AttachmentsRequirement,
        ProjectRequirement = s.ProjectRequirement,
        BillableRequirement = s.BillableRequirement,
        ShowPriority = s.ShowPriority,
        RequireCompletionConfirmation = s.RequireCompletionConfirmation,
        MaxAttachmentsPerTask = s.MaxAttachmentsPerTask,
        MaxAttachmentSizeMb = s.MaxAttachmentSizeMb,
        AllowImages = s.AllowImages,
        AllowPdf = s.AllowPdf,
        AllowWord = s.AllowWord,
        AllowExcel = s.AllowExcel,
    };

    public void ApplyTo(WorkTaskSettings s)
    {
        s.DescriptionRequirement = DescriptionRequirement;
        s.DueDateRequirement = DueDateRequirement;
        s.TargetHoursRequirement = TargetHoursRequirement;
        s.AttachmentsRequirement = AttachmentsRequirement;
        s.ProjectRequirement = ProjectRequirement;
        s.BillableRequirement = BillableRequirement;
        s.ShowPriority = ShowPriority;
        s.RequireCompletionConfirmation = RequireCompletionConfirmation;
        s.MaxAttachmentsPerTask = MaxAttachmentsPerTask;
        s.MaxAttachmentSizeMb = MaxAttachmentSizeMb;
        s.AllowImages = AllowImages;
        s.AllowPdf = AllowPdf;
        s.AllowWord = AllowWord;
        s.AllowExcel = AllowExcel;
    }
}
```

- [ ] **Step 4: Write the query, command and validator**

`Application/TaskSettings/Queries/GetWorkTaskSettings.cs`:

```csharp
using Application.Core;
using MediatR;
using Persistence;

namespace Application.TaskSettings.Queries;

public class GetWorkTaskSettings
{
    public class Query : IRequest<Result<WorkTaskSettingsDto>>;

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<WorkTaskSettingsDto>>
    {
        public async Task<Result<WorkTaskSettingsDto>> Handle(Query request, CancellationToken cancellationToken) =>
            Result<WorkTaskSettingsDto>.Success(WorkTaskSettingsDto.From(await WorkTaskSettingsStore.LoadAsync(context, cancellationToken)));
    }
}
```

`Application/TaskSettings/Commands/UpdateWorkTaskSettings.cs`:

```csharp
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.TaskSettings.Commands;

/// <summary>
/// The System Administrator's save of the Task Settings — a full replace. Switching
/// confirmation off closes every task waiting for it in the same save (Done, no
/// confirmer, the send-back note cleared) and emails nobody, the way UpdateLeaveType
/// sweeps leave in flight: a task must not wait on a step that no longer exists.
/// Switching it on moves nothing.
/// </summary>
public class UpdateWorkTaskSettings
{
    public class Command : IRequest<Result<WorkTaskSettingsDto>>
    {
        public WorkTaskSettingsDto Settings { get; set; } = new();

        /// <summary>Test seam for the sweep's completion time; the controller leaves it null.</summary>
        public DateTime? NowUtc { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<WorkTaskSettingsDto>>
    {
        public async Task<Result<WorkTaskSettingsDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var row = await context.WorkTaskSettings.FirstOrDefaultAsync(cancellationToken);
            if (row is null)
            {
                row = new WorkTaskSettings();
                context.WorkTaskSettings.Add(row);
            }

            var confirmationDropped = row.RequireCompletionConfirmation && !request.Settings.RequireCompletionConfirmation;
            request.Settings.ApplyTo(row);

            if (confirmationDropped)
            {
                var now = request.NowUtc ?? DateTime.UtcNow;
                var waiting = await context.WorkTasks
                    .Where(t => t.Status == WorkTaskStatus.AwaitingConfirmation)
                    .ToListAsync(cancellationToken);
                foreach (var task in waiting)
                {
                    task.Status = WorkTaskStatus.Done;
                    task.CompletedAtUtc = now;
                    task.ConfirmedById = null;
                    task.SentBackReason = null;
                    task.SentBackAtUtc = null;
                    task.UpdatedAtUtc = now;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            return Result<WorkTaskSettingsDto>.Success(WorkTaskSettingsDto.From(row));
        }
    }
}
```

`Application/TaskSettings/Validators/UpdateWorkTaskSettingsValidator.cs`:

```csharp
using Application.TaskSettings.Commands;
using Domain;
using FluentValidation;

namespace Application.TaskSettings.Validators;

public class UpdateWorkTaskSettingsValidator : AbstractValidator<UpdateWorkTaskSettings.Command>
{
    public UpdateWorkTaskSettingsValidator()
    {
        RuleFor(x => x.Settings).NotNull();
        RuleFor(x => x.Settings.DescriptionRequirement).IsInEnum();
        RuleFor(x => x.Settings.DueDateRequirement).IsInEnum();
        RuleFor(x => x.Settings.TargetHoursRequirement).IsInEnum();
        RuleFor(x => x.Settings.AttachmentsRequirement).IsInEnum();
        // The timesheet's Task column sets a row's project from the task.
        RuleFor(x => x.Settings.ProjectRequirement)
            .Must(r => r is FieldRequirement.Required or FieldRequirement.Optional)
            .WithMessage("Project can be required or optional, not hidden.");
        // Billable is a yes/no answer: asked, or not asked.
        RuleFor(x => x.Settings.BillableRequirement)
            .Must(r => r is FieldRequirement.Required or FieldRequirement.Hidden)
            .WithMessage("Billable can be required or hidden, not optional.");
        RuleFor(x => x.Settings.MaxAttachmentsPerTask)
            .InclusiveBetween(1, WorkTaskSettings.MaxAttachmentsCeiling)
            .WithMessage($"A task can be allowed 1 to {WorkTaskSettings.MaxAttachmentsCeiling} attachments.");
        RuleFor(x => x.Settings.MaxAttachmentSizeMb)
            .InclusiveBetween(1, WorkTaskSettings.MaxAttachmentSizeMbCeiling)
            .WithMessage($"The size limit must be 1 to {WorkTaskSettings.MaxAttachmentSizeMbCeiling} MB.");
        RuleFor(x => x.Settings)
            .Must(s => s.AllowImages || s.AllowPdf || s.AllowWord || s.AllowExcel)
            .WithMessage("Allow at least one kind of file.");
    }
}
```

- [ ] **Step 5: Write the controller**

`API/Controllers/WorkTaskSettingsController.cs`:

```csharp
using API.Hubs;
using Application.TaskSettings;
using Application.TaskSettings.Commands;
using Application.TaskSettings.Queries;
using Asp.Versioning;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace API.Controllers;

/// <summary>
/// The Task Settings. Every role's task dialog reads them; only the System
/// Administrator — who configures the workspace and does not see the tasks — writes them.
/// </summary>
[ApiVersion("1.0")]
public class WorkTaskSettingsController(IHubContext<NotificationsHub> notificationsHub) : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<WorkTaskSettingsDto>> GetSettings() =>
        HandleResult(await Mediator.Send(new GetWorkTaskSettings.Query()));

    [HttpPut]
    [Authorize(Roles = AppRoles.SystemAdministrator)]
    public async Task<ActionResult<WorkTaskSettingsDto>> UpdateSettings([FromBody] WorkTaskSettingsDto settings, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new UpdateWorkTaskSettings.Command { Settings = settings }, cancellationToken);
        // Rare, and it can close waiting tasks: every open task page refetches.
        if (result.IsSuccess)
            await notificationsHub.Clients.All.SendAsync("notificationsUpdated", cancellationToken);
        return HandleResult(result);
    }
}
```

Check how `BaseApiController` derives the route. Other controllers such as `SettingsController` get `/api/settings` from their class name, so this one gets `/api/worktasksettings`. If `Result` has no `IsSuccess` property, use whatever name `WorkTaskReviewTests` uses. It uses `result.IsSuccess`, so the name matches.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskSettingsTests|FullyQualifiedName~SystemAdministrationSurfaceTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Application/TaskSettings API/Controllers/WorkTaskSettingsController.cs Tests/WorkTrack.Tests/WorkTasks/WorkTaskSettingsTests.cs Tests/WorkTrack.Tests/SystemAdministrationSurfaceTests.cs
git commit -m "Let the System Administrator save Task Settings; switching confirmation off closes waiting tasks

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Field rules on create and edit

**Files:**
- Create: `Application/WorkTasks/Support/WorkTaskFieldRules.cs`
- Modify: `Application/WorkTasks/Validators/UpsertWorkTaskRequestValidator.cs`: the `ProjectId` and `IsBillable` rules.
- Modify: `Application/WorkTasks/Commands/CreateWorkTask.cs`, `Application/WorkTasks/Commands/UpdateWorkTask.cs`
- Modify: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskValidatorTests.cs`: the tests at lines ~44 (`ProjectId = null`) and ~87 (`IsBillable = null`).
- Test: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskFieldRuleTests.cs`

**Interfaces:**
- Consumes: `WorkTaskSettings`, `FieldRequirement` and `WorkTaskSettingsStore.LoadAsync` (Task 1).
- Produces: `static class WorkTaskFieldRules`, with the six message constants from the Global Constraints (`DescriptionRequiredMessage`, `DueDateRequiredMessage`, `TargetHoursRequiredMessage`, `ProjectRequiredMessage`, `BillableRequiredMessage`, `AttachmentRequiredMessage`) and three methods:
  - `string? Check(WorkTaskSettings s, UpsertWorkTaskRequest input)`
  - `void ApplyHiddenOnCreate(WorkTaskSettings s, UpsertWorkTaskRequest input)`
  - `void KeepHiddenOnEdit(WorkTaskSettings s, UpsertWorkTaskRequest input, WorkTask stored)`
- `Check` returns the first failing message, or null. The order is Project, Billable, Description, Due date, Target hours. It never checks a Hidden field.
- `ApplyHiddenOnCreate` blanks hidden fields in `input` in place: Description, DueDate, TargetHours and IsBillable become null, and Priority becomes `Normal`.
- `KeepHiddenOnEdit` copies the stored value into `input` for each hidden field.

- [ ] **Step 1: Write the failing tests**

`Tests/WorkTrack.Tests/WorkTasks/WorkTaskFieldRuleTests.cs`:

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
/// The System Administrator decides which task fields are asked. A required field is
/// refused blank on create and on edit; a hidden one is not asked, stored as nothing on
/// create, and left as it was on edit — nobody can see it to clear it.
/// </summary>
public class WorkTaskFieldRuleTests
{
    private readonly FakeEmailService _email = new();

    private Task<Result<WorkTaskDto>> Create(AppDbContext db, UpsertWorkTaskRequest task) =>
        new CreateWorkTask.Handler(db, _email, NullLogger<CreateWorkTask.Handler>.Instance)
            .Handle(new CreateWorkTask.Command { CallerUserId = Hr, Task = task }, CancellationToken.None);

    private Task<Result<WorkTaskDto>> Update(AppDbContext db, int id, UpsertWorkTaskRequest task) =>
        new UpdateWorkTask.Handler(db, _email, NullLogger<UpdateWorkTask.Handler>.Instance)
            .Handle(new UpdateWorkTask.Command { Id = id, CallerUserId = Hr, Task = task }, CancellationToken.None);

    private static UpsertWorkTaskRequest Request() => new()
    {
        Title = "Chase notes",
        DepartmentId = Sales,
        ProjectId = SalesProject,
        AssigneeIds = [SalesManager],
        Priority = WorkTaskPriority.High,
        IsBillable = true,
    };

    private static async Task Configure(AppDbContext db, Action<WorkTaskSettings> change)
    {
        var row = await db.WorkTaskSettings.SingleAsync();
        change(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData("Description")]
    [InlineData("DueDate")]
    [InlineData("TargetHours")]
    public async Task A_required_field_is_refused_blank(string field)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s =>
        {
            if (field == "Description") s.DescriptionRequirement = FieldRequirement.Required;
            if (field == "DueDate") s.DueDateRequirement = FieldRequirement.Required;
            if (field == "TargetHours") s.TargetHoursRequirement = FieldRequirement.Required;
        });

        var result = await Create(db, Request());

        var expected = field switch
        {
            "Description" => WorkTaskFieldRules.DescriptionRequiredMessage,
            "DueDate" => WorkTaskFieldRules.DueDateRequiredMessage,
            _ => WorkTaskFieldRules.TargetHoursRequiredMessage,
        };
        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public async Task Whitespace_is_not_a_description()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.DescriptionRequirement = FieldRequirement.Required);
        var request = Request();
        request.Description = "   ";

        Assert.Equal(WorkTaskFieldRules.DescriptionRequiredMessage, (await Create(db, request)).Error);
    }

    [Fact]
    public async Task An_optional_project_may_be_left_out()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.ProjectRequirement = FieldRequirement.Optional);
        var request = Request();
        request.ProjectId = null;

        var result = await Create(db, request);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.ProjectId);
    }

    [Fact]
    public async Task A_required_project_is_refused_blank()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var request = Request();
        request.ProjectId = null;

        Assert.Equal(WorkTaskFieldRules.ProjectRequiredMessage, (await Create(db, request)).Error);
    }

    [Fact]
    public async Task A_required_billable_is_refused_blank()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var request = Request();
        request.IsBillable = null;

        Assert.Equal(WorkTaskFieldRules.BillableRequiredMessage, (await Create(db, request)).Error);
    }

    [Fact]
    public async Task A_hidden_field_is_stored_as_nothing_on_create()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s =>
        {
            s.DescriptionRequirement = FieldRequirement.Hidden;
            s.DueDateRequirement = FieldRequirement.Hidden;
            s.TargetHoursRequirement = FieldRequirement.Hidden;
            s.BillableRequirement = FieldRequirement.Hidden;
            s.ShowPriority = false;
        });
        var request = Request();
        request.Description = "sneaked in";
        request.DueDate = new DateOnly(2026, 10, 1);
        request.TargetHours = 8;

        var result = await Create(db, request);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await db.WorkTasks.AsNoTracking().SingleAsync();
        Assert.Null(stored.Description);
        Assert.Null(stored.DueDate);
        Assert.Null(stored.TargetHours);
        Assert.Null(stored.IsBillable);
        Assert.Equal(WorkTaskPriority.Normal, stored.Priority);
    }

    [Fact]
    public async Task A_hidden_field_survives_an_edit()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, SalesManager);
        task.Description = "Keep me";
        task.DueDate = new DateOnly(2026, 10, 1);
        task.TargetHours = 16;
        task.IsBillable = true;
        task.Priority = WorkTaskPriority.High;
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Configure(db, s =>
        {
            s.DescriptionRequirement = FieldRequirement.Hidden;
            s.DueDateRequirement = FieldRequirement.Hidden;
            s.TargetHoursRequirement = FieldRequirement.Hidden;
            s.BillableRequirement = FieldRequirement.Hidden;
            s.ShowPriority = false;
        });
        // What the dialog sends with those fields hidden: nothing for them.
        var request = Request();
        request.Title = "Typo fixed";
        request.Description = null;
        request.DueDate = null;
        request.TargetHours = null;
        request.IsBillable = null;
        request.Priority = WorkTaskPriority.Normal;

        var result = await Update(db, task.Id, request);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal("Typo fixed", stored.Title);
        Assert.Equal("Keep me", stored.Description);
        Assert.Equal(new DateOnly(2026, 10, 1), stored.DueDate);
        Assert.Equal(16, stored.TargetHours);
        Assert.True(stored.IsBillable);
        Assert.Equal(WorkTaskPriority.High, stored.Priority);
    }

    [Fact]
    public async Task Unhiding_a_field_as_required_holds_old_tasks_to_it()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, SalesManager);
        task.IsBillable = true;
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Configure(db, s => s.DueDateRequirement = FieldRequirement.Required);
        var request = Request();
        request.Title = "Typo fixed";

        var result = await Update(db, task.Id, request);

        Assert.Equal(WorkTaskFieldRules.DueDateRequiredMessage, result.Error);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskFieldRuleTests"`
Expected: build FAIL, because `WorkTaskFieldRules` doesn't exist yet.

- [ ] **Step 3: Write `WorkTaskFieldRules`**

`Application/WorkTasks/Support/WorkTaskFieldRules.cs`:

```csharp
using Application.WorkTasks.DTOs;
using Domain;

namespace Application.WorkTasks.Support;

/// <summary>
/// The Task Settings' field rules, applied by CreateWorkTask and UpdateWorkTask.
/// Mirrored by client/src/lib/task-settings.ts — keep the messages identical.
///
/// A hidden field is the one deliberate exception to UpsertWorkTaskRequest being a
/// full replace: on edit the stored value is kept whatever the request carries,
/// because nobody can see the field to clear it, and hiding then un-hiding a field
/// must lose nothing. On create a hidden field is stored as nothing.
/// </summary>
public static class WorkTaskFieldRules
{
    public const string DescriptionRequiredMessage = "Description is required.";
    public const string DueDateRequiredMessage = "Due date is required.";
    public const string TargetHoursRequiredMessage = "Target hours are required.";
    public const string ProjectRequiredMessage = "Project is required.";
    public const string BillableRequiredMessage = "Say whether the task is billable.";
    public const string AttachmentRequiredMessage = "Attach a file before marking this task done.";

    public static string? Check(WorkTaskSettings s, UpsertWorkTaskRequest input)
    {
        if (s.ProjectRequirement == FieldRequirement.Required && input.ProjectId is null) return ProjectRequiredMessage;
        if (s.BillableRequirement == FieldRequirement.Required && input.IsBillable is null) return BillableRequiredMessage;
        if (s.DescriptionRequirement == FieldRequirement.Required && string.IsNullOrWhiteSpace(input.Description)) return DescriptionRequiredMessage;
        if (s.DueDateRequirement == FieldRequirement.Required && input.DueDate is null) return DueDateRequiredMessage;
        if (s.TargetHoursRequirement == FieldRequirement.Required && input.TargetHours is null) return TargetHoursRequiredMessage;
        return null;
    }

    public static void ApplyHiddenOnCreate(WorkTaskSettings s, UpsertWorkTaskRequest input)
    {
        if (s.DescriptionRequirement == FieldRequirement.Hidden) input.Description = null;
        if (s.DueDateRequirement == FieldRequirement.Hidden) input.DueDate = null;
        if (s.TargetHoursRequirement == FieldRequirement.Hidden) input.TargetHours = null;
        if (s.BillableRequirement == FieldRequirement.Hidden) input.IsBillable = null;
        if (!s.ShowPriority) input.Priority = WorkTaskPriority.Normal;
    }

    public static void KeepHiddenOnEdit(WorkTaskSettings s, UpsertWorkTaskRequest input, WorkTask stored)
    {
        if (s.DescriptionRequirement == FieldRequirement.Hidden) input.Description = stored.Description;
        if (s.DueDateRequirement == FieldRequirement.Hidden) input.DueDate = stored.DueDate;
        if (s.TargetHoursRequirement == FieldRequirement.Hidden) input.TargetHours = stored.TargetHours;
        if (s.BillableRequirement == FieldRequirement.Hidden) input.IsBillable = stored.IsBillable;
        if (!s.ShowPriority) input.Priority = stored.Priority;
    }

    /// <summary>A task under a required-attachment rule cannot be closed without a file.</summary>
    public static bool NeedsAttachment(WorkTaskSettings s, int attachmentCount) =>
        s.AttachmentsRequirement == FieldRequirement.Required && attachmentCount == 0;
}
```

Check the property types in `UpsertWorkTaskRequest` (`Application/WorkTasks/DTOs/WorkTaskDto.cs:111`). `DueDate` must be `DateOnly?` and `TargetHours` must be `int?`. Adjust the tests if the types differ.

- [ ] **Step 4: Relax the validator**

In `UpsertWorkTaskRequestValidator`, replace:

```csharp
        RuleFor(x => x.ProjectId).NotNull().GreaterThan(0).WithMessage("Project is required.");
```

with:

```csharp
        // Whether a project is required is a Task Setting (WorkTaskFieldRules); a given one must be real.
        RuleFor(x => x.ProjectId).GreaterThan(0).When(x => x.ProjectId.HasValue).WithMessage("Project is required.");
```

Also delete the line below. Billable is now a Task Setting as well:

```csharp
        RuleFor(x => x.IsBillable).NotNull().WithMessage("Say whether the task is billable.");
```

In `WorkTaskValidatorTests.cs`, the two tests that set `r.ProjectId = null` (~line 44) and `r.IsBillable = null` (~line 87) now pin rules that moved. Change each one to assert the request is **valid** (`Assert.True(result.IsValid)`), and rename them to `A_missing_project_is_left_to_the_task_settings` and `A_missing_billable_is_left_to_the_task_settings`. `WorkTaskFieldRuleTests` now covers the refusals.

- [ ] **Step 5: Apply the rules in `CreateWorkTask`**

Add `using Application.TaskSettings;`. Right after `var input = request.Task;`, insert:

```csharp
            var settings = await WorkTaskSettingsStore.LoadAsync(context, cancellationToken);
            WorkTaskFieldRules.ApplyHiddenOnCreate(settings, input);
            if (WorkTaskFieldRules.Check(settings, input) is { } fieldError)
                return Result<WorkTaskDto>.Invalid(fieldError);
```

Replace the project check:

```csharp
            if (input.ProjectId is not { } projectId
                || !await WorkTaskProjectRule.IsAvailableAsync(context, projectId, input.DepartmentId, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskProjectRule.NotAvailableMessage);
```

with:

```csharp
            // A project may be optional (a Task Setting); one that is given must be available.
            if (input.ProjectId is { } projectId
                && !await WorkTaskProjectRule.IsAvailableAsync(context, projectId, input.DepartmentId, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskProjectRule.NotAvailableMessage);
```

- [ ] **Step 6: Apply the rules in `UpdateWorkTask`**

Add `using Application.TaskSettings;`. Right after `var input = request.Task;`, insert:

```csharp
            var settings = await WorkTaskSettingsStore.LoadAsync(context, cancellationToken);
            WorkTaskFieldRules.KeepHiddenOnEdit(settings, input, task);
            if (WorkTaskFieldRules.Check(settings, input) is { } fieldError)
                return Result<WorkTaskDto>.Invalid(fieldError);
```

Replace the project check:

```csharp
            if ((departmentChanged || projectChanged)
                && (input.ProjectId is not { } projectId
                    || !await WorkTaskProjectRule.IsAvailableAsync(context, projectId, input.DepartmentId, cancellationToken)))
                return Result<WorkTaskDto>.Invalid(WorkTaskProjectRule.NotAvailableMessage);
```

with:

```csharp
            if ((departmentChanged || projectChanged)
                && input.ProjectId is { } projectId
                && !await WorkTaskProjectRule.IsAvailableAsync(context, projectId, input.DepartmentId, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskProjectRule.NotAvailableMessage);
```

The existing assignments further down (`task.Description = ...`, `task.DueDate = input.DueDate`, and so on) now receive the kept values. Leave them as they are.

- [ ] **Step 7: Run the task tests to verify they pass**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTrack.Tests.WorkTasks"`
Expected: all PASS, both the new `WorkTaskFieldRuleTests` and every existing task test. In the existing tests `Request(...)` sets a project and billable, and the defaults keep them required.

- [ ] **Step 8: Commit**

```bash
git add Application/WorkTasks/Support/WorkTaskFieldRules.cs Application/WorkTasks/Validators/UpsertWorkTaskRequestValidator.cs Application/WorkTasks/Commands/CreateWorkTask.cs Application/WorkTasks/Commands/UpdateWorkTask.cs Tests/WorkTrack.Tests/WorkTasks/WorkTaskFieldRuleTests.cs Tests/WorkTrack.Tests/WorkTasks/WorkTaskValidatorTests.cs
git commit -m "Enforce the Task Settings' required and hidden fields on create and edit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Completion rules and attachment limits

**Files:**
- Modify: `Application/WorkTasks/Commands/UpdateWorkTaskStatus.cs`
- Modify: `Application/WorkTasks/Commands/AddWorkTaskAttachment.cs`
- Modify: `Application/Files/Commands/StoreFile.cs`
- Test: `Tests/WorkTrack.Tests/WorkTasks/WorkTaskCompletionSettingsTests.cs`

**Interfaces:**
- Consumes: `WorkTaskSettingsStore.LoadAsync` (Task 1), and `WorkTaskFieldRules.NeedsAttachment` and `AttachmentRequiredMessage` (Task 3).
- Produces: `StoreFile.Command` gains two optional properties, `IReadOnlyCollection<FileSignatureValidator.FileKind>? AcceptedKindsOverride` and `int? MaxSizeBytesOverride`. Both can only **narrow** the purpose's policy: the accepted kinds are the intersection, and the size is the minimum.
- Produces: `AddWorkTaskAttachment.TooManyMessageFor(int max) : string`, replacing the static `TooManyMessage` field.

- [ ] **Step 1: Write the failing tests**

`Tests/WorkTrack.Tests/WorkTasks/WorkTaskCompletionSettingsTests.cs`. `WorkTaskAttachmentTests.cs` has helpers that build a valid PDF and PNG byte payload. Copy that test file's private payload helpers (for example a `Pdf(int size)` / `Png()` builder) into this class verbatim. Don't invent new byte layouts.

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
/// The Task Settings' completion rules: confirmation can be switched off, a required
/// attachment gates Done rather than creation, and the attachment limits are the
/// System Administrator's — narrowing StoreFile's policy, never widening it.
/// </summary>
public class WorkTaskCompletionSettingsTests
{
    private readonly FakeEmailService _email = new();

    private Task<Result<WorkTaskDto>> SetStatus(AppDbContext db, int id, string caller, WorkTaskStatus status) =>
        new UpdateWorkTaskStatus.Handler(db, _email, NullLogger<UpdateWorkTaskStatus.Handler>.Instance)
            .Handle(new UpdateWorkTaskStatus.Command { Id = id, CallerUserId = caller, Status = status }, CancellationToken.None);

    private static Task<Result<WorkTaskDto>> Attach(AppDbContext db, int id, byte[] content, string name) =>
        new AddWorkTaskAttachment.Handler(db).Handle(new AddWorkTaskAttachment.Command
        {
            Id = id, CallerUserId = Hr, Content = content, FileName = name,
        }, CancellationToken.None);

    private static async Task Configure(AppDbContext db, Action<WorkTaskSettings> change)
    {
        var row = await db.WorkTaskSettings.SingleAsync();
        change(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<int> Seeded(AppDbContext db, WorkTask task)
    {
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task.Id;
    }

    // Copy WorkTaskAttachmentTests' PDF/PNG payload helpers here.

    [Fact]
    public async Task With_confirmation_off_an_assignees_done_closes_the_task()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.RequireCompletionConfirmation = false);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.Done, result.Value!.Status);
        Assert.NotNull(result.Value.CompletedAtUtc);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task A_required_attachment_refuses_done_without_a_file()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.AttachmentsRequirement = FieldRequirement.Required);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkTaskFieldRules.AttachmentRequiredMessage, result.Error);
    }

    [Fact]
    public async Task A_required_attachment_refuses_confirm_without_a_file()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));
        await Configure(db, s => s.AttachmentsRequirement = FieldRequirement.Required);

        var result = await SetStatus(db, id, Hr, WorkTaskStatus.Done);

        Assert.Equal(WorkTaskFieldRules.AttachmentRequiredMessage, result.Error);
    }

    [Fact]
    public async Task A_required_attachment_does_not_stop_cancelling()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.AttachmentsRequirement = FieldRequirement.Required);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, Hr, WorkTaskStatus.Cancelled);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task With_a_file_a_required_attachment_lets_done_through()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.AttachmentsRequirement = FieldRequirement.Required);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));
        Assert.True((await Attach(db, id, Pdf(2_000), "brief.pdf")).IsSuccess);

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.AwaitingConfirmation, result.Value!.Status);
    }

    [Fact]
    public async Task The_configured_count_limits_uploads()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.MaxAttachmentsPerTask = 2);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));
        await Attach(db, id, Pdf(2_000), "a.pdf");
        await Attach(db, id, Pdf(2_000), "b.pdf");

        var third = await Attach(db, id, Pdf(2_000), "c.pdf");

        Assert.Equal(AddWorkTaskAttachment.TooManyMessageFor(2), third.Error);
    }

    [Fact]
    public async Task A_task_over_a_lowered_count_keeps_its_files_but_takes_no_more()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));
        for (var i = 0; i < 3; i++) await Attach(db, id, Pdf(2_000), $"{i}.pdf");
        await Configure(db, s => s.MaxAttachmentsPerTask = 1);

        var more = await Attach(db, id, Pdf(2_000), "late.pdf");

        Assert.False(more.IsSuccess);
        Assert.Equal(3, await db.WorkTaskAttachments.CountAsync(a => a.WorkTaskId == id));
    }

    [Fact]
    public async Task The_configured_size_limits_uploads()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.MaxAttachmentSizeMb = 2);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        var result = await Attach(db, id, Pdf(3 * 1024 * 1024), "big.pdf");

        Assert.Equal("That file is larger than the 2MB limit.", result.Error);
    }

    [Fact]
    public async Task A_kind_switched_off_is_refused()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.AllowImages = false);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        var result = await Attach(db, id, Png(), "shot.png");

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Only ", result.Error);
    }
}
```

If the copied PDF helper can't produce a payload of a chosen size, extend it the same way it already pads its bytes: pad after the `%PDF-` header up to `size`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTaskCompletionSettingsTests"`
Expected: build FAIL, because `TooManyMessageFor` doesn't exist yet. After a temporary stub, the behaviour tests should fail as well.

- [ ] **Step 3: Gate completion in `UpdateWorkTaskStatus`**

Add `using Application.TaskSettings;` and `using Microsoft.EntityFrameworkCore;`. After the `StageIsDerivedMessage` check, insert:

```csharp
            var settings = await WorkTaskSettingsStore.LoadAsync(context, cancellationToken);
```

In the `else if` branch that converts an assignee's Done into `AwaitingConfirmation`, add the switch as the first condition:

```csharp
            else if (target == WorkTaskStatus.Done && !waiting && !isReviewer
                && settings.RequireCompletionConfirmation
                && WorkTaskReviewRule.NeedsConfirmation(task)
                && await WorkTaskReviewRule.AnyReviewerAsync(context, task, cancellationToken))
```

Just before `if (task.Status != target)`, insert the attachment gate. It covers every path into Done or AwaitingConfirmation, and nothing else:

```csharp
            if (target is WorkTaskStatus.Done or WorkTaskStatus.AwaitingConfirmation
                && task.Status != target
                && WorkTaskFieldRules.NeedsAttachment(
                    settings, await context.WorkTaskAttachments.CountAsync(a => a.WorkTaskId == task.Id, cancellationToken)))
                return Result<WorkTaskDto>.Failure(WorkTaskFieldRules.AttachmentRequiredMessage);
```

- [ ] **Step 4: Let `StoreFile` narrow its policy**

Add these to `StoreFile.Command`:

```csharp
        /// <summary>A caller's own, narrower list (Task Settings). Intersected with the purpose's; never widens it.</summary>
        public IReadOnlyCollection<FileSignatureValidator.FileKind>? AcceptedKindsOverride { get; set; }

        /// <summary>A caller's own, lower ceiling (Task Settings). The smaller of it and the purpose's wins.</summary>
        public int? MaxSizeBytesOverride { get; set; }
```

In `Handler.Handle`, right after the `Policies.TryGetValue` block, insert:

```csharp
            if (request.AcceptedKindsOverride is { } narrowed)
                policy = policy with { AcceptedKinds = policy.AcceptedKinds.Where(narrowed.Contains).ToArray() };
            if (request.MaxSizeBytesOverride is { } ceiling && ceiling < policy.MaxSizeBytes)
                policy = policy with { MaxSizeBytes = ceiling };
```

This works because `UploadPolicy` is a `sealed record`. If `narrowed.Contains` binds to a span overload (see the C# 14 note in memory), write `k => narrowed.Contains(k)` with `narrowed` typed as `IReadOnlyCollection`. That is already its type, so it should bind correctly.

- [ ] **Step 5: Use the settings in `AddWorkTaskAttachment`**

Add `using Application.TaskSettings;`. Replace the static `TooManyMessage` field with:

```csharp
    public static string TooManyMessageFor(int max) =>
        $"A task can carry at most {max} attachments. Remove one to add another.";
```

In `Handle`, replace the count check and the `StoreFile` call:

```csharp
            var settings = await WorkTaskSettingsStore.LoadAsync(context, cancellationToken);
            if (await context.WorkTaskAttachments.CountAsync(a => a.WorkTaskId == task.Id, cancellationToken) >= settings.MaxAttachmentsPerTask)
                return Result<WorkTaskDto>.Invalid(TooManyMessageFor(settings.MaxAttachmentsPerTask));

            var stored = await new StoreFile.Handler(context).Handle(new StoreFile.Command
            {
                Content = request.Content,
                FileName = request.FileName,
                DeclaredContentType = request.DeclaredContentType,
                Purpose = StoredFilePurpose.TaskAttachment,
                UploadedById = request.CallerUserId,
                AcceptedKindsOverride = AllowedKinds(settings),
                MaxSizeBytesOverride = settings.MaxAttachmentSizeMb * 1024 * 1024,
            }, cancellationToken);
```

Add this private helper to the class:

```csharp
    private static List<FileSignatureValidator.FileKind> AllowedKinds(WorkTaskSettings s)
    {
        var kinds = new List<FileSignatureValidator.FileKind>();
        if (s.AllowImages) kinds.AddRange([FileSignatureValidator.FileKind.Jpeg, FileSignatureValidator.FileKind.Png]);
        if (s.AllowPdf) kinds.Add(FileSignatureValidator.FileKind.Pdf);
        if (s.AllowWord) kinds.AddRange([FileSignatureValidator.FileKind.Docx, FileSignatureValidator.FileKind.Doc]);
        if (s.AllowExcel) kinds.AddRange([FileSignatureValidator.FileKind.Xlsx, FileSignatureValidator.FileKind.Xls]);
        return kinds;
    }
```

Check the namespace `FileSignatureValidator` lives in (`grep -rn "class FileSignatureValidator" Application`) and add its `using` if needed.

Update the one existing reference in `WorkTaskAttachmentTests.cs:135`, `AddWorkTaskAttachment.TooManyMessage`, to `AddWorkTaskAttachment.TooManyMessageFor(WorkTaskAttachment.MaxPerTask)`. Keep the `MaxPerTask` constant: it still equals the default and the test loop uses it.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Tests/WorkTrack.Tests --filter "FullyQualifiedName~WorkTrack.Tests.WorkTasks|FullyQualifiedName~StoreFile|FullyQualifiedName~FileSignature"`
Expected: all PASS, including the existing `WorkTaskReviewTests` and `WorkTaskAttachmentTests`.

- [ ] **Step 7: Run the whole server suite once**

Run: `dotnet test Tests/WorkTrack.Tests`
Expected: all PASS. Report the pass count. If anything outside the task tests fails, stop and investigate before committing.

- [ ] **Step 8: Commit**

```bash
git add Application/WorkTasks/Commands/UpdateWorkTaskStatus.cs Application/WorkTasks/Commands/AddWorkTaskAttachment.cs Application/Files/Commands/StoreFile.cs Tests/WorkTrack.Tests/WorkTasks/WorkTaskCompletionSettingsTests.cs Tests/WorkTrack.Tests/WorkTasks/WorkTaskAttachmentTests.cs
git commit -m "Honour the Task Settings on completion and on upload

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Client settings module and the Task Settings panel

**Files:**
- Create: `client/src/lib/api/work-task-settings.ts`
- Modify: `client/src/lib/api/index.ts` (add `export * from './work-task-settings'`)
- Create: `client/src/lib/task-settings.ts`, `client/src/lib/task-settings.test.ts`
- Create: `client/src/lib/task-settings-query.ts`: the `useWorkTaskSettings` hook.
- Create: `client/src/components/admin/TaskSettingsPanel.tsx`, `client/src/components/admin/TaskSettingsPanel.test.tsx`
- Modify: `client/src/components/admin/index.ts` (add `export { default as TaskSettingsPanel } from './TaskSettingsPanel'`)
- Modify: `client/src/components/annual-leave/DashboardHome.tsx`: add `TaskSettingsPanel` to the import from `'..'` on line 37, and after line 141 add `if (isSystemAdmin && adminSection === 'task-settings') return <TaskSettingsPanel />`.
- Modify: `client/src/components/layout/Sidebar.tsx`: in the System Administrator's Configuration block, after the Leave Types entry, add a `Task Setting` sub-section and a `Tasks` entry.
- Modify: `client/src/components/layout/Topbar.tsx` (~line 357): `else if (s === 'task-settings') pageTitle = 'Task Settings'`
- Modify: `client/src/components/layout/Sidebar.test.tsx`: the System Administrator case expects "Task Settings". The HR case's hidden list gains `'Task Settings'`.

**Interfaces:**
- Produces, in `lib/api/work-task-settings.ts`:
  - `type FieldRequirement = 'Optional' | 'Required' | 'Hidden'`
  - `interface WorkTaskSettings`, with camelCase properties matching `WorkTaskSettingsDto`
  - `getWorkTaskSettings(): Promise<WorkTaskSettings>`
  - `updateWorkTaskSettings(s: WorkTaskSettings): Promise<WorkTaskSettings>`
  - `WORK_TASK_SETTINGS_KEY`
- Produces, in `lib/task-settings-query.ts`: `useWorkTaskSettings(): WorkTaskSettings`, which never returns undefined. It returns `DEFAULT_TASK_SETTINGS` while loading or on error.
- Produces, in `lib/task-settings.ts`:
  - `DEFAULT_TASK_SETTINGS`
  - `FIELD_OPTIONS: Record<TaskField, FieldRequirement[]>`
  - `type TaskField = 'description' | 'dueDate' | 'targetHours' | 'attachments' | 'project' | 'billable'`
  - `requirementOf(s, field): FieldRequirement`
  - `isShown(s, field: TaskField | 'priority'): boolean`
  - `fieldRequirementError(s, values): string | null`
  - `needsAttachmentBeforeDone(s, attachmentCount): boolean`
  - `attachmentLimits(s): TaskAttachmentLimits`
  - the six message constants
- Produces, in `lib/task-attachments.ts` (the full change is in Task 6; Task 5 only needs the type): `interface TaskAttachmentLimits { maxFiles: number; maxBytes: number; extensions: string[] }`.

- [ ] **Step 1: Write the failing lib test**

`client/src/lib/task-settings.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import {
    ATTACHMENT_REQUIRED_MESSAGE, DEFAULT_TASK_SETTINGS, DUE_DATE_REQUIRED_MESSAGE, PROJECT_REQUIRED_MESSAGE,
    attachmentLimits, fieldRequirementError, isShown, needsAttachmentBeforeDone,
} from './task-settings'

const values = { description: '', dueDate: '', targetHours: null, projectId: 10, isBillable: true }

describe('task settings', () => {
    it('defaults reproduce today: project and billable required, the rest optional', () => {
        expect(fieldRequirementError(DEFAULT_TASK_SETTINGS, values)).toBeNull()
        expect(fieldRequirementError(DEFAULT_TASK_SETTINGS, { ...values, projectId: null })).toBe(PROJECT_REQUIRED_MESSAGE)
    })

    it('a required due date is refused blank, with the server message', () => {
        const s = { ...DEFAULT_TASK_SETTINGS, dueDateRequirement: 'Required' as const }
        expect(fieldRequirementError(s, values)).toBe(DUE_DATE_REQUIRED_MESSAGE)
    })

    it('a hidden field is never checked and never shown', () => {
        const s = { ...DEFAULT_TASK_SETTINGS, billableRequirement: 'Hidden' as const }
        expect(fieldRequirementError(s, { ...values, isBillable: null })).toBeNull()
        expect(isShown(s, 'billable')).toBe(false)
        expect(isShown({ ...DEFAULT_TASK_SETTINGS, showPriority: false }, 'priority')).toBe(false)
    })

    it('a required attachment holds Done until a file is on the task', () => {
        const s = { ...DEFAULT_TASK_SETTINGS, attachmentsRequirement: 'Required' as const }
        expect(needsAttachmentBeforeDone(s, 0)).toBe(true)
        expect(needsAttachmentBeforeDone(s, 1)).toBe(false)
        expect(needsAttachmentBeforeDone(DEFAULT_TASK_SETTINGS, 0)).toBe(false)
        expect(ATTACHMENT_REQUIRED_MESSAGE).toBe('Attach a file before marking this task done.')
    })

    it('attachment limits follow the settings', () => {
        const limits = attachmentLimits({ ...DEFAULT_TASK_SETTINGS, maxAttachmentsPerTask: 3, maxAttachmentSizeMb: 2, allowImages: false, allowWord: false })
        expect(limits.maxFiles).toBe(3)
        expect(limits.maxBytes).toBe(2 * 1024 * 1024)
        expect(limits.extensions).toEqual(['.pdf', '.xls', '.xlsx'])
    })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd client && node_modules/.bin/vitest run src/lib/task-settings.test.ts`
Expected: FAIL, because the module doesn't exist yet.

- [ ] **Step 3: Write the API module**

`client/src/lib/api/work-task-settings.ts`:

```ts
import apiClient from './client'

export type FieldRequirement = 'Optional' | 'Required' | 'Hidden'

/** Mirrors `WorkTaskSettingsDto` — a full replace on save. */
export interface WorkTaskSettings {
    descriptionRequirement: FieldRequirement
    dueDateRequirement: FieldRequirement
    targetHoursRequirement: FieldRequirement
    attachmentsRequirement: FieldRequirement
    projectRequirement: FieldRequirement
    billableRequirement: FieldRequirement
    showPriority: boolean
    requireCompletionConfirmation: boolean
    maxAttachmentsPerTask: number
    maxAttachmentSizeMb: number
    allowImages: boolean
    allowPdf: boolean
    allowWord: boolean
    allowExcel: boolean
}

export async function getWorkTaskSettings() {
    const response = await apiClient.get<WorkTaskSettings>('/worktasksettings')
    return response.data
}

export async function updateWorkTaskSettings(settings: WorkTaskSettings) {
    const response = await apiClient.put<WorkTaskSettings>('/worktasksettings', settings)
    return response.data
}

/** Under `['work-tasks']`, so the `notificationsUpdated` handler in App.tsx refreshes it. */
export const WORK_TASK_SETTINGS_KEY = ['work-tasks', 'settings'] as const
```

Create `client/src/lib/task-settings-query.ts`. The hook lives here, outside `lib/api`, and reads through the `lib/api` index. The component tests replace that whole index with `vi.mock('../../lib/api', () => ({ ... }))`. A hook inside the mocked module would disappear under such a mock, while this one picks up the test's `getWorkTaskSettings`.

```ts
import { useQuery } from '@tanstack/react-query'
import { WORK_TASK_SETTINGS_KEY, getWorkTaskSettings } from './api'
import type { WorkTaskSettings } from './api'
import { DEFAULT_TASK_SETTINGS } from './task-settings'

/**
 * The rules every task surface reads. Never undefined: while loading, and against an
 * older API with no endpoint, it is today's behaviour — so nothing is held back or
 * hidden that the server would not hold back or hide.
 */
export function useWorkTaskSettings(): WorkTaskSettings {
    const { data } = useQuery({
        queryKey: WORK_TASK_SETTINGS_KEY ?? ['work-tasks', 'settings'],
        queryFn: getWorkTaskSettings,
        retry: false,
        staleTime: 60_000,
    })
    return data ?? DEFAULT_TASK_SETTINGS
}
```

The `?? ['work-tasks', 'settings']` is there because a test's `vi.mock` factory that lists only functions leaves `WORK_TASK_SETTINGS_KEY` undefined.

- [ ] **Step 4: Write the rules module**

`client/src/lib/task-settings.ts`:

```ts
/**
 * The System Administrator's Task Settings, as every task surface reads them.
 * Mirrors `WorkTaskFieldRules` on the server — keep the messages identical — so the
 * dialog holds Save and the card holds Done where the API is certain to refuse.
 */
import type { FieldRequirement, WorkTaskSettings } from './api/work-task-settings'
import type { TaskAttachmentLimits } from './task-attachments'

export const DESCRIPTION_REQUIRED_MESSAGE = 'Description is required.'
export const DUE_DATE_REQUIRED_MESSAGE = 'Due date is required.'
export const TARGET_HOURS_REQUIRED_MESSAGE = 'Target hours are required.'
export const PROJECT_REQUIRED_MESSAGE = 'Project is required.'
export const BILLABLE_REQUIRED_MESSAGE = 'Say whether the task is billable.'
export const ATTACHMENT_REQUIRED_MESSAGE = 'Attach a file before marking this task done.'

/** Today's behaviour; also what an older API with no settings endpoint reads as. */
export const DEFAULT_TASK_SETTINGS: WorkTaskSettings = {
    descriptionRequirement: 'Optional',
    dueDateRequirement: 'Optional',
    targetHoursRequirement: 'Optional',
    attachmentsRequirement: 'Optional',
    projectRequirement: 'Required',
    billableRequirement: 'Required',
    showPriority: true,
    requireCompletionConfirmation: true,
    maxAttachmentsPerTask: 10,
    maxAttachmentSizeMb: 10,
    allowImages: true,
    allowPdf: true,
    allowWord: true,
    allowExcel: true,
}

export type TaskField = 'description' | 'dueDate' | 'targetHours' | 'attachments' | 'project' | 'billable'

/** Which values each field may take — mirrors UpdateWorkTaskSettingsValidator. */
export const FIELD_OPTIONS: Record<TaskField, FieldRequirement[]> = {
    description: ['Required', 'Optional', 'Hidden'],
    dueDate: ['Required', 'Optional', 'Hidden'],
    targetHours: ['Required', 'Optional', 'Hidden'],
    attachments: ['Required', 'Optional', 'Hidden'],
    project: ['Required', 'Optional'],
    billable: ['Required', 'Hidden'],
}

const KEYS: Record<TaskField, keyof WorkTaskSettings> = {
    description: 'descriptionRequirement',
    dueDate: 'dueDateRequirement',
    targetHours: 'targetHoursRequirement',
    attachments: 'attachmentsRequirement',
    project: 'projectRequirement',
    billable: 'billableRequirement',
}

export function requirementOf(s: WorkTaskSettings, field: TaskField): FieldRequirement {
    return s[KEYS[field]] as FieldRequirement
}

export function withRequirement(s: WorkTaskSettings, field: TaskField, value: FieldRequirement): WorkTaskSettings {
    return { ...s, [KEYS[field]]: value }
}

export function isShown(s: WorkTaskSettings, field: TaskField | 'priority'): boolean {
    return field === 'priority' ? s.showPriority : requirementOf(s, field) !== 'Hidden'
}

export interface TaskFieldValues {
    description: string
    dueDate: string
    targetHours: number | null
    projectId: number | null
    isBillable: boolean | null
}

/** The first required field left blank, in the server's order, or null. */
export function fieldRequirementError(s: WorkTaskSettings, v: TaskFieldValues): string | null {
    if (s.projectRequirement === 'Required' && v.projectId == null) return PROJECT_REQUIRED_MESSAGE
    if (s.billableRequirement === 'Required' && v.isBillable == null) return BILLABLE_REQUIRED_MESSAGE
    if (s.descriptionRequirement === 'Required' && v.description.trim() === '') return DESCRIPTION_REQUIRED_MESSAGE
    if (s.dueDateRequirement === 'Required' && v.dueDate === '') return DUE_DATE_REQUIRED_MESSAGE
    if (s.targetHoursRequirement === 'Required' && v.targetHours == null) return TARGET_HOURS_REQUIRED_MESSAGE
    return null
}

export function needsAttachmentBeforeDone(s: WorkTaskSettings, attachmentCount: number): boolean {
    return s.attachmentsRequirement === 'Required' && attachmentCount === 0
}

export function attachmentLimits(s: WorkTaskSettings): TaskAttachmentLimits {
    const extensions = [
        ...(s.allowPdf ? ['.pdf'] : []),
        ...(s.allowWord ? ['.doc', '.docx'] : []),
        ...(s.allowExcel ? ['.xls', '.xlsx'] : []),
        ...(s.allowImages ? ['.jpg', '.jpeg', '.png'] : []),
    ]
    return { maxFiles: s.maxAttachmentsPerTask, maxBytes: s.maxAttachmentSizeMb * 1024 * 1024, extensions }
}
```

Add the type to `client/src/lib/task-attachments.ts` now, so this compiles. Task 6 uses it:

```ts
export interface TaskAttachmentLimits {
    maxFiles: number
    maxBytes: number
    extensions: string[]
}
```

Add `export * from './work-task-settings'` to `client/src/lib/api/index.ts`.

- [ ] **Step 5: Run the lib test to verify it passes**

Run: `cd client && node_modules/.bin/vitest run src/lib/task-settings.test.ts`
Expected: PASS (5 tests).

- [ ] **Step 6: Write the failing panel test**

`client/src/components/admin/TaskSettingsPanel.test.tsx`. Mirror the render and mocking setup that `ProjectTypesPanel.test.tsx` uses: open that file first and copy its `vi.mock('../../lib/api', ...)` shape and its `QueryClientProvider` wrapper.

```tsx
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import TaskSettingsPanel from './TaskSettingsPanel'
import { DEFAULT_TASK_SETTINGS } from '../../lib/task-settings'

const getWorkTaskSettings = vi.fn()
const updateWorkTaskSettings = vi.fn()
vi.mock('../../lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('../../lib/api')>()),
    getWorkTaskSettings: () => getWorkTaskSettings(),
    updateWorkTaskSettings: (s: unknown) => updateWorkTaskSettings(s),
}))

function renderPanel() {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(<QueryClientProvider client={client}><TaskSettingsPanel /></QueryClientProvider>)
}

describe('TaskSettingsPanel', () => {
    beforeEach(() => {
        getWorkTaskSettings.mockResolvedValue(DEFAULT_TASK_SETTINGS)
        updateWorkTaskSettings.mockImplementation(async (s) => s)
    })

    it('offers each field only its allowed values', async () => {
        renderPanel()
        const project = await screen.findByRole('group', { name: 'Project' })
        expect(within(project).queryByRole('button', { name: 'Hidden' })).toBeNull()
        const billable = screen.getByRole('group', { name: 'Billable' })
        expect(within(billable).queryByRole('button', { name: 'Optional' })).toBeNull()
        const due = screen.getByRole('group', { name: 'Due date' })
        expect(within(due).getAllByRole('button')).toHaveLength(3)
    })

    it('saves the whole settings object', async () => {
        const user = userEvent.setup()
        renderPanel()
        const due = await screen.findByRole('group', { name: 'Due date' })
        await user.click(within(due).getByRole('button', { name: 'Required' }))
        await user.click(screen.getByRole('button', { name: 'Save changes' }))

        await waitFor(() => expect(updateWorkTaskSettings).toHaveBeenCalledWith({ ...DEFAULT_TASK_SETTINGS, dueDateRequirement: 'Required' }))
    })

    it('says what switching confirmation off does', async () => {
        renderPanel()
        expect(await screen.findByText(/closes every task now waiting for confirmation/i)).toBeInTheDocument()
    })

    it('holds Save when no file kind is allowed', async () => {
        const user = userEvent.setup()
        renderPanel()
        for (const kind of ['Images', 'PDF', 'Word', 'Excel'])
            await user.click(await screen.findByRole('checkbox', { name: kind }))
        expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()
        expect(screen.getByText('Allow at least one kind of file.')).toBeInTheDocument()
    })
})
```

- [ ] **Step 7: Run it to verify it fails**

Run: `cd client && node_modules/.bin/vitest run src/components/admin/TaskSettingsPanel.test.tsx`
Expected: FAIL, because the component doesn't exist yet.

- [ ] **Step 8: Write the panel**

`client/src/components/admin/TaskSettingsPanel.tsx`. Use the page-header shape from `SystemLogPanel` (a 22px title and a 14px subtitle):

```tsx
import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
    Alert, Box, Button, Checkbox, FormControlLabel, Paper, Stack, Switch, TextField, ToggleButton, ToggleButtonGroup, Typography,
} from '@mui/material'
import { WORK_TASK_SETTINGS_KEY, getWorkTaskSettings, updateWorkTaskSettings } from '../../lib/api'
import type { FieldRequirement, WorkTaskSettings } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { FIELD_OPTIONS, requirementOf, withRequirement, type TaskField } from '../../lib/task-settings'

const FIELD_LABELS: Record<TaskField, string> = {
    description: 'Description',
    dueDate: 'Due date',
    targetHours: 'Target hours',
    attachments: 'Attachments',
    project: 'Project',
    billable: 'Billable',
}

const FIELD_NOTES: Partial<Record<TaskField, string>> = {
    attachments: 'Required means a task cannot be marked done until a file is attached.',
    project: 'Cannot be hidden: timesheet rows take their project from the task.',
    billable: 'A yes/no answer — asked, or not asked.',
}

/** Mirrors UpdateWorkTaskSettingsValidator, with its messages. */
function settingsError(s: WorkTaskSettings): string | null {
    if (!(s.allowImages || s.allowPdf || s.allowWord || s.allowExcel)) return 'Allow at least one kind of file.'
    if (!Number.isInteger(s.maxAttachmentsPerTask) || s.maxAttachmentsPerTask < 1 || s.maxAttachmentsPerTask > 20)
        return 'A task can be allowed 1 to 20 attachments.'
    if (!Number.isInteger(s.maxAttachmentSizeMb) || s.maxAttachmentSizeMb < 1 || s.maxAttachmentSizeMb > 10)
        return 'The size limit must be 1 to 10 MB.'
    return null
}

/**
 * The System Administrator's Task Settings: which task fields are asked, whether an
 * assignee's Done waits for confirmation, and what files a task may carry. One set for
 * the whole organisation. The System Administrator does not see the tasks themselves.
 */
export default function TaskSettingsPanel() {
    const queryClient = useQueryClient()
    const { data, isLoading, isError } = useQuery({ queryKey: WORK_TASK_SETTINGS_KEY, queryFn: getWorkTaskSettings })
    const [draft, setDraft] = useState<WorkTaskSettings | null>(null)
    useEffect(() => { if (data) setDraft(data) }, [data])

    const save = useMutation({
        mutationFn: updateWorkTaskSettings,
        onSuccess: (saved) => {
            queryClient.setQueryData(WORK_TASK_SETTINGS_KEY, saved)
            void queryClient.invalidateQueries({ queryKey: ['work-tasks'] })
        },
    })

    if (isLoading || !draft) return isError ? <Alert severity="error">Could not load the task settings.</Alert> : null
    const error = settingsError(draft)
    const set = (patch: Partial<WorkTaskSettings>) => setDraft({ ...draft, ...patch })

    return (
        <Stack spacing={2}>
            <Box>
                <Typography sx={{ fontSize: 22, fontWeight: 700, color: 'text.primary' }}>✅ Task Settings</Typography>
                <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>
                    How tasks work for everyone: which fields are asked, how a task is finished, and what files it may carry. Title and Department are always required.
                </Typography>
            </Box>

            {save.isError && <Alert severity="error">{getApiErrorMessage(save.error, 'The task settings could not be saved.')}</Alert>}
            {save.isSuccess && <Alert severity="success">Task settings saved.</Alert>}

            <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography sx={{ fontWeight: 600, mb: 1.5 }}>Fields</Typography>
                <Stack spacing={1.5}>
                    {(Object.keys(FIELD_OPTIONS) as TaskField[]).map((field) => (
                        <Box key={field} sx={{ display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
                            <Box sx={{ width: 140, fontSize: 14 }} id={`task-field-${field}`}>{FIELD_LABELS[field]}</Box>
                            <ToggleButtonGroup
                                exclusive
                                size="small"
                                aria-labelledby={`task-field-${field}`}
                                value={requirementOf(draft, field)}
                                onChange={(_, value: FieldRequirement | null) => { if (value) setDraft(withRequirement(draft, field, value)) }}
                            >
                                {FIELD_OPTIONS[field].map((option) => (
                                    <ToggleButton key={option} value={option}>{option}</ToggleButton>
                                ))}
                            </ToggleButtonGroup>
                            {FIELD_NOTES[field] && <Box sx={{ fontSize: 12, color: 'text.secondary' }}>{FIELD_NOTES[field]}</Box>}
                        </Box>
                    ))}
                    <FormControlLabel
                        control={<Switch checked={draft.showPriority} onChange={(e) => set({ showPriority: e.target.checked })} />}
                        label="Show priority"
                    />
                </Stack>
            </Paper>

            <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography sx={{ fontWeight: 600, mb: 1 }}>Completion</Typography>
                <FormControlLabel
                    control={<Switch checked={draft.requireCompletionConfirmation} onChange={(e) => set({ requireCompletionConfirmation: e.target.checked })} />}
                    label="A task somebody else handed you waits for confirmation when marked done"
                />
                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                    Switching this off closes every task now waiting for confirmation, without emailing anyone.
                </Typography>
            </Paper>

            <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography sx={{ fontWeight: 600, mb: 1.5 }}>Attachments</Typography>
                <Stack direction="row" spacing={2} sx={{ mb: 1.5 }}>
                    <TextField
                        label="Files per task" type="number" size="small"
                        value={draft.maxAttachmentsPerTask}
                        onChange={(e) => set({ maxAttachmentsPerTask: Number(e.target.value) })}
                        slotProps={{ htmlInput: { min: 1, max: 20 } }}
                    />
                    <TextField
                        label="Size limit (MB)" type="number" size="small"
                        value={draft.maxAttachmentSizeMb}
                        onChange={(e) => set({ maxAttachmentSizeMb: Number(e.target.value) })}
                        slotProps={{ htmlInput: { min: 1, max: 10 } }}
                    />
                </Stack>
                <Box>
                    <FormControlLabel control={<Checkbox checked={draft.allowImages} onChange={(e) => set({ allowImages: e.target.checked })} />} label="Images" />
                    <FormControlLabel control={<Checkbox checked={draft.allowPdf} onChange={(e) => set({ allowPdf: e.target.checked })} />} label="PDF" />
                    <FormControlLabel control={<Checkbox checked={draft.allowWord} onChange={(e) => set({ allowWord: e.target.checked })} />} label="Word" />
                    <FormControlLabel control={<Checkbox checked={draft.allowExcel} onChange={(e) => set({ allowExcel: e.target.checked })} />} label="Excel" />
                </Box>
                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                    Limits apply to new uploads. Files already attached stay.
                </Typography>
            </Paper>

            {error && <Typography sx={{ fontSize: 13, color: 'error.main' }}>{error}</Typography>}
            <Box>
                <Button variant="contained" disabled={!!error || save.isPending} onClick={() => save.mutate(draft)}>Save changes</Button>
            </Box>
        </Stack>
    )
}
```

MUI's `ToggleButtonGroup` renders `role="group"`, and `aria-labelledby` gives it the accessible name the test queries. If the test can't find the group by name, pass `aria-label={FIELD_LABELS[field]}` instead of `aria-labelledby`.

- [ ] **Step 9: Wire up the route, sidebar entry and title**

In `DashboardHome.tsx`, add `TaskSettingsPanel` to the import on line 37, and after the `system-log` line (~141):

```tsx
    if (isSystemAdmin && adminSection === 'task-settings') return <TaskSettingsPanel />
```

In `Sidebar.tsx`, add these after the Leave Types entry, inside the Configuration block. Import `TaskAltRoundedIcon` from `@mui/icons-material/TaskAltRounded`, the same way the other icons are imported:

```tsx
                { kind: 'section', label: 'Task Setting', sub: true } as NavEntry,
                { kind: 'item', label: 'Task Settings', icon: <TaskAltRoundedIcon sx={{ fontSize: 18 }} />, onClick: () => uiStore.navigateToAdminSection('task-settings'), active: onAdminSection('task-settings'), indent: true } as NavEntry,
```

In `Topbar.tsx`, before the final `else pageTitle = 'Administration'`:

```tsx
        else if (s === 'task-settings') pageTitle = 'Task Settings'
```

In `Sidebar.test.tsx`, add `'Task Settings'` to the HR Administrator's hidden list (line ~118). In the System Administrator test (line ~124), add an assertion that `Task Settings` is present, the same way that test checks the other Configuration entries.

- [ ] **Step 10: Run the client tests to verify they pass**

Run: `cd client && node_modules/.bin/vitest run src/lib/task-settings.test.ts src/components/admin/TaskSettingsPanel.test.tsx src/components/layout`
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add client/src/lib/api/work-task-settings.ts client/src/lib/api/index.ts client/src/lib/task-settings.ts client/src/lib/task-settings-query.ts client/src/lib/task-settings.test.ts client/src/lib/task-attachments.ts client/src/components/admin/TaskSettingsPanel.tsx client/src/components/admin/TaskSettingsPanel.test.tsx client/src/components/admin/index.ts client/src/components/annual-leave/DashboardHome.tsx client/src/components/layout/Sidebar.tsx client/src/components/layout/Sidebar.test.tsx client/src/components/layout/Topbar.tsx
git commit -m "Add the Task Settings page for the System Administrator

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Task dialog and attachment picker honour the settings

**Files:**
- Modify: `client/src/lib/task-attachments.ts`
- Modify: `client/src/components/tasks/TaskAttachments.tsx`, `TaskAttachments.test.tsx`
- Modify: `client/src/components/tasks/TaskDialog.tsx`, `TaskDialog.test.tsx`

**Interfaces:**
- Consumes: `useWorkTaskSettings` (Task 5), and from `lib/task-settings.ts` `isShown`, `requirementOf`, `fieldRequirementError`, `attachmentLimits` and `DEFAULT_TASK_SETTINGS`.
- Produces: `taskAttachmentError(file, limits?: TaskAttachmentLimits)`, `DEFAULT_ATTACHMENT_LIMITS` and `acceptFor(limits): string` in `lib/task-attachments.ts`. `TaskAttachments` and `StagedTaskAttachments` gain an optional `limits?: TaskAttachmentLimits` prop, defaulting to `DEFAULT_ATTACHMENT_LIMITS`.

- [ ] **Step 1: Write the failing dialog tests**

Add these to `TaskDialog.test.tsx`. The file already mocks `../../lib/api`. Add `getWorkTaskSettings` to that mock, defaulting to `mockResolvedValue(DEFAULT_TASK_SETTINGS)` in its `beforeEach`, and use the file's existing render helper. The names below (`renderDialog`, `fillRequired`) stand for whatever that file already calls its helpers. Read the top of the file first and use the real names.

```tsx
    it('holds Save until a required due date is given, and says why', async () => {
        getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, dueDateRequirement: 'Required' })
        const user = userEvent.setup()
        renderDialog()
        await fillRequired(user) // title, project, billable — as the existing create test does

        expect(await screen.findByText('Due date is required.')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Create task' })).toBeDisabled()

        await user.type(screen.getByLabelText(/Due date/), '2026-10-01')
        expect(screen.getByRole('button', { name: 'Create task' })).toBeEnabled()
    })

    it('leaves hidden fields off the form', async () => {
        getWorkTaskSettings.mockResolvedValue({
            ...DEFAULT_TASK_SETTINGS,
            descriptionRequirement: 'Hidden', dueDateRequirement: 'Hidden', targetHoursRequirement: 'Hidden',
            attachmentsRequirement: 'Hidden', billableRequirement: 'Hidden', showPriority: false,
        })
        renderDialog()

        await screen.findByLabelText(/Title/)
        await waitFor(() => expect(screen.queryByLabelText(/Description/)).toBeNull())
        expect(screen.queryByLabelText(/Due date/)).toBeNull()
        expect(screen.queryByLabelText(/Target hours/)).toBeNull()
        expect(screen.queryByText('Attachments')).toBeNull()
        expect(screen.queryByText('Billing')).toBeNull()
        expect(screen.queryByLabelText(/Priority/)).toBeNull()
    })

    it('lets an optional project be left out and sends null', async () => {
        getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, projectRequirement: 'Optional' })
        const user = userEvent.setup()
        renderDialog()
        await user.type(await screen.findByLabelText(/Title/), 'No project')
        await user.click(screen.getByLabelText('Billable'))
        await user.click(screen.getByRole('button', { name: 'Create task' }))

        await waitFor(() => expect(createWorkTask).toHaveBeenCalledWith(expect.objectContaining({ projectId: null })))
    })

    it("falls back to today's rules when the settings cannot be read", async () => {
        getWorkTaskSettings.mockRejectedValue(new Error('404'))
        renderDialog()

        expect(await screen.findByLabelText(/Description/)).toBeInTheDocument()
        expect(screen.getByText('Billing')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Create task' })).toBeDisabled() // project and billable still required
    })
```

Add this to `TaskAttachments.test.tsx`, beside the existing 10MB test, and use that test's own render pattern:

```tsx
    it('refuses by the configured limits', async () => {
        const onChange = vi.fn()
        render(
            <StagedTaskAttachments
                files={[]}
                onChange={onChange}
                limits={{ maxFiles: 1, maxBytes: 2 * 1024 * 1024, extensions: ['.pdf'] }}
            />,
        )

        pick(file('big.pdf', 3 * 1024 * 1024), file('shot.png'), file('ok.pdf'))

        const alert = await screen.findByRole('alert')
        expect(alert).toHaveTextContent('big.pdf is larger than the 2MB limit.')
        expect(alert).toHaveTextContent('shot.png: only PDF files can be attached.')
        expect(onChange).toHaveBeenCalledWith([expect.objectContaining({ name: 'ok.pdf' })])
        expect(screen.getByTestId('task-attachment-input')).toHaveAttribute('accept', '.pdf')
    })
```

`pick` and `file` are the helpers already defined at the top of `TaskAttachments.test.tsx`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd client && node_modules/.bin/vitest run src/components/tasks/TaskDialog.test.tsx src/components/tasks/TaskAttachments.test.tsx`
Expected: the new tests FAIL.

- [ ] **Step 3: Parameterise `lib/task-attachments.ts`**

Replace the constants and `taskAttachmentError` with the following. Keep `formatFileSize` and the `TaskAttachmentLimits` interface added in Task 5:

```ts
const KIND_NAMES: Record<string, string> = {
    '.pdf': 'PDF', '.doc': 'Word', '.docx': 'Word', '.xls': 'Excel', '.xlsx': 'Excel', '.jpg': 'JPG', '.jpeg': 'JPG', '.png': 'PNG',
}

/** Today's limits, and the Task Settings defaults: 10 files, 10 MB, PDF, Word, Excel and images. */
export const DEFAULT_ATTACHMENT_LIMITS: TaskAttachmentLimits = {
    maxFiles: 10,
    maxBytes: 10 * 1024 * 1024,
    extensions: ['.pdf', '.doc', '.docx', '.xls', '.xlsx', '.jpg', '.jpeg', '.png'],
}

/** For the file input's `accept`. */
export function acceptFor(limits: TaskAttachmentLimits): string {
    return limits.extensions.join(',')
}

/** "PDF, Word and JPG" — the kinds a limit allows, for messages and helper text. */
export function describeKinds(limits: TaskAttachmentLimits): string {
    const names = [...new Set(limits.extensions.map((e) => KIND_NAMES[e]))]
    return names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

/** Why this file would be refused, or null when it looks acceptable. */
export function taskAttachmentError(file: Pick<File, 'name' | 'size'>, limits: TaskAttachmentLimits = DEFAULT_ATTACHMENT_LIMITS): string | null {
    const dot = file.name.lastIndexOf('.')
    const extension = dot === -1 ? '' : file.name.slice(dot).toLowerCase()
    if (!limits.extensions.includes(extension)) return `${file.name}: only ${describeKinds(limits)} files can be attached.`
    if (file.size > limits.maxBytes) return `${file.name} is larger than the ${Math.round(limits.maxBytes / 1024 / 1024)}MB limit.`
    return null
}
```

The existing message was `"only PDF, Word, Excel, JPG and PNG files can be attached."`. `describeKinds(DEFAULT_ATTACHMENT_LIMITS)` produces `"PDF, Word, Excel, JPG and PNG"` with the extensions in the order above. Check that the existing `TaskAttachments.test.tsx` assertions still match that string.

Remove `MAX_TASK_ATTACHMENTS`, `MAX_TASK_ATTACHMENT_BYTES` and `TASK_ATTACHMENT_ACCEPT`. Then run `grep -rn "MAX_TASK_ATTACHMENT\|TASK_ATTACHMENT_ACCEPT" client/src`. Every hit is in `TaskAttachments.tsx` and is replaced in Step 4.

- [ ] **Step 4: Thread the limits through `TaskAttachments.tsx`**

- `checkFiles(files, alreadyAttached)` becomes `checkFiles(files, alreadyAttached, limits)`. It calls `taskAttachmentError(file, limits)` and compares against `limits.maxFiles`, with the message `` `${file.name}: a task can carry at most ${limits.maxFiles} attachments.` ``.
- `AttachButton` takes an `accept: string` prop and passes it to the `<input accept=...>`, replacing `TASK_ATTACHMENT_ACCEPT`.
- `TaskAttachments` and `StagedTaskAttachments` each take `limits = DEFAULT_ATTACHMENT_LIMITS`. Pass the prop to `checkFiles`, pass `acceptFor(limits)` to `AttachButton`, and replace `MAX_TASK_ATTACHMENTS` with `limits.maxFiles` in `full` and in the "files attached" labels.

- [ ] **Step 5: Make `TaskDialog.tsx` read the settings**

- Import `useWorkTaskSettings` from `'../../lib/task-settings-query'`, and `isShown`, `requirementOf` and `fieldRequirementError` from `'../../lib/task-settings'`. Also import `attachmentLimits` from there and `describeKinds` from `'../../lib/task-attachments'`.
- Near the top of the component, add `const settings = useWorkTaskSettings()` and `const limits = attachmentLimits(settings)`.
- Compute the field error:

```tsx
    const fieldError = fieldRequirementError(settings, {
        description,
        dueDate,
        targetHours: typeof hours === 'number' ? hours : null,
        projectId: projectId === '' ? null : projectId,
        isBillable,
    })
```

- In `canSave`, replace `projectId !== '' &&` and `isBillable !== null &&` with `fieldError === null &&`.
- In `submit`, send `projectId: projectId === '' ? null : projectId` (it was `projectId as number`) and `isBillable` as is (it was `isBillable as boolean`). Change `UpsertWorkTaskRequest` in `lib/types` so that `projectId: number | null` and `isBillable: boolean | null`, if they aren't typed that way already.
- Wrap each configurable field in `isShown(settings, ...)`: Description (`'description'`), the Attachments block (`'attachments'`), Project (`'project'` — always true, but keep the call for symmetry), Billing (`'billable'`), Due date (`'dueDate'`), Target hours (`'targetHours'`) and Priority (`'priority'`).
- Set `required` on each input from the settings instead of hard-coding it:
  - `required={requirementOf(settings, 'description') === 'Required'}` on Description, Due date and Target hours;
  - `FormControl required={requirementOf(settings, 'project') === 'Required'}` for Project;
  - Billing's `FormControl required` stays as it is, because it only renders when shown, and shown means Required.
  - Project's select gets an empty item first when optional: `{requirementOf(settings, 'project') === 'Optional' && <MenuItem value="">No project</MenuItem>}`.
- Pass `limits` to `TaskAttachments` and `StagedTaskAttachments`. Replace the helper text with `` `${describeKinds(limits)}, up to ${settings.maxAttachmentSizeMb}MB each` ``.
- Below the fields, above the actions, show the field error once the user has started filling the form: `{fieldError && trimmedTitle.length > 0 && <FormHelperText error>{fieldError}</FormHelperText>}`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd client && node_modules/.bin/vitest run src/components/tasks src/lib`
Expected: PASS, both the new tests and every existing dialog and attachment test.

- [ ] **Step 7: Type-check**

Run: `cd client && npx tsc -b`
Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add client/src/lib/task-attachments.ts client/src/lib/types client/src/components/tasks/TaskAttachments.tsx client/src/components/tasks/TaskAttachments.test.tsx client/src/components/tasks/TaskDialog.tsx client/src/components/tasks/TaskDialog.test.tsx
git commit -m "Ask the task fields the Task Settings require, and hide the ones they hide

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Tasks page — card, CSV, filters and tile honour the settings

**Files:**
- Modify: `client/src/lib/work-tasks.ts` (`tasksToCsv`, and the `CSV_HEADER` it uses)
- Modify: `client/src/components/tasks/TasksPage.tsx`
- Modify: `client/src/components/tasks/TaskDialog.tsx`: the status select's Done option, when a file is required.
- Test: `client/src/components/tasks/TasksPage.test.tsx`, plus the `tasksToCsv` tests wherever they live (`grep -rn "tasksToCsv" client/src --include=*.test.*`).

**Interfaces:**
- Consumes: `useWorkTaskSettings` (import it from `'../../lib/task-settings-query'`), and `isShown`, `needsAttachmentBeforeDone` and `ATTACHMENT_REQUIRED_MESSAGE` from `'../../lib/task-settings'`.
- Produces: `tasksToCsv(tasks, settings = DEFAULT_TASK_SETTINGS)`, which drops the columns of hidden fields (Description, Priority, Billable, Due date, Target hours) from both the header and the rows.

- [ ] **Step 1: Write the failing tests**

Add these to `TasksPage.test.tsx`, using its existing mocks and render helper. Add `getWorkTaskSettings` to its `lib/api` mock, defaulting to `DEFAULT_TASK_SETTINGS`:

```tsx
    it('holds Done on a card until a required file is attached', async () => {
        getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, attachmentsRequirement: 'Required' })
        // a task the caller is assigned to, InProgress, no attachments — built with the file's task factory
        renderPage([inProgressTaskAssignedToMe({ attachments: [] })])

        const card = await screen.findByTestId('task-card')
        expect(within(card).getByText('File needed before Done')).toBeInTheDocument()
        expect(within(card).getByRole('button', { name: /Mark done|Done/ })).toBeDisabled()
    })

    it('holds Confirm on a waiting task with no file when one is required', async () => {
        getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, attachmentsRequirement: 'Required' })
        renderPage([waitingTaskICanConfirm({ attachments: [] })])

        const card = await screen.findByTestId('task-card')
        expect(within(card).getByRole('button', { name: /Confirm/ })).toBeDisabled()
    })

    it('drops the To Confirm tile when confirmation is off', async () => {
        getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, requireCompletionConfirmation: false })
        renderPageAsManager([])

        await screen.findByTestId('stat-open')
        expect(screen.queryByTestId('stat-confirm')).toBeNull()
    })

    it('hides the priority chip and filter when priority is hidden', async () => {
        getWorkTaskSettings.mockResolvedValue({ ...DEFAULT_TASK_SETTINGS, showPriority: false })
        renderPage([inProgressTaskAssignedToMe({ priority: 'High' })])

        await screen.findByTestId('task-card')
        expect(screen.queryByText('High priority')).toBeNull()
        expect(screen.queryByLabelText('Priority filter')).toBeNull()
    })
```

The factory and render names are placeholders. Use whatever task builder and render helpers `TasksPage.test.tsx` already defines (read its top first), passing overrides the same way.

Add this to the `tasksToCsv` test file:

```ts
    it('leaves hidden fields out of the export', () => {
        const csv = tasksToCsv([task], { ...DEFAULT_TASK_SETTINGS, descriptionRequirement: 'Hidden', showPriority: false })
        const header = csv.split('\r\n')[0]
        expect(header).not.toMatch(/Description/)
        expect(header).not.toMatch(/Priority/)
        expect(header).toMatch(/Title/)
    })
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd client && node_modules/.bin/vitest run src/components/tasks/TasksPage.test.tsx src/lib`
Expected: the new tests FAIL.

- [ ] **Step 3: Make `tasksToCsv` respect hidden fields**

In `lib/work-tasks.ts`, rebuild the header and rows from one column list, so a hidden field drops out of both together:

```ts
export function tasksToCsv(tasks: readonly WorkTask[], settings: WorkTaskSettings = DEFAULT_TASK_SETTINGS): string {
    const columns: { header: string; shown: boolean; value: (t: WorkTask) => string }[] = [
        { header: 'Title', shown: true, value: (t) => csvText(t.title) },
        { header: 'Description', shown: isShown(settings, 'description'), value: (t) => csvText(t.description) },
        { header: 'Department', shown: true, value: (t) => csvText(t.departmentName) },
        { header: 'Project code', shown: true, value: (t) => csvText(t.projectCode) },
        { header: 'Project', shown: true, value: (t) => csvText(t.projectName) },
        { header: 'Status', shown: true, value: (t) => csvText(STATUS_LABELS[t.status]) },
        { header: 'Priority', shown: isShown(settings, 'priority'), value: (t) => csvText(PRIORITY_LABELS[t.priority]) },
        { header: 'Billable', shown: isShown(settings, 'billable'), value: (t) => (t.isBillable == null ? '' : t.isBillable ? 'Yes' : 'No') },
        { header: 'Assignees', shown: true, value: (t) => csvText(t.assignees.map((a) => a.displayName).join('; ')) },
        { header: 'Created by', shown: true, value: (t) => csvText(t.createdByName) },
        { header: 'Due date', shown: isShown(settings, 'dueDate'), value: (t) => t.dueDate?.slice(0, 10) ?? '' },
        { header: 'Target hours', shown: isShown(settings, 'targetHours'), value: (t) => (t.targetHours == null ? '' : String(t.targetHours)) },
        { header: 'Logged hours', shown: true, value: (t) => formatHours(Number(t.loggedHours) || 0) },
        { header: 'Created', shown: true, value: (t) => t.createdAtUtc.slice(0, 10) },
        { header: 'Completed', shown: true, value: (t) => t.completedAtUtc?.slice(0, 10) ?? '' },
    ]
    const shown = columns.filter((c) => c.shown)
    return [shown.map((c) => c.header).join(','), ...tasks.map((t) => shown.map((c) => c.value(t)).join(','))].join('\r\n')
}
```

Before replacing it, open the existing `CSV_HEADER` constant and copy its header strings **exactly** into the `header` fields above. The names written here are guesses, and the existing CSV tests pin the real ones. Then delete `CSV_HEADER` if nothing else uses it. Import `isShown` and `DEFAULT_TASK_SETTINGS` from `./task-settings`, and the type `WorkTaskSettings` from `./api/work-task-settings`.

- [ ] **Step 4: Honour the settings on `TasksPage.tsx`**

- In the page component, add `const settings = useWorkTaskSettings()`.
- Download button: `downloadTasksCsv(visible, settings)`. Change `downloadTasksCsv` (line ~51) to take `settings` and pass it to `tasksToCsv`.
- Stats: render the `stat-confirm` tile only when `manages && settings.requireCompletionConfirmation`. Adjust the grid column count the same way: `(isHr ? 3 : 4) + (manages && settings.requireCompletionConfirmation ? 1 : 0)`.
- Priority filter: render the `SelectFilter` with `ariaLabel="Priority filter"` only when `isShown(settings, 'priority')`.
- Pass `settings` into `TaskCard` as a new prop. Inside the card:
  - render the Billable chip only when `isShown(settings, 'billable')`;
  - render the priority chip only when `isShown(settings, 'priority')`;
  - render the description summary line only when `isShown(settings, 'description')`;
  - render the attachment-count line only when `isShown(settings, 'attachments')`;
  - render the Due and Target stat blocks only when their fields are shown.
  - Compute `const fileNeeded = !closed && needsAttachmentBeforeDone(settings, attachmentCount)`.
  - When `fileNeeded`, render `<Box sx={{ fontSize: 11, color: 'warning.dark' }}>File needed before Done</Box>` above the footer, with `title={ATTACHMENT_REQUIRED_MESSAGE}`.
  - Pass `doneBlocked={fileNeeded}` to `ReviewControls`, which disables Confirm when true, and to `StatusControls`, which disables the main button when `next.to === 'Done'` and disables the menu's Done item.
- In `TaskDialog.tsx`'s status select, disable the `Done` menu item when `needsAttachmentBeforeDone(settings, attachments.length)`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd client && node_modules/.bin/vitest run src/components/tasks`, then `cd client && node_modules/.bin/vitest run src/lib`
Expected: PASS.

- [ ] **Step 6: Type-check and lint**

Run: `cd client && npx tsc -b && npm run lint`
Expected: no errors.

- [ ] **Step 7: Commit**

```bash
git add client/src/lib/work-tasks.ts client/src/components/tasks/TasksPage.tsx client/src/components/tasks/TasksPage.test.tsx client/src/components/tasks/TaskDialog.tsx
git add $(git ls-files client/src --modified | grep -i "work-tasks.*test")
git commit -m "Honour the Task Settings on the task cards, filters, tile and CSV

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Document it and verify end to end

**Files:**
- Modify: `CLAUDE.md`: add a **Task Settings** paragraph at the end of the **Tasks** bullet under "The two administrators are disjoint", and add a `WorkTaskSettings` row to the Domain Model table.

- [ ] **Step 1: Write the CLAUDE.md paragraph**

Append this to the Tasks bullet:

```markdown
**Task Settings** (`WorkTaskSettings`, one row inserted by migration `AddWorkTaskSettings` with today's behaviour; `/admin/task-settings`, `TaskSettingsPanel`, sidebar Configuration → Task Setting): the System Administrator — who does not see the tasks — decides how they work for everyone. `GET /api/worktasksettings` is open to anyone signed in (every task dialog reads it), `PUT` is System Administrator only (`SystemAdministrationSurfaceTests`). Per field a `FieldRequirement`: Description, Due date, Target hours and Attachments take Required/Optional/Hidden; Project only Required/Optional (a timesheet row takes its project from the task); Billable only Required/Hidden (a yes/no is asked or not); Priority is shown or not. Title and Department are always required. `WorkTaskFieldRules` is the rule, called from `CreateWorkTask`/`UpdateWorkTask`, mirrored by `lib/task-settings.ts` with the same messages. **A hidden field is the one exception to `UpsertWorkTaskRequest` being a full replace**: on edit the stored value is kept whatever the request carries (nobody can see it to clear it, and hide → unhide loses nothing); on create it is stored as nothing. A field made Required holds older tasks to it on their next save, as `EmploymentStartDate` does. **A required attachment gates completion, not creation** — the create dialog uploads after the task exists — so `UpdateWorkTaskStatus` refuses Done and Confirm on a task with no file (`AttachmentRequiredMessage`), and the card shows "File needed before Done". `RequireCompletionConfirmation` off makes every Done close directly; switching it off sweeps every `AwaitingConfirmation` task to Done in the same save (no confirmer, send-back note cleared, nobody emailed), switching it on moves nothing. Attachment count (1–20), size (1–10 MB) and kinds narrow `StoreFile`'s `TaskAttachment` policy through `AcceptedKindsOverride`/`MaxSizeBytesOverride` and apply to new uploads only. The client reads the settings under `['work-tasks', 'settings']` via `useWorkTaskSettings`, which reads a missing endpoint as today's defaults. `WorkTaskSettingsTests`, `WorkTaskFieldRuleTests`, `WorkTaskCompletionSettingsTests`, `TaskSettingsPanel.test.tsx` and `task-settings.test.ts` pin it.
```

Add this row to the Domain Model table after `WorkTask`:

```markdown
| `WorkTaskSettings` | One row (`Id` 1), inserted by migration: a `FieldRequirement` per configurable task field, `ShowPriority`, `RequireCompletionConfirmation`, `MaxAttachmentsPerTask`, `MaxAttachmentSizeMb`, `AllowImages`/`AllowPdf`/`AllowWord`/`AllowExcel`. Read through `WorkTaskSettingsStore.LoadAsync`, which returns the defaults when the row is missing. See **Task Settings** under the Tasks bullet |
```

- [ ] **Step 2: Run the full server suite**

Run: `dotnet test Tests/WorkTrack.Tests`
Expected: all PASS. Record the count.

- [ ] **Step 3: Run the client tests in batches**

Run each of these and check its output. Per memory, the exit code alone doesn't prove anything:
- `cd client && node_modules/.bin/vitest run src/lib`
- `cd client && node_modules/.bin/vitest run src/components/tasks`
- `cd client && node_modules/.bin/vitest run src/components/admin`
- `cd client && node_modules/.bin/vitest run src/components/layout`

Expected: every batch reports all tests passing.

- [ ] **Step 4: Check it in the running app**

Restart the API; memory warns that a stale API serves old code. Start `npm run dev`. Then:
1. Sign in as `systemadmin@`. Open **Task Settings** from the sidebar, set Due date to Required and Attachments to Required, and save.
2. Sign in as a Manager. On `/tasks`, New task must hold Create until a due date is set. On a task with no file, the assignee's card must show "File needed before Done" with Done disabled.
3. As the System Administrator, switch confirmation off. A task waiting for confirmation must now read Done.

Report what was observed. If any step can't be run, say so rather than claiming it passed.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "Document Task Settings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
