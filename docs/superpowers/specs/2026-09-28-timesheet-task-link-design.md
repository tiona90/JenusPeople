# Timesheet entries logged against tasks — design

Date: 2026-09-28

## Goal

A person filling in a timesheet can say which of their tasks a row's hours were
spent on. The task then shows how much of its `TargetHours` has been used and how
much is left, so a Manager can see progress without asking.

Today `WorkTask.TargetHours` is "quoted only — timesheet entries are not linked to
tasks". This reverses that.

## Decisions (agreed)

| Question | Answer |
|---|---|
| Who gets the Task picker | Every task assignee — Managers and Employees alike (HR Administrators are never assignees) |
| Which hours count | Every saved entry, whatever its timesheet's status (Draft, Submitted, Approved, Rejected, Resubmitted). Only deleting the entry, or unlinking it, removes the hours |
| Task ↔ project | The task narrows by the row's project; picking a task on a row with no project fills the project in; the server refuses a task whose project differs from the row's |
| Over target | Allowed. Never blocks a save; the card shows "Nh over" |

## 1. Data model

- `TimesheetEntry.WorkTaskId` — `int?`, FK to `WorkTask`, `OnDelete(SetNull)`.
  Deleting a task leaves the hours on the timesheet and drops only the link.
  Nullable because every existing row has none, and linking is optional.
- Migration `LinkTimesheetEntriesToTasks`: the column, index, FK. No backfill.
- `WorkTask.TargetHours` doc comment rewritten: it is measured against linked
  entries.
- Deleting a user: entries go with their timesheets as today; nothing new.
  `DeleteWorkTask` needs nothing beyond the SetNull. Check that
  `DeleteAdminUser` / `CleanupUserDependencies`, which delete a leaver's own tasks,
  don't trip the FK on SQL Server. SetNull covers them, so confirm it with a SQLite
  test.

## 2. Validation — `TimesheetEntryTaskRule`

New pure rule in `Application/Timesheets/Support/`, called from
`TimesheetEntriesController.AddEntry` and `UpdateEntry` next to
`TimesheetEntryValidator`. It runs **only when the entry's `WorkTaskId` is new or
changed**: on add with a task, or on update where the stored task differs. An
entry keeping its task is never re-checked, so a task marked Done or an assignee
removed later does not invalidate past entries (the same "check only what changed"
shape as `WorkTaskAssigneeRule`).

When it runs, it refuses:

1. a task that does not exist, with the same message as not being assigned, so a
   probe can't tell a task exists — "That task is not one of yours.";
2. a task the **timesheet owner** is not assigned to. This is the owner, not the
   caller, since an HR Administrator may write on somebody's behalf —
   "That task is not one of yours.";
3. a task not in `ToDo`/`InProgress` — "That task is closed.";
4. a task whose `ProjectId` is not the entry's `ProjectId` — "That task belongs to
   another project."

Owner = `Timesheet.EmployeeId` → the `EmployeeProfile`'s `UserId` (check which id
`Timesheet.EmployeeId` holds when implementing).

The same-day uniqueness key (project + type + component) is unchanged. The task is
not part of it.

**Copying.** `GenerateDraft` and the client's "Copy to rest of week" carry
`WorkTaskId` only when the task is still open and the owner still assigned.
Otherwise the copy has no task. On the client, the options list decides this: a
task not in the list is dropped when copying.

## 3. Measuring against the target

- `WorkTaskDto.LoggedHours` (`decimal`) — the sum of `HoursWorked` over every
  `TimesheetEntry` with that `WorkTaskId`, in any timesheet status. Computed in
  `WorkTaskProjection` on every read and never stored.
- New query `GetTimesheetTaskOptions` behind
  `GET /api/worktasks/timesheet-options?timesheetId={id}`. The caller must be able
  to read that timesheet (`TimesheetAccess`/scope, same as the entries), otherwise 404.
  It returns, for the timesheet's owner:
  - every open task they are assigned to, plus
  - any task already referenced by an entry on this sheet (so an existing row
    never opens blank), flagged `IsClosed` when it is no longer open.

  Each item: `Id`, `Title`, `ProjectId`, `ProjectCode`, `TargetHours`,
  `LoggedHours`, `IsClosed`.

  The class-level gate on `WorkTasksController` admits HR, Manager and Employee,
  which covers every timesheet writer. A System Administrator files no timesheet
  and needs no options.
- `TimesheetEntry` as returned by the timesheet detail carries `WorkTaskId`, and a
  `WorkTaskTitle` for display (the detail's entry projection; check whether it
  returns raw entities or a DTO when implementing).

## 4. Client

**Timesheet editor (`NewTimesheetPage`)**
- A **TASK** column sits between Project and Component. It is optional; the first
  option is "No task".
- Options: `timesheet-options` filtered to `projectId === row.projectId`, or all of
  them when the row has no project yet. Label: `PAY-002 · sdf — 16h left`, or
  `— 4h over`, or `— 8h logged` with no target. A closed task already on the row is
  shown as `(closed)` and cannot be picked again once removed.
- Picking a task on a row with no project sets the project. Changing the project
  clears a task whose project no longer matches.
- Query key `['work-tasks', 'timesheet-options', timesheetId]`. Saving an entry
  invalidates `['work-tasks']` so the remaining figures refresh.
- `lib/api` gains `getTimesheetTaskOptions(timesheetId)`, and the entry types gain
  `workTaskId?: number | null`.

**Task card (`TasksPage`)**
- TARGET tile: `24h`, subtitle `8h logged · 16h left`. When over: `4h over` in the
  error colour. No target: tile value `—`, subtitle `8h logged` when anything is
  logged.
- The wording lives in one helper in `client/src/lib/work-tasks.ts`
  (`describeTaskProgress(target, logged)`), used by both the card and the picker
  label.

**Breakdown (`TimesheetDailyBreakdown`)** — shows the task title beside each
entry that has one.

## 5. Tests

Server (`Tests/WorkTrack.Tests/`):
- `TimesheetEntryTaskRuleTests`: each refusal; unchanged task not re-checked;
  closed task kept on an existing row passes; HR writing on behalf is checked
  against the owner.
- `WorkTaskLoggedHoursTests`: sums across statuses and sheets; zero with no
  entries; deleting the task nulls the entries (SQLite, `TransactionalTestDb`).
- `GetTimesheetTaskOptionsTests`: open assigned tasks only, plus tasks already on
  the sheet; out-of-scope timesheet → 404.
- `GenerateDraft` drops a closed or unassigned task.

Client:
- `NewTimesheetPage` — picker narrows by project; picking a task fills the project;
  changing the project clears the task; the payload carries `workTaskId`.
- `TasksPage` — "left" / "over" / no-target wording.
- `work-tasks` — `describeTaskProgress` unit tests.

## 6. Docs

In the CLAUDE.md `WorkTask` row, replace "quoted only — timesheet entries are not
linked to tasks" with a short description of the link and the rule's location, and
add `WorkTaskId` to the `TimesheetEntry` row.

## Out of scope

- A per-assignee breakdown of logged hours.
- Blocking saves over target, or letting HR set target consumption by hand.
- Moving `TimesheetEntriesController` onto MediatR (a noted exception in CLAUDE.md;
  unrelated to this change).
