# Task confirmation — design

Date: 2026-09-29

## Goal

When a Manager or HR Administrator hands a task to somebody, the assignee's "Done"
does not close it. The task waits until a reviewer confirms the work, or sends it
back with a reason. Only a confirmed task is Done.

Today any assignee moves a task straight to Done (`UpdateWorkTaskStatus`), and
`CompletedAtUtc` is stamped with nobody having looked at the work.

## Decisions (agreed with the user)

- **Who confirms:** the task's creator, or any Manager or HR Administrator whose
  `ManagerAccessScopeResolver` scope covers the task's department.
- **Nobody confirms work they are assigned to** — except the creator, who owns the
  task. A Manager on the task cannot confirm it even though it is in their scope.
- **Send back needs a reason**, is stored and shown on the card, and emails the
  assignees.
- **Who is emailed when a task starts waiting:** the creator only. If the creator is
  deactivated, the reviewers covering the department instead.
- **Bell:** Managers and HR Administrators see the tasks waiting for their
  confirmation in the Topbar bell.
- **Approach:** a new status, not a flag on Done, so that Done keeps meaning
  "finished" everywhere it is already read.

## Status

`WorkTaskStatus.AwaitingConfirmation = 4`, appended so stored values keep their
meaning. Existing Done tasks stay Done; nothing is backfilled.

## The rule — `Application/WorkTasks/Support/WorkTaskReviewRule.cs`

- **Needs confirmation** — the task has at least one assignee who is not its
  creator. A task whose only assignee is its creator (every Employee-created task,
  and a Manager's task assigned to themselves) closes directly, as today.
- **Reviewers** — the creator, while active; plus the active Managers and HR
  Administrators whose scope covers the department and who are not assignees.
  `IsReviewerAsync(context, task, callerUserId, assignedOnly)`. Nobody
  `assignedOnly` (an Employee) is a reviewer of a task they did not create.
- **Resolving a request for Done** (`ResolveDone`):
  - The task needs no confirmation → `Done`.
  - The caller is a reviewer → `Done` (confirmed by them).
  - No reviewer exists at all (a deactivated creator and nobody else in scope who
    is not on the task) → `Done`, so a task never waits on nobody.
  - Otherwise → `AwaitingConfirmation`.
  The client always asks for `Done`; the server decides. A client asking for
  `AwaitingConfirmation` directly is refused (`StageIsDerivedMessage`), and asking
  for the status a task already has is a no-op, as today.

## Transitions out of AwaitingConfirmation

| Who | To | Effect |
|---|---|---|
| Reviewer | Done (Confirm) | `CompletedAtUtc` = now, `ConfirmedById` = caller |
| Reviewer | InProgress (Send back) | `Reason` required (1–500 chars) → `SentBackReason`, `SentBackAtUtc`; assignees emailed |
| Reviewer | Cancelled | as today |
| Assignee | InProgress (Withdraw) | no reason, no email; clears nothing else |
| Assignee | anything else | refused (`AwaitingConfirmationMessage`) |

Moving to InProgress from AwaitingConfirmation as a reviewer **is** the send-back,
so the reason is required there. On entering `AwaitingConfirmation` the previous
`SentBackReason`/`SentBackAtUtc` are kept, so the reviewer sees what was asked last
time; they are cleared on Done. Leaving Done (reopening) clears `ConfirmedById`
and `CompletedAtUtc`, as `CompletedAtUtc` is cleared today.

## Data — migration `AddWorkTaskConfirmation`

On `WorkTask`:
- `ConfirmedById` (string?, FK → User, `Restrict`) — who confirmed. Must be
  unpicked in `DeleteAdminUser` and `DbInitializer.CleanupUserDependencies`
  (set to null), per the `Restrict` trap in CLAUDE.md.
- `SentBackReason` (nvarchar(500)?)
- `SentBackAtUtc` (datetime2?)

## Command — `UpdateWorkTaskStatus`

Gains `Reason` (string?). Visibility and participation checks stay; a reviewer who
is neither assignee nor creator is now also a participant for these transitions.
Emails are sent after `SaveChangesAsync`.

The controller passes `AssignedOnly` as today; no new endpoint. The request body
gains an optional `reason`.

## Email — `Application/WorkTasks/WorkTaskReviewNotification.cs`

Same shape as `WorkTaskAssignmentNotification` (honours `EmailNotificationsEnabled`,
never throws).
- `SubmittedAsync` — "{assignee} marked {title} as done. Please confirm it or
  send it back." To the creator if active, else to the reviewers.
- `SentBackAsync` — "{reviewer} sent {title} back: {reason}." To every assignee
  except the reviewer.

Writes keep fanning `notificationsUpdated` out to the department group and the
assignees, so the bell refreshes.

## DTO

`WorkTaskDto` gains:
- `CanConfirm` — the task is AwaitingConfirmation and the caller is a reviewer.
- `ConfirmedByName`, `SentBackReason`, `SentBackAtUtc`.

## Other readers of the status

- `TimesheetEntryTaskRule` and `GetTimesheetTaskOptions`: AwaitingConfirmation is
  **open** — hours from that week may still be logged after the assignee clicks Done.
- `GetIdleTaskPeople`: AwaitingConfirmation is neither ToDo nor InProgress, so
  somebody whose only task is waiting counts as not working on a task.
- `WorkTaskProjection` ordering: AwaitingConfirmation sorts with the open tasks.

## Client

- `WorkTaskStatus` type gains `'AwaitingConfirmation'`; `STATUS_COLORS` gains an
  amber entry (`warning`), label "Awaiting confirmation".
- **Card (`TasksPage` → `TaskCard` / `StatusControls`):**
  - Assignee's Done now reads "Mark done". On a task needing confirmation the card
    then shows "Waiting for {creator} to confirm" and a **Withdraw** action.
  - Reviewer (`canConfirm`): **Confirm** and **Send back**; Send back opens a
    reason dialog (required, 500 chars).
  - `sentBackReason` shows as "Sent back: {reason}" on an open task.
- **Filter and tile:** an "Awaiting confirmation" status filter option and stat tile.
- **Edit dialog:** the status select does not offer AwaitingConfirmation; asking
  for Done goes through the same server routing.
- **Topbar bell:** for Managers and HR Administrators, tasks with
  `canConfirm` are listed ("{title} is waiting for your confirmation"), navigating
  to `/tasks`. Uses the existing tasks list query key so SignalR invalidation
  refreshes it.
- `lib/work-tasks.ts` gains `isAwaitingConfirmation` and the filter case.

## Tests

- `Tests/WorkTrack.Tests/WorkTasks/WorkTaskReviewTests.cs`:
  assignee Done → Awaiting; creator/HR/in-scope Manager Done → Done;
  Manager who is an assignee cannot confirm; personal task → Done directly;
  send back without reason refused, with reason → InProgress + fields set;
  assignee withdraw; assignee cannot Done an awaiting task; deactivated creator →
  reviewers emailed; no reviewer at all → Done; client asking for
  AwaitingConfirmation refused; reopening clears `ConfirmedById`.
- `TimesheetEntryTaskRuleTests`: an awaiting task can still be logged.
- `GetIdleTaskPeopleTests`: an awaiting task does not make somebody busy.
- `DeleteAdminUser` cleanup test covers `ConfirmedById`.
- Client: `TasksPage.test.tsx` (buttons per role, send-back dialog),
  `Topbar.test.tsx` (task confirmation items).
- CLAUDE.md: a paragraph in the Tasks bullet.

## Out of scope

- A history table of task status changes (only the last send-back is kept).
- Confirmation for Employee-created personal tasks.
