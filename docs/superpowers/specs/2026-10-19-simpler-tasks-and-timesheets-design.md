# Simpler tasks and timesheets

**Date:** 2026-10-19
**Status:** Draft for review. Nothing here is implemented.

## The problem

An Employee with two tasks opens a Tasks page carrying six stat tiles, a search box,
two filters, a three-way layout toggle, and cards that each show a project code, a
title, a status pill, a department chip, a priority chip, a billable chip, the project
name, a description, avatars, a notice band, three dated facts and an action footer.

The same Employee, to record an afternoon on one of those tasks, opens a separate
"Submit Timesheet" page, expands the day, adds a row, and answers six questions —
Task, Project, Type, Component, Activity, Hours — plus a note, even though picking
the Task already answers Project, and the Type, Component and Activity catalogues
may be empty or leave the project only one choice.

Both features were built for the Manager and HR Administrator who run them. The
Employee, who uses them every day, gets the same surface with nothing removed.

## What "simpler" means here

Three decisions, taken for the whole design:

1. **Simplify for the Employee first.** Managers and HR Administrators keep every
   tile, layout and column they have. What comes off is what carries no information
   for the person looking at it: a department chip for someone with one department, a
   "Normal priority" chip on every card, a picker with one option.
2. **Remove from the view, not from the model.** No column, entity or setting is
   dropped. Every stored value is still stored and still editable where it is edited
   today. The Task Settings page keeps every switch it has.
3. **Make the task the thing the Employee touches.** A task already knows its
   project, its billability and its target hours, and a timesheet row already points
   at a task. Logging hours should start from the task, and the timesheet should fill
   itself in from there as far as it can.

## Scope and phasing

This is four pieces of work. Each is independent, each gets its own implementation
plan, and they are ordered by value over cost:

| Phase | What | Where |
|---|---|---|
| 1 | A quieter Tasks page for Employees | client only |
| 2 | The task fills the timesheet row; unused columns disappear | client only |
| 3 | Log time from the task card | server + client |
| 4 | One Timesheets entry in the sidebar | client only |

Two ideas are **deliberately not in scope** and recorded at the end: a weekly grid
replacing the day-by-day editor, and a Timesheet Settings page.

---

## Phase 1 — A quieter Tasks page for Employees

### Who this applies to

"Employee" below means a viewer for whom `canManageTasks` is false: someone who
works the tasks they are given and runs their own. The Manager's and the HR
Administrator's page is unchanged by this phase except for the card rule in 1.3,
which applies to everybody because the chips it hides are redundant for everybody.

### 1.1 No stat tiles

The Employee's tile row (Open Tasks, Overdue, Done This Month, From Manager & HR) is
replaced by one summary line at the left of the toolbar, built from the same
`stats` object the tiles read:

> **2 open** · 1 overdue · 1 waiting for confirmation

Parts that are zero are left out ("2 open" alone when nothing is overdue or
waiting). "waiting for confirmation" appears only when the Task Settings require
confirmation. Done-this-month is dropped from the Employee's view; it is a
Manager's measure. The `StatCard` components stay for the Manager and HR.

### 1.2 No layout toggle

The Cards / List / Board toggle is rendered for Managers and HR only. An Employee
sees Cards. The `tasks-layout` localStorage key is still read and written for the
roles that have the toggle; for an Employee it is ignored (not deleted — a promotion
to Manager restores their old choice).

### 1.3 A thinner card

Rules applied in `TaskCard`, for every viewer:

- **Department chip**: shown only when the viewer manages tasks (`canManageTasks`).
  An Employee's tasks are all in their own department.
- **Priority chip**: shown only when `showPriority` is on **and** the priority is not
  `Normal`. "Normal" is the default state and says nothing.
- **Billable chip**: shown only when the field is shown **and** the task is billable.
  "Non-billable" is dropped from the card; the details dialog still states it.
- **Facts row**: Due and Target as today. The third fact ("Created Sep 29 by HR") is
  dropped from the card; for a Done task it is replaced by "Completed {date}" as
  today. "Created by" moves to the details dialog, which already opens on click. The
  section header ("From your manager & HR", "Created by you") already says who the
  task came from.
- **Project name** under the chips stays. It is the one line that tells two tasks on
  different projects apart.

Nothing else on the card changes: title, status pill, description (two lines),
assignees, attachments count, notices and the action footer are as today.

### 1.4 Priority filter

The "Any priority" filter is hidden when `showPriority` is off, for every viewer.
(Verify during implementation whether this is already the case; if so, no change.)

### Testing

`TasksPage.test.tsx`: an Employee sees the summary line and no tiles, no layout
toggle; a Manager still sees both. Card tests: department chip absent for an
Employee and present for a Manager; "Normal priority" absent and "High priority"
present; "Non-billable" absent and "Billable" present; "Created … by" absent on an
open card. The existing tests that look for those strings are updated to the new
rule rather than deleted.

