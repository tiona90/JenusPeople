# Task Settings: field rules, confirmation switch, attachment limits — design

Date: 2026-09-29
Status: approved in conversation, awaiting spec review

## Context

The System Administrator configures the workspace but has no say over how tasks
work: task statuses are a fixed enum, the required fields are hard-coded in
`UpsertWorkTaskRequestValidator`, and the confirmation step and attachment limits
are constants. The request is for the System Administrator to shape tasks —
define statuses, decide which fields are required, and so on — while Managers, HR
Administrators and Employees work within those rules.

The work is split into three sub-projects, each with its own spec, plan and
implementation, built in this order:

1. **Task Settings page + field rules** (this spec) — also carries the
   confirmation on/off switch and the attachment limits.
2. **Custom statuses and priorities** — a status catalogue whose entries each
   belong to a fixed stage (To Do / In Progress / Done / Cancelled), with
   `AwaitingConfirmation` kept as a system status; priorities as a catalogue.
3. **Custom fields** — admin-defined text / number / date / dropdown fields,
   stored per task.

## Decisions already made

- The System Administrator gets **settings only**. They do not see the task list;
  `/tasks` and `TASK_ROLES` stay as they are.
- Settings are **organisation-wide**, one set for every department (like Leave
  Types and the project catalogues). A task moving department never changes shape.

## Scope of this spec

In: the `WorkTaskSettings` table, its API, the Task Settings panel, field
requirement rules enforced on the server and mirrored on the client, the
`RequireCompletionConfirmation` switch, and configurable attachment limits.

Out: statuses, priorities as a catalogue, custom fields (sub-projects 2 and 3).

## Data model

New singleton entity `WorkTaskSettings` (`Domain/WorkTaskSettings.cs`), one row,
inserted by the migration — not by the seeder, which does not run on the deployed
host. Defaults reproduce today's behaviour exactly, so the migration changes nothing
anyone can observe.

New enum `FieldRequirement { Optional = 0, Required = 1, Hidden = 2 }`.

| Column | Type | Allowed values | Default |
|---|---|---|---|
| `DescriptionRequirement` | `FieldRequirement` | Required / Optional / Hidden | Optional |
| `DueDateRequirement` | `FieldRequirement` | Required / Optional / Hidden | Optional |
| `TargetHoursRequirement` | `FieldRequirement` | Required / Optional / Hidden | Optional |
| `AttachmentsRequirement` | `FieldRequirement` | Required / Optional / Hidden | Optional |
| `ProjectRequirement` | `FieldRequirement` | Required / Optional | Required |
| `BillableRequirement` | `FieldRequirement` | Required / Hidden | Required |
| `ShowPriority` | `bool` | shown / hidden | true |
| `RequireCompletionConfirmation` | `bool` | on / off | true |
| `MaxAttachmentsPerTask` | `int` | 1–20 | 10 |
| `MaxAttachmentSizeMb` | `int` | 1–10 | 10 |
| `AllowedAttachmentKinds` | `TaskAttachmentKinds` (flags) | Images, Pdf, Word, Excel; at least one | all four |

Title and Department are always required and are not configurable.

Why the restricted sets:

- **Project cannot be Hidden.** The timesheet editor's Task column sets a row's
  project from the task (`lib/timesheet-tasks.ts`), and `TimesheetEntryTaskRule`
  holds a task to the entry's own project. A task with no project could be offered
  to no row. Optional is allowed: such a task is simply not offered in the
  timesheet Task column. `WorkTaskProjectRule` runs only when a project is given.
- **Billable cannot be merely Optional.** It is a yes/no answer; "optional" would
  reintroduce the null the column was made required to get rid of. It is either
  asked (Required) or not asked (Hidden).
- **Priority always carries a value**, so the only choice is whether it is shown.

`UpsertWorkTaskSettingsRequestValidator` refuses any value outside a field's
allowed set, out-of-range limits, and an empty kinds set.

## API

- `GET /api/worktasksettings` — `GetWorkTaskSettings`. Open to `LeaveAndTimeRoles`
  plus System Administrator: the task dialog, card and attachment picker need it.
- `PUT /api/worktasksettings` — `UpdateWorkTaskSettings`. System Administrator only,
  a full replace like every other settings save. Pinned in
  `SystemAdministrationSurfaceTests`.
- Writes fan `notificationsUpdated` out so open task pages refetch the settings.

## Enforcement

### Required

`UpsertWorkTaskRequestValidator` loads the settings (the validator gets
`AppDbContext` injected, as other validators that read configuration do) and
refuses a blank required field on create and on edit, with a message per field
("Due date is required."). There is no backfill: a legacy task missing a field
that was switched to Required after it was saved is held to the rule on its next
save, even an edit that only fixes the title. That is the same trade
`EmploymentStartDate` makes.

