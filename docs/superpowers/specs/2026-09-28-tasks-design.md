# Tasks — design

Date: 2026-09-28 · Status: approved in chat, awaiting spec review

## Goal

Managers and HR Administrators need a place to hand each other follow-ups and see them
through — "chase the missing sick notes in Sales", "review the new starter's timesheet
setup". Nothing in the app records that today. A **Tasks** page gives them assignable
to-dos, scoped to departments the same way leave and timesheets are.

## Decisions (from the conversation)

| Question | Answer |
|---|---|
| What a task is | An assignable to-do: created by one Manager/HR Administrator, assigned to one, tracked to done. |
| Who uses it | Managers and HR Administrators only. System Administrators and Employees neither open the page nor appear as assignees. |
| Visibility | Department-scoped. Every task names one department; everyone whose scope covers it sees it. |
| Department / assignee rule | Department required, chosen from the creator's scope. Assignee must be an active Manager or HR Administrator whose scope includes that department. |

## Assumptions (confirmed in chat)

- **Fields:** Title (required, ≤ 200), Description (optional, ≤ 2000), Department
  (required), Assignee (required), Due date (optional, calendar date), Priority
  (`Low`/`Normal`/`High`, default `Normal`), Status. Audit: `CreatedById`,
  `CreatedAtUtc`, `UpdatedAtUtc`, `CompletedAtUtc`.
- **Statuses:** `ToDo` (0) → `InProgress` (1) → `Done` (2), plus `Cancelled` (3). Any
  move between them is allowed (reopening a Done task is ordinary). `CompletedAtUtc`
  is set on entering `Done` and cleared on leaving it.
- **Who may do what:**
  - **Status** — the creator or the assignee.
  - **Edit details / reassign / delete** — the creator only.
  - **Everyone else in scope** — read-only.
- **Scope is read, not stored.** Losing a department (an HR Administrator's
  `UserDepartment` row removed, a Manager moving department) removes its tasks from
  view on the next read — *including* tasks the caller created or is assigned. Scope
  wins; there is no "mine" exception, so the rule is one sentence.
- **Notifications (v1):** an email to the assignee when a task is created for them or
  reassigned to them (not when they assign it to themselves; honours
  `EmailNotificationsEnabled`). A SignalR `notificationsUpdated` to
  `NotificationsHub.DepartmentManagerGroup(task.DepartmentId)` after every write —
  Managers and HR Administrators already join those groups for their scope. No
  reminders, no bell entry, no overdue digest.

## Backend

**Entity** — `Domain/WorkTask.cs` (named `WorkTask`, not `Task`, to avoid
`System.Threading.Tasks.Task` in every handler), enums `WorkTaskStatus` and
`WorkTaskPriority`. Foreign keys: `DepartmentId` → `Department` (`Restrict`),
`AssigneeId` and `CreatedById` → `User` (`Restrict`). Index on
`(DepartmentId, Status)`. Migration `AddWorkTasks`.

**Cleanup traps (from CLAUDE.md):**
- Both `User` FKs are `Restrict`, so `DeleteAdminUser.CleanupUserDependenciesAsync`
  and `DbInitializer.CleanupUserDependencies` must handle them: tasks the leaver
  **created** are deleted; tasks **assigned** to them (created by somebody else) are
  reassigned to their creator, so the work isn't lost. A SQLite test pins it.
- `DeleteDepartment` must refuse a department that still has tasks (listed as a
  blocker beside the existing ones), rather than dying on the FK.

**Handlers** — `Application/WorkTasks/`, each injecting `AppDbContext` and resolving
scope through `ManagerAccessScopeResolver`:

| Handler | Notes |
|---|---|
| `Queries/GetWorkTaskList` | Filters: `View` (`AssignedToMe`/`CreatedByMe`/`All`), `Status?`, `DepartmentId?`. Always restricted to `ManagedDepartmentIds`. Projects to `WorkTaskDto` (names resolved, `CanEdit`, `CanChangeStatus` computed for the caller). Sorted: open first, then due date (nulls last), then priority desc, then created. |
| `Queries/GetWorkTaskAssignees` | Active Managers/HR Administrators whose scope includes a given department (in the caller's scope). Feeds the dialog's picker. |
| `Commands/CreateWorkTask` | Validates department in scope and assignee eligible; emails assignee. |
| `Commands/UpdateWorkTask` | Creator only; full replace of the editable fields (title, description, department, assignee, due date, priority); re-checks eligibility; emails a new assignee. |
| `Commands/UpdateWorkTaskStatus` | Creator or assignee; stamps `CompletedAtUtc`. |
| `Commands/DeleteWorkTask` | Creator only. |

A task outside the caller's scope reads as **not found** (404), not forbidden, so the
API does not confirm its existence. Business refusals return `Result.Failure` with
named messages (`NotCreatorMessage`, `NotParticipantMessage`,
`DepartmentOutOfScopeMessage`, `AssigneeNotEligibleMessage`). FluentValidation covers
shape (lengths, enum values, required fields).

"Eligible assignee" is one helper, `WorkTaskAssigneeRule`, used by create, update and
the assignees query: active user, role Manager or HR Administrator, and the
department is in *their* resolved scope.

**Controller** — `API/Controllers/WorkTasksController.cs`, thin, class-level
`[Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]`, route `api/worktasks`:
`GET /`, `GET /assignees?departmentId=`, `POST /`, `PUT /{id}`,
`PATCH /{id}/status`, `DELETE /{id}`. Sends the SignalR event after a successful
write. `SystemAdministrationSurfaceTests` gains a row pinning the gate.

**Email** — `Application/WorkTasks/WorkTaskAssignmentNotification.cs`, sent through
`IEmailService`: subject "New task: {title}", body with department, due date,
priority, the creator's name and the description. Failure is logged, never fails the
request.

## Client

- **Roles:** `TASK_ROLES = ['HR Administrator', 'Manager']` in `lib/roles.ts`, mirroring
  `AppRoles.LeaveAndTimeDecisionRoles`.
- **Route:** `/tasks` in `App.tsx` behind `<ProtectedRoute roles={[...TASK_ROLES]} />`;
  `uiStore.navigateToTasks()`; Topbar title "Tasks".
- **Sidebar:** "Tasks" (`TaskAltRounded` icon) under **My Team** for a Manager, after
  Approvals; in the HR Administrator's Leave & Time section likewise.
- **API module:** `lib/api/work-tasks.ts` + types in `lib/types/work-task.ts`. React
  Query keys under `['work-tasks', …]`; `App.tsx`'s `notificationsUpdated` handler
  invalidates that prefix.
- **Page:** `components/tasks/TasksPage.tsx`:
  - Tabs **Assigned to me** / **Created by me** / **All in my departments**, with counts
    of open tasks.
  - Filters: status (default "Open" = To do + In progress), department (hidden when
    the caller has one).
  - Rows: title, department chip, assignee, due date (red when overdue and open),
    priority chip, status select (enabled only when `canChangeStatus`), row menu
    Edit/Delete (only when `canEdit`).
  - **New task** button → `TaskDialog` (create and edit): department select first,
    then the assignee picker loads eligible people for it; changing department clears
    an assignee no longer eligible. Validation mirrors the server's lengths.
  - Empty state per tab.

## Testing

- **Server** (`Tests/WorkTrack.Tests/WorkTasks/`, against `TransactionalTestDb`):
  scope filtering per view; department-out-of-scope and ineligible-assignee refusals;
  creator-only edit/delete; creator-or-assignee status; `CompletedAtUtc` stamping;
  404 for out-of-scope ids; assignment email sent / not sent to self; user-delete
  cleanup; department-delete blocker; controller role gate.
- **Client** (Vitest): `TasksPage.test.tsx` — tabs, filters, overdue styling, disabled
  controls by permission flags; `TaskDialog.test.tsx` — assignee reload and clear on
  department change, validation; sidebar shows Tasks for Manager and HR only.

## Out of scope

Comments on tasks, attachments, recurring tasks, reminders/overdue digest, bell
entries, employees as assignees, linking a task to a leave or timesheet.