---

## Phase 2 — The task fills the timesheet row

This phase changes the editor in `NewTimesheetPage.tsx` only. No API changes.

### 2.1 Column visibility follows catalogue use

Each of the three narrowing columns — Type, Component, Activity — is shown on the
sheet **only when its catalogue has two or more active entries**. An organisation
that never set up components does not see a Component column. An organisation with
one component has nothing to choose, so it does not see the column either; the single
value is still written (2.2).

The decision is made once per sheet, not per row or per day, so the grid and its
header strip stay aligned. `TASK_GRID` and `TASK_HEADERS` become functions of which
columns are shown.

### 2.2 A single option is filled in, not asked

Per row, after the project is chosen (by hand or by picking a task):

- For each of Type, Component and Activity, compute the options the project leaves
  (`typeOptionsFrom`, `componentOptionsFor`, `activityOptionsFor` — the helpers that
  already feed the pickers).
- If exactly **one** option remains and the field is empty, set it.
- If the column is shown and the project leaves one option, the picker renders
  **disabled** with that value, so the user sees what will be stored but is not asked.
- If the column is hidden and the catalogue has exactly one active entry, that
  entry is written silently.
- If the project leaves zero options (unclassified project, empty catalogue), the
  field stays empty, as today.

A value already on the row is never overwritten by this rule — it fills blanks only.
Changing the project re-runs it after the existing retention logic
(`retainedProjectId` and friends) has decided what survives the change.

### 2.3 What an Employee sees afterwards

In the common case (catalogues empty or the project narrows them to one) a row is:

> Task · Project · Hours · ✕
> What did you work on?

Picking a task fills the project, as today. The Employee answers two things.

### 2.4 Wording

The editor calls its rows "tasks", which now collides with the Tasks page the first
column points at. User-facing copy changes: "+ Add task" → "+ Add entry", "Please fix
the highlighted tasks" → "…entries", "Add at least one task before submitting" →
"…one entry". Internal names (`Task`, `onAddTask`) can stay or be renamed at the
implementer's discretion; a rename is not required for the behaviour.

### Testing

There is no `NewTimesheetPage.test.tsx` today; this phase adds one. Cases: with an
empty component catalogue the Component header and picker are absent; with two
components they are present; a project carrying one type shows the Type picker
disabled with that type and saves it; a project carrying two leaves the picker
enabled and empty; a stored legacy value outside the options still displays; the
header strip has the same number of columns as a row.

---

## Phase 3 — Log time from the task card

### 3.1 The command

`POST /api/worktasks/{id}/log-time`, gated like the rest of `WorkTasksController`
(`LeaveAndTimeRoles`). Body:

```json
{ "date": "2026-10-19", "hours": 2, "notes": "optional" }
```

Handler `Application/WorkTasks/Commands/LogTaskTime.cs`, injecting `AppDbContext`
like every other handler. It:

1. Loads the task; 404 if it is outside the caller's visibility (same rule as
   `GetWorkTask`). Refuses unless the caller is an **assignee** of the task
   (`NotAnAssigneeMessage`) — a creator who is not on the task does not log hours
   on it. Refuses a `Done` or `Cancelled` task (`TaskClosedMessage`);
   `AwaitingConfirmation` is allowed, since the status counts as open for logging.
2. Validates `date` is not in the future and `hours` is a multiple of 0.5 between
   0.5 and 24.
3. Finds the caller's timesheet whose period contains `date` (weeks run Monday to
   Sunday; `PeriodStart` is the Monday). If none, creates one in `Draft` with the
   caller's `DepartmentId`, the way `CreateTimesheet` does. If one exists and is
   `Submitted`, `Approved` or `Resubmitted`, refuses with
   `WeekSubmittedMessage` ("This week's timesheet has already been submitted. Open it
   from Timesheets to change it."). `Draft` and `Rejected` are writable, matching the
   editor's `isEditable`.
4. Resolves the entry's project from the task (`WorkTask.ProjectId`; a task with no
   project cannot be logged against — `TaskHasNoProjectMessage`). Fills Type,
   Component and Activity by the Phase 2 rule: the one option the project leaves, or
   null.
5. Looks for an existing entry on that date **for this task** (same `WorkTaskId`).
   If found, adds the hours to it and appends the note (newline-separated) if one
   was given. Otherwise creates a new entry with `WorkTaskId` set.
6. Runs `TimesheetEntryValidator.Validate` and `TimesheetEntryTaskRule.CheckAsync`
   exactly as `TimesheetEntriesController.AddEntry` does, so the 24-hour day cap and
   the "owner's open task on this project" rule hold. Recalculates `TotalHours`.
7. Returns `{ timesheetId, entryId, hoursOnEntry, hoursOnDay, timesheetStatus }`.

Emails nobody. Fans `notificationsUpdated` out as other task writes do, so the
card's logged-hours figure refreshes for anyone watching.