Target hours keep their 1–9,999 range whenever a value is present.

### Hidden

- The dialog does not render the field. The card, the CSV export
  (`tasksToCsv`) and the list filters leave it out.
- **On edit, the server keeps the stored value and ignores whatever the request
  carries** for a hidden field. This is a deliberate exception to the full-replace
  rule documented in CLAUDE.md: nobody can see the field to try to clear it, and it
  makes hide-then-unhide lossless. It is documented in the handler and in CLAUDE.md.
- On create, a hidden field is stored as null (Billable: null — the column is
  already nullable for legacy rows, and every reader treats null as "not stated").

### Required attachments gate completion, not creation

The create dialog stages files and uploads them only after the task exists, so
the server cannot see them at create. As with the leave attachment policy, the
rule gates completion instead. `WorkTaskAttachmentRequirementRule`:

- It refuses an assignee's Done and a reviewer's Confirm while the task has no
  attachment ("Attach a file before marking this task done."). It is called from
  `UpdateWorkTaskStatus` on every path into `Done` or `AwaitingConfirmation`.
- It does not apply to Cancelled, Withdraw or Send back.
- The client mirror is in `lib/task-settings.ts`. The card disables Done and Confirm
  and shows "File needed before Done".

### Confirmation switch

- **Off:** `WorkTaskReviewRule` closes every Done directly (`CompletedAtUtc` set,
  `ConfirmedById` null), and no review email is sent. The client drops the "To
  Confirm" tile, Confirm / Send back, and the bell's waiting-task list.
- **Switching off** sweeps every `AwaitingConfirmation` task to Done in the same
  save (`CompletedAtUtc` = now, `ConfirmedById` null, `SentBackReason` cleared) and
  emails nobody, the way `UpdateLeaveType` sweeps leave in flight.
- **Switching on** moves nothing.

### Attachment limits

- `AddWorkTaskAttachment` reads `MaxAttachmentsPerTask` instead of
  `WorkTaskAttachment.MaxPerTask`. The count message quotes the configured figure.
- `StoreFile`'s `TaskAttachment` entry takes its size ceiling and accepted kinds
  from the settings instead of the constant. Signature detection
  (`FileSignatureValidator`) is unchanged; the settings only narrow what it accepts.
  10 MB stays the hard maximum.
- The limits apply to new uploads only. Files already over a new, lower limit stay.
  A task already holding more files than a new, lower count keeps them all, but
  accepts nothing more until it is under the count.
- `lib/task-attachments.ts` reads the limits from the settings query, so the picker
  refuses the same files the server would.

## Client

- `TaskSettingsPanel` (`client/src/components/admin/`) at `/admin/task-settings`,
  in the sidebar's Configuration section, gated by `isSystemAdministrator`. It has
  three cards:
  - **Fields:** one row per field, with a segmented control showing only that
    field's allowed values.
  - **Completion:** the confirmation switch, with a line saying that switching it
    off closes the tasks now waiting.
  - **Attachments:** the count, the size, and the kind checkboxes.
- `lib/task-settings.ts`: the types, `fieldRequirementError` using the server's
  messages, `isFieldShown`, and the attachment-completion check. It reads a
  missing settings response (an older API) as today's defaults, so an older API
  behaves as before.
- `TaskDialog` shows the required marker and holds Save for a required field, and
  omits hidden fields. `TasksPage`, the card, the CSV and the filters honour
  `isFieldShown`. The bell and the "To Confirm" tile honour the confirmation switch.
- Query key `['work-task-settings']`, invalidated on `notificationsUpdated` with the
  other task keys.

## Testing

Server:

- `WorkTaskSettingsTests`:
  - each field rule on create and on edit;
  - a hidden field surviving an edit that sends a different value;
  - a hidden field stored as null on create;
  - a required attachment refusing Done and Confirm, but not Cancel;
  - the confirmation-off sweep, and switching back on moving nothing;
  - the attachment count and size limits.
- An SQLite-backed test that the migration seeds exactly one row with today's
  defaults.
- `SystemAdministrationSurfaceTests`: the `PUT` is System Administrator only, and
  the `GET` is open to the task roles.

Client:

- `TaskSettingsPanel.test.tsx`: each field offers only its allowed values, and the
  save payload is a full replace.
- `TaskDialog` tests: the required marker holds Save, hidden fields are absent, and
  an older API's missing settings behave as today.
- Card tests: "File needed before Done", and no Confirm when confirmation is off.

## Documentation

CLAUDE.md gets a **Task Settings** paragraph under the Tasks bullet: the table, the
hidden-field exception to full-replace, attachments gating completion, the
confirmation sweep, and "limits apply to new uploads only".