### 3.2 The entry's task becomes part of its identity

Today two entries with the same project, type and component on one date are an
overlap, whatever task they point at. That blocks logging two tasks on the same
project on the same day with no type or component — exactly the Phase 2 common
case. `TimesheetEntryValidator`'s overlap rule gains one condition: rows whose
`WorkTaskId`s differ (including one null and one set) are two facts, not a
duplicate. Rows that both leave the task blank keep today's rule.

This is a server rule change and applies to the editor's own saves as well. The
client-side mirror, if the editor has one for overlaps, changes with it.

### 3.3 The control

On `TaskCard`'s action footer and in `TaskDetailsDialog`, for a viewer who is an
assignee of an open task: a **Log time** button (clock glyph). It opens a small
popover anchored to the button, not a dialog:

> Date [today ▾]   Hours [1.0]   Note [optional]   **Log**

Date defaults to today and cannot be after today. Hours default to 1, step 0.5,
min 0.5, max 24. On success the popover closes and a snackbar reads

> 2h logged to the week of 13 Oct · 3h on Mon 19 — **Open timesheet**

where the link navigates to `/new-timesheet` for that week. On refusal the popover
shows the server's message inline (the week is submitted, the task has no project,
the day would pass 24 hours). The mutation invalidates `['work-tasks']` and
`['timesheets']`, so "1.3h logged · 0.7h left" on the card updates.

`lib/api/work-tasks.ts` gains `logTaskTime(id, body)`. `TaskSurface`-style tests
pin the route's gate.

### Testing

Server, `LogTaskTimeTests` over `TestDb`: creates a Draft sheet when none exists;
adds to an existing entry for the same task and date; creates a second entry when
the date already has an entry for a different task on the same project (the 3.2
rule); refuses a submitted week, a future date, a non-assignee, a closed task, a
task with no project, and a day passing 24 hours; fills a single Type when the
project carries one. `TimesheetEntryValidatorTests` gain the 3.2 cases. Client:
the button appears for an assignee on an open task and not for a bystander or on
a Done task; a successful log shows the snackbar and refreshes the card.

---

## Phase 4 — One Timesheets entry in the sidebar

### 4.1 Sidebar

For Employees and Managers, "Submit Timesheet" is removed and "My Timesheets" is
renamed **Timesheets**, pointing at `/timesheets` as today. The HR Administrator's
sidebar does not have the pair and is unchanged.

### 4.2 Where "Submit" went

`MyTimesheetPage` already renders a "This week" stat ("No timesheet started" when
there is none). It gains a primary **Open this week** button beside the stats
(label "Start this week" when no sheet exists, "Continue this week" for a Draft or
Rejected one, "View this week" otherwise) that navigates to `/new-timesheet`. Each
row in the history list already opens the editor for its week.

The `/new-timesheet` route stays, so deep links, the dashboard's shortcuts and the
Phase 3 snackbar link keep working.

### Testing

`Sidebar.test.tsx`: an Employee's and a Manager's sidebar list "Timesheets" once and
"Submit Timesheet" not at all. A `MyTimesheetPage` test: the button label follows
the week's status and navigates to the editor.

---

## Not in this design, and why

- **A weekly grid** (rows = task or project, columns = Mon–Fri) in place of the
  day-by-day accordion. It is the classic timesheet shape and would remove the
  "Copy to rest of week" workaround, but it is a rewrite of a 1,356-line page with
  no test file. Phase 3 takes most of the daily pain away by letting hours be logged
  from the task; revisit the grid after it has been used for a while.
- **A Timesheet Settings page** (Type / Component / Activity each Required, Optional
  or Hidden). Phase 2 hides a column whenever the organisation has not populated
  its catalogue, which covers the case the setting would exist for, with nothing
  for a System Administrator to configure.
- **Changing the Task Settings defaults** (Priority off, Billable hidden,
  confirmation off). The row already exists in every database with today's values,
  and the seeder does not run on the deployed host, so a default change would be a
  data migration that alters production behaviour unasked. The System Administrator
  can set those three today. Phase 1 makes the defaults quieter without touching
  them.
- **Merging "My Leave" and "Request Leave"**, the same shape as Phase 4. Out of
  scope: this design is tasks and timesheets.

## Rules this design must keep

- Server rules stay authoritative; every client mirror may under-refuse, never
  over-refuse (the `AttachmentPolicyRule` / `attachment-policy.ts` pattern).
- Full-replace semantics on `UpsertWorkTaskRequest` are untouched; nothing here
  edits a task.
- The timesheet notification bell keeps polling the plain `['timesheets']` key and
  never asks for attendance; Phase 3's invalidation uses the prefix, as the editor does.
- Timesheet dates compare on the first ten characters (`isoDateOnly`); Phase 3's
  "week containing date" is computed server-side on `DateOnly`, never from a local
  midnight.
