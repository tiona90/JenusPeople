# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WorkTrack is a full-stack leave management and timesheet tracking application built with ASP.NET Core 10 and React 19 (TypeScript + Vite). See [Architecture](#architecture) for the layer layout — it is layered, but deliberately not dependency-inverted.

## Commands

### Backend (.NET)

```bash
# Run from API/ directory or solution root
dotnet run --project API
dotnet build
dotnet test

# Database migrations (run from solution root)
dotnet ef migrations add <MigrationName> --project Persistence --startup-project API
dotnet ef database update --project Persistence --startup-project API
```

### Frontend (React)

```bash
# Run from client/ directory
npm run dev        # Start Vite dev server (http://localhost:5173)
npm run build      # tsc -b && vite build
npm run lint       # eslint .
npm run preview    # Preview production build
```

### Running Full Stack

Run `dotnet run --project API` (port 5000) and `npm run dev` in `client/` concurrently.

## Architecture

The solution is **layered but not dependency-inverted**. Don't assume the textbook
Clean Architecture graph — `Application` references `Persistence` on purpose. The
actual project references:

```
Domain          → nothing (entities, enums, service contracts)
Persistence     → Domain (AppDbContext, EF configs, migrations)
Application     → Domain + Persistence (MediatR CQRS)
Infrastructure  → Domain (Email, holidays, config)
API             → Application + Infrastructure
client/         → React SPA (separate)
```

Note `Infrastructure → Domain`, not `Application`: the contracts it implements live in
`Domain/Interfaces/` (e.g. `IEmailService`).

**EF Core is the persistence abstraction — there is no repository layer.** This is a
deliberate trade, not drift: roughly 70 of `Application`'s ~150 files inject
`AppDbContext` straight into handlers, which query with LINQ and project to DTOs. What
that means when adding code:

- Inject `AppDbContext` into the handler constructor, like every existing handler does.
  Do **not** introduce `IRepository`/`IUnitOfWork` interfaces for new features.
- Handler tests run against a real EF provider, not mocks. `Tests/WorkTrack.Tests/`
  offers two: `TestDb` in `TestSupport.cs` (EF in-memory — fast, but enforces no constraints and
  ignores transactions) and `TransactionalTestDb` (SQLite in-memory — real transactions,
  enforced unique indexes and foreign keys). Assert on constraint or transaction
  behaviour only against the latter.
- Swapping the ORM would mean touching `Application`. That cost was accepted in exchange
  for dropping a layer of indirection over `DbContext`, which is already a unit of work
  plus a set of queryable repositories.

### Backend Patterns

**CQRS via MediatR:** Business logic belongs in `Application/*/Queries/` and `Application/*/Commands/`. Controllers should be thin — dispatch to MediatR, then call `HandleResult<T>()`. Two existing exceptions to follow *away* from, not copy: `API/Controllers/TimesheetEntriesController.cs` does its entry CRUD directly against `AppDbContext`, and `AnnualLeavesController`/`TimesheetsController` query it to resolve SignalR notification audiences. `API/Hubs/`, `API/BackgroundServices/`, and the health checks also use `AppDbContext` directly, which is fine — they sit outside the request/handler path.

**Result<T> pattern:** Handlers return `Result<T>` (never throw for business errors). `BaseApiController.HandleResult<T>()` maps these to HTTP responses consistently.

**Validation pipeline:** `FluentValidation` validators auto-run via MediatR's `ValidationBehavior` pipeline behavior. Add a validator class in the same folder as the command/query.

**Full-replace update DTOs:** `AdminUpdateUserDto` is a replace, not a patch — `UpdateAdminUser` assigns every field it carries unconditionally, so a `null` in the request genuinely clears the stored value. Anything added to it has to follow that: mixing in a "a null leaves the stored answer alone" rule for one field gives a field that looks editable but quietly refuses to be cleared. A field that is genuinely optional on such a DTO therefore needs a way for the UI to *send* null. `User.Gender` used to be one — the dialog offered an explicit "Not specified" radio for exactly this reason — but it is now **required for an Employee or a Manager** on both admin validators (`PersonFieldRules.GenderRequiredMessage`), so the radio is gone; see the `User` row below for why. The null it still sends is the one for a **System Administrator**, for whom the field is hidden and a value is *refused* (`GenderNotForAdminMessage`) — the same role scoping as `DepartmentId` and `EmploymentStartDate`, and with the same payoff: a promotion to System Administrator clears the stored answer rather than stranding one the System Administrator's dialog cannot show.

**Authorization:** Policy-based (`"AnnualLeaveRead"`, `"AnnualLeaveCreate"`, etc.) defined in `API/Program.cs`. Roles: `System Administrator`, `HR Administrator`, `Manager`, `Employee`. Managers are scoped to their departments in queries. The first role was called `Admin` until migration `RenameAdminRoleToSystemAdministrator` renamed the Identity row in place; `AppRoles.SystemAdministrator` is the one source of the string, and the client's `UserRole` union reads it. Class, file and route names such as `AdminUsersController` keep "Admin" as shorthand — they are not the role.

**The two administrators are disjoint: the System Administrator configures the workspace, the HR Administrator runs Leave & Time.** Migration `AddHrAdministratorRole` inserted the Identity row (the seeder does not run on the deployed host). There are two questions a gate can ask, and they have different answers:

- **Reach** — who may open the company-wide pages. Both roles, but they do not see the same data: the **System Administrator sees every department**, and the **HR Administrator sees the departments assigned to them** as `UserDepartment` rows (Users → Edit User → Role & access → Departments; `PUT /api/adminusers/{id}/departments`, System Administrator only). `AppRoles.Administrators` (a list, for EF `Contains` and `RequireRole` — typed `IReadOnlyList<string>` on purpose; see the comment on it), `AppRoles.AdministratorRoles` (the comma-joined `const` for `[Authorize(Roles = ...)]`) and `AppRoles.IsAdministrator` (a string overload and a `ClaimsPrincipal` extension, so controllers write `User.IsAdministrator()`) are the *authorization* gate for those pages and the role-scoped rules (an HR Administrator still has no department, gender or employment start date of their own, files their own leave without coverage and is excluded from attendance); the client mirrors them in `client/src/lib/roles.ts` (`ADMINISTRATOR_ROLES`, `isAdministrator`, `isAdministratorRole`). They are **never a data filter**. A query asks `User.IsSystemAdministrator()` for the unscoped reach and `User.IsDepartmentScoped()` (Manager or HR Administrator) for the scoped one, and the scoped one goes through `ManagerAccessScopeResolver`, whose `ManagedDepartmentIds` is the caller's profile department plus their `UserDepartment` rows. Four queries with no scope before — company attendance, presence, the Users list and detail — take `ScopeToCaller`, which the controllers set to `!IsSystemAdministrator()`. An HR Administrator joins the per-department SignalR groups rather than `NotificationsHub.AdminGroup`, and the birthday digest and daily attendance report they receive cover their departments alone. **The daily attendance report goes to HR Administrators only** (`ReminderDispatcher.DailyAttendanceReportAsync`): attendance is Leave & Time, so the System Administrator, who used to get a company-wide copy, gets none, and a workspace whose HR Administrators have no departments assigned sends nothing rather than falling back to them. What the System Administrator is emailed about instead is **the system failing**: `Application/SystemErrors/SystemErrorNotifier.cs` mails every active System Administrator when a request ends in an unhandled 500 (`GlobalExceptionMiddleware`, after the response is written, with the correlation id) or a reminder's dispatch throws (`ReminderBackgroundService`, which now catches per reminder so one failure does not stop the rest of the tick). The same fault — source plus exception type, never the message — is reported once an hour (`SystemErrorThrottle`, an in-memory singleton like the reminder dedup), it honours `EmailNotificationsEnabled`, and it never throws back into its caller. `SystemErrorNotifierTests` pins all of it, including that a 4xx is not a system error. **The same fault is also written down as a `SystemError` row, and that is what the System Administrator's notification bell lists** — the role neither files nor decides leave, so the Topbar used to hand it the employee branch (everyone's "Leave approved" and "Timesheet rejected") while the one thing it is emailed about never reached the bell. `SystemErrorNotifier.RecordAsync` writes the row before the email is attempted and regardless of `EmailNotificationsEnabled` (the bell is not email); a repeat inside the hour bumps `Occurrences`/`LastOccurredAtUtc` on the existing row rather than adding one; recording and emailing are caught separately so a database that is itself the fault still gets the email out. `GET /api/systemerrors` (`GetSystemErrorList`, newest by last occurrence, System Administrator only — pinned in `SystemAdministrationSurfaceTests`) feeds both the bell (`Topbar`, a third mode beside the manager's and the employee's, which stops fetching the status histories for the role; read state keyed by row id **plus** last occurrence, so a fault that comes back after being read is unread again) and the **System Log** page (`SystemLogPanel`, sidebar System section, `/admin/system-log`), which the bell navigates to with `#system-error-{id}` and which highlights and scrolls to that row. The correlation id is the handle: nothing longer than the message is stored, the log has the rest. `Topbar.test.tsx` and `SystemLogPanel.test.tsx` pin the client side. **The department set follows `DepartmentId`'s rule inverted: at least one is required for an HR Administrator, and any are refused for every other role** (`HrDepartmentScopeRules`, mirrored by `hrDepartmentsError` in `client/src/lib/validation/person.ts`). `SetAdminUserRoles` keeps a `UserDepartment` row only across a save that leaves the role unchanged; **any** change of role clears the whole set, whether or not the new role could hold one — a Manager promoted to HR Administrator or an HR Administrator demoted to Manager never silently keeps a reach nobody can see or revoke. The dialog's **roles → departments** order re-supplies an HR Administrator's set in the same save, immediately after the clear, so a promotion into the role still ends up with the departments chosen for it rather than inheriting a manager's old team unasked; the seeder's `RemoveUnscopedUserDepartments` does the same on startup. `AdminUsersPanel`'s edit mutation therefore runs **roles → departments → user → profile**. An HR Administrator with no rows (a legacy account) sees nothing until assigned, deliberately — an empty picker must never mean "everything". Department-less leave and timesheets are inside every HR Administrator's reach regardless of their assigned departments — a leave or timesheet whose `DepartmentId` is null (an administrator's own, since every Employee and Manager has one; or a legacy row predating the department column, which is reachable by every HR Administrator just the same, deliberately — HR saw everything before scope existed, and a row nobody can assign a department to must not become a row nobody can act on) has nobody else who could approve it: the leave commands OR in `request.IsAdmin && !DepartmentId.HasValue`, the leave and timesheet read queries carry an `IsHrAdministrator` flag that widens the scoped branch to `DepartmentId == null`, and `TimesheetScope.ApplyAsync` / `TimesheetAccess.AuthorizeWriteAsync` take an `isHrAdministrator` parameter (placed after `cancellationToken` on purpose, so positional callers keep compiling). Submitting a timesheet on somebody's behalf inside scope is the HR Administrator's alone (`SubmitTimesheet.IsHrAdministrator`), not a Manager's — a Manager reviews what their team submits, and putting the draft in for them would commit hours the employee never stood behind. The same department-less ruling reaches evidence and handover files: `GetStoredFile`'s manager-branch scope checks widen with `IsHrAdministrator && DepartmentId == null`, so an HR Administrator can open what is attached to an administrator's own leave and a plain Manager cannot. `NotificationsHub.HrAdministratorGroup` (`role:hr-administrators`) is where every HR Administrator lands instead of `AdminGroup`; the leave and timesheet controllers fan a department-less event out to it, and the department groups cover everything else. And children follow the reach, not the write: an HR Administrator gets a Manager's read-only access inside their departments (`ChildAccessResolver`), because the one write surface — Users → Edit User → Profile — is not one they can open. `GET /api/userdepartments` is System Administrator only — the table now says which departments each HR Administrator runs, so reading the whole thing is system administration, not a company directory.
- **System administration** — who configures the system. System Administrator alone, by name: the write actions on `AdminUsersController` (action-level attributes ANDed with the class-level reach gate), `DepartmentsController`, `LeaveTypesController`, `ProjectsController` and the three project catalogues, `SettingsController`, `HolidaysController.GetCountries`, and the `EmployeeProfileUpdate` policy. On the client, `isSystemAdministrator` / `SYSTEM_ADMINISTRATOR_ROLES` gate the `/admin/*` routes, the configuration panels in `DashboardHome`, and the People, Configuration and System sections of the sidebar. `SystemAdministrationSurfaceTests` pins which routes carry which gate.

- **Leave and time decisions** — who acts on somebody else's leave or timesheet. The HR Administrator within their assigned departments and a Manager within their department: `AppRoles.LeaveAndTimeDecisionRoles` on the timesheet approve/reject actions and the team attendance board, and `User.IsHrAdministrator()` where a leave controller decides whether the caller is acting on somebody's behalf (create, edit, status, delete). A System Administrator is deliberately neither — they neither file nor decide leave — so a call from them falls through to the manager-scope checks and is refused. **Nobody decides their own timesheet**: `UpdateTimesheetStatus` refuses when the caller's profile owns it (`OwnTimesheetMessage`), whatever the flags say — a Manager's own sheet sits inside their own department scope and an HR Administrator reaches their own department-less one, so scope alone would let either sign off their own hours. The dashboards already left the caller's own row out of the queue; Team Timesheets and All Timesheets now render it without Approve/Reject and keep it out of a bulk selection. **Timesheets follow leave's manager stage** (`Application/Timesheets/Support/TimesheetReviewRule.cs`): a submitted timesheet is the Manager's to review, and an HR Administrator approves or rejects it only when no manager is available to (`WithManagerMessage`) — every manager who could review it is on approved leave today, or the department has none, all read through `ManagerAvailability` so the rule, the list and the submission email agree. **A manager's own sheet is self-certified**: when the submitter holds the Manager role and no other manager could review it (`TimesheetReviewRule.SelfCertifiesAsync` — the recipient set minus themselves is empty), `SubmitTimesheet` files it straight into Approved with a history row (`SelfCertifiedComment`) and emails nobody; nobody approves their own hours, and HR's review is for a manager who is away, not a standing second signature on every manager's week. A manager with a colleague manager is reviewed like anyone else, and an employee's sheet in a department with no manager still goes to HR — that is a configuration gap the workspace overview flags, and the sheet must not wait on nobody. A manager's own sheet submitted before this rule stays Submitted and HR may close it. **HR takes an approval back** the way they cancel an approved leave: `ReopenTimesheet` (`PATCH /api/timesheets/{id}/reopen`, HR Administrator only, inside their departments or on a department-less sheet, reason required) returns an Approved sheet to Submitted, clears `ApprovedAt`/`ApproverId`, writes an Approved → Submitted history row with the reason, and emails the employee and the managers who review it; the sheet is then `AwaitingManager` again. All Timesheets offers it as **Cancel approval** on an HR Administrator's approved rows (not their own). `ReopenTimesheetTests` pins it. There is no "with HR" status; the question is asked when HR decides (`UpdateTimesheetStatus`, with a `NowUtc` test seam) and when the list is built: `TimesheetDto.AwaitingManager` is true on an open sheet a manager is available for, and the HR dashboard queue, the HR bell and All Timesheets leave those rows out (`isTimesheetWithManager` in `approval-stage.ts`; a missing flag from an older API reads as not with the manager, so the row is shown and the server answers). All Timesheets renders no status tab bar for HR, as Leave Management does not, and also leaves an HR Administrator's view free of **Drafts** — an employee's unsubmitted work is nobody's to review yet, so HR sees a sheet from submission on, and the status filter is built from the rows the viewer is shown, so it offers no "Draft" either (`AllTimesheetsPage.test.tsx`); the System Administrator's view keeps every row. Because both read the clock at that moment, a manager going on leave after the submission hands the sheet to HR for the duration and takes it back on return — nothing is rerouted. `SubmitTimesheet` emails the managers when one is available and the HR Administrators covering the department otherwise, with a "Note: Sent to HR for review: …" line, the way leave routes at filing. `TimesheetReviewRuleTests` pins all of it. The **reads** stay on the reach gate, because the System Administrator's own panels quote them: the Users panel reads presence, the Departments panel reads company attendance, leave and timesheets. On the client, `LEAVE_AND_TIME_ROLES` (HR Administrator, Manager, Employee) gates every Leave & Time route in `App.tsx` and the sidebar section.

Pick the helper by the question, never by role name: a reach gate that names System Administrator locks HR out of their own pages, and a configuration gate that uses `AdministratorRoles` hands HR the settings. The seeded `systemadmin@` account keeps the System Administrator role.

The two roles also open on different dashboards. `DashboardHome` sends a System Administrator to `AdminDashboard` — the workspace overview, built from users, profiles, departments, the catalogues and the settings (`buildWorkspaceOverview`): accounts and roles, people by department with each department's manager, what needs attention (invites not accepted, people with no department, departments without a manager, deactivated accounts), configuration counts and the system settings — and nothing from leave, timesheets or attendance. An HR Administrator goes to `HrDashboard`: the company-wide approval queue with Approve/Reject to hand (shared with the manager's dashboard through `buildApprovalQueue`, which also holds Approve on a request still short a document its type requires), who is away today and in the next fortnight, "balances to watch" (two days or fewer left, or under a quarter used with half the leave year gone — `buildBalanceWatch`), attendance by department, today's attendance issues and the recent activity feed (the two cards `CompanyAttendancePage.test.tsx` checks agree with the attendance page). `HrDashboard.test.tsx` pins which role lands where. In demo mode the seeder also creates `hradmin@annualleave.com` (`DbInitializer.HrAdministratorDemoEmail`, seed password, stripped on a real deployment like the other demo accounts), and `EnsureAdministratorProfiles` gives any administrator without a profile a department-less one — `SeedEmployeeProfiles` only runs on an empty table, so an administrator seeded later would otherwise have none.

### Frontend Patterns

**Hash-based routing:** `App.tsx` reads `uiStore.currentPage` (a hash-style string) to decide which component to render. There is no router library — navigation happens by setting `uiStore.currentPage`.

**State split:** MobX (`authStore`, `uiStore`) holds client-only UI/auth state. React Query handles all server state (fetching, caching, invalidation).

**Real-time:** SignalR hub at `/hubs/notifications` sends `notificationsUpdated` events. `App.tsx` listens and calls `queryClient.invalidateQueries()` to refresh relevant caches.

**API client:** Axios instance at `client/src/lib/api/client.ts` (base URL `http://localhost:5000/api`, includes credentials). API modules in `client/src/lib/api/` are thin wrappers returning typed responses.

## Domain Model Summary

| Entity | Key Fields |
|--------|-----------|
| `User` | Extends `IdentityUser`; has `DisplayName`, `ImageUrl`, `IsActive` (may this account sign in — a leaver is switched off rather than deleted, since `DeleteAdminUser` nulls out every approval they gave). `DateOfBirth` and `Gender` are recorded HR data an admin maintains on the Users panel. **`Gender` decides who is offered Maternity and Paternity Leave, and any other type an admin restricted through `LeaveType.AvailableTo`** — see [Who is offered a leave type](#domain-model-summary) below the table. It follows `DepartmentId`'s rule exactly — **required for an Employee or a Manager, refused for a System Administrator** — on both admin dialogs and both admin validators (`PersonFieldRules.GenderRequiredMessage` / `GenderNotForAdminMessage`, mirrored by `genderError` in `client/src/lib/validation/person.ts`, which the dialogs skip for a System Administrator): the dialog offers Male or Female only, and hides the radios for a System Administrator, who sits outside every leave rule a gender routes. `UpdateAdminUserValidator` reads the **stored** role to tell which rule applies, since the payload carries none, so `AdminUsersPanel`'s edit mutation sets **roles → user → profile** in that order; reordering refuses every role change on a field the admin cannot see. It used to offer an explicit "Not specified" so a value set by mistake could be taken back — see **Full-replace update DTOs** under [Backend Patterns](#backend-patterns) — but the eligibility rule reads a stored `null` as "offer everything", so a type restricted to one gender was still offered to anyone an admin left unspecified, and the restriction looked like a rule and behaved like none. The column stays nullable for accounts predating it, and such a `null` is still offered **both** parental types rather than neither, until the account is next saved — when the admin has to pick one, the same backfill-on-save the date of birth gets. A System Administrator's `null` is the standing answer, not a gap, so a System Administrator filing their *own* leave is offered everything; accepted as the price of not asking them |
| `AnnualLeave` | `EmployeeId`, `StartDate/EndDate`, `Status` (enum), `TotalDays` (computed, no weekends). `ChildId` is nullable — required on a request against a `PerChildEntitlement` type, `null` on every row predating the feature (and on any request against a type that isn't per-child), and a `null` `ChildId` counts against no per-child ledger |
| `LeaveType` | `Name`, `IsActive`, `AffectsBalance` (is it deducted from the enforced pool), `DefaultAllowance` and `MaxCarryoverDays` — the allowance and the year-end cap that bounds it (and which it in turn bounds: a cap may not exceed the allowance, and is nullable, `null` meaning no cap at all), both per type and both edited **only** on Leave Types. See [Leave is configured once](#domain-model-summary). `PerChildEntitlement` plus its three numbers (`PerChildTotalWeeks`, `PerChildWeeksPerYear`, `ChildEligibleUntilAge`) configure the second, per-child ledger — see [the two leave ledgers](#domain-model-summary) below the table. `AttachmentPolicy` decides whether a request needs a supporting document, and is enforced on create and edit — see [the attachment policy](#domain-model-summary) below the table. `MinServiceMonths` hides the type from anyone whose `EmploymentStartDate` is not that many months behind today (0 = no minimum) — see [A leave type can ask for a length of service](#domain-model-summary) below the table. `ProRateFirstYear` scales a mid-year joiner's first leave year of this type's allowance from their `EmploymentStartDate`; enforced only on the type flagged `AffectsBalance`, quoted for every other, refused on a per-child type — see [A leave type can pro-rate the first year](#domain-model-summary) below the table. Annual, Maternity and Paternity Leave are **built-in** (`Domain/SystemLeaveTypes.cs`): they cannot be renamed or deleted, though every other setting on them stays editable. Keyed by name, which is sound only because the name is frozen and already unique case-insensitively; `LeaveTypeDto.IsSystem` derives the flag so the client keeps no copy of the list. Annual leave additionally cannot be **disabled** — it is the type the enforced pool is a budget for — but Maternity and Paternity can be, for an organisation that does not offer them. `RequiresManagerApproval` / `RequiresHrApproval` are the two approval stages — see [Approval can take two stages](#domain-model-summary) below the table |
| `AnnualLeave` (cont.) | `Duration` (`Full`/`HalfDayMorning`/`HalfDayAfternoon`) decides whether the request costs whole days or 0.5 of one, and `TotalDays` is **decimal** because of it. `Full` is 0, so every row predating the column reads as the full day it was charged as. A half day covers exactly one date and is refused on a type whose `HalfDayAllowed` is off — see [A half day is stored and charged](#domain-model-summary) below the table |
| `Timesheet` | `EmployeeId`, `PeriodStart/End`, `TotalHours`, `Status` (Draft→Submitted→Approved/Rejected), `DepartmentId` (nullable — the department it was filed under, kept for history so it outlives its author's move; null when the author has none, i.e. a System Administrator, matching `AnnualLeave.DepartmentId`) |
| `TimesheetEntry` | `TimesheetId`, `ProjectId`, `Date`, `HoursWorked` (decimal 4,2), optional `ActivityTypeId`, `ProjectTypeId` and `ProjectComponentId`. One entry per project **+ type + component** per date |
| `Project` | `Name` (unique), `Code` (unique), `IsActive`; belongs to many `Department` via `ProjectDepartment` (which departments can see it), narrows activities via `ProjectActivityAssignment`, components via `ProjectComponentAssignment`, and its kinds of engagement via `ProjectTypeAssignment` |
| `EmployeeProfile` | Links `User` to `Department`, tracks leave entitlement. `DepartmentId` is **nullable, and null is what a System Administrator gets** — the role sees every department, so belonging to one grants nothing, and an invented assignment counted for real (headcount, attendance warnings, `DeleteDepartment` blockers). The validators enforce it both ways: required for Employee/Manager, refused for System Administrator. Anything grouping profiles by department must skip the nulls. `AnnualLeaveEntitlement` and `LeaveBalance` are the pool the API enforces on approval, but are **derived from the annual-leave allowance, never edited per person** — see [Leave is configured once](#domain-model-summary) below the table. **A stored 0 switches the balance check off entirely** (`AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync`), so never write one. `HasChildren` is a tri-state (`null` = never asked, `false` = declared none, `true` = has some) — `HasChildrenDeclaration` refuses `false` while any `Child` row still points at the profile. `EmploymentStartDate` follows `DepartmentId`'s rule exactly — **required for Employee/Manager, refused for System Administrator** — because both live in the dialog's Profile section, which is hidden for a System Administrator; see [The employment start date](#domain-model-summary) below the table |
| `Child` | One declared child of an `EmployeeProfile`: `Name`, `DateOfBirth`. **Age and eligibility are never stored** — both are computed on every read (`PerChildLeaveCalculationService`), which is what makes a child aging out of paternity leave automatic. Deleting a child with leave against them is refused (`DeleteChild`, and the FK is `Restrict`): the row is what the per-child ledger is queried by, so removing it would erase the record of leave actually taken. An aged-out child is kept and reads as ineligible. Managed from **two** surfaces, both rendering `ChildrenSection`: the employee's own Edit profile (sidebar), which also asks the `HasChildren` Yes/No, and **Users → Edit User → Profile**, where an admin maintains them for an employee by passing that person's user id. `ChildAccessResolver` is the authority on who may touch whose — self and System Administrator read/write, Manager and HR Administrator read-only within their department scope. The declaration is not asked on the admin surface (it is the employee's own statement) but is still recorded, because `CreateChild` sets `HasChildren = true` |
| `ProjectComponent` | Org-wide catalogue of deliverables (DM, Lasernet, jDocs): `Name` (unique), `Icon`, `ColorKey`, `IsActive`. Projects declare theirs via `ProjectComponentAssignment`, and a `TimesheetEntry` logs against one — narrowed by its project the same way the activity is |
| `ProjectType` | Org-wide catalogue of engagement kinds (Task, Issue, Inquiry, Support): `Name` (unique), `Icon`, `ColorKey`, `IsActive`. Projects carry any number via `ProjectTypeAssignment`, or none; a type projects still carry cannot be deleted. A `TimesheetEntry` also logs against one — narrowed to the types its project carries, and the field that narrows its project picker |
| `StoredFile` | An uploaded file's bytes in the database: `Content` (varbinary(max)), `FileName`, `ContentType` (**detected**, never the caller's claim), `Sha256` (also the HTTP ETag), `SizeBytes`, `UploadedById`. `Purpose` (`ProfileImage`, `LeaveEvidence`, `CoverageHandover`) drives both what the upload accepts and who may read it back. Evidence and a handover document are kept apart on purpose: the delegate may open the handover, and must not thereby be able to open a doctor's note |

Status enums: `AnnualLeaveStatus` (Pending, Approved, Rejected, Cancelled, AwaitingHrApproval); `TimesheetStatus` (Draft=0, Submitted=1, Approved=2, Rejected=3, Resubmitted=4).

**Leave is configured once, for everyone, on Leave Types.** Both numbers that describe
an annual-leave budget are columns on the type flagged `AffectsBalance` (annual leave,
in practice): `LeaveType.DefaultAllowance`, how many days it grants, and
`LeaveType.MaxCarryoverDays`, how many unused ones survive the year end. One row, and
the Leave Types screen is the only place either is edited.

Leave Settings (`AppSettingsPanel`) **quotes** both — the carryover preview is
meaningless without them — but no longer edits either, and saves no leave type. It
used to edit both: the allowance as a second surface onto the same column, and the cap
as an org-wide `AppSettings.MaxCarryoverDays` sitting a screen away from the allowance
it bounds, with no way to say that sick leave carries nothing while annual leave
carries five. The column is gone (migration `MoveCarryoverCapToLeaveType`, which
copied the configured cap onto the `AffectsBalance` type first).
`client/src/lib/leave-allowance.ts` reads both figures (`annualLeaveAllowance`,
`annualCarryoverCap`).

**The cap is nullable, and all three of its readings mean something different:**
`null` is no cap — every unused day carries; `0` is the opposite — nothing carries,
an ordinary policy, unlike a 0 allowance, which is a hazard; `N` caps at N days and
**may not exceed that type's own `DefaultAllowance`** (`UpsertLeaveTypeRequestValidator`).
The bound is what migration `BoundCarryoverCapToTheAllowance` added, along with the
nullability and a clamp of any stored cap above its allowance. Before it, the cap was
checked against the calendar alone, so annual leave could grant 23 days a year and
carry over 80 — reachable only after four straight years of taking none, so it read
like a limit and behaved like none. That policy is real, but it is now the field left
blank rather than a number chosen to be out of reach.

Note the cap and the allowance are not the same quantity at year end: a closing balance
is last year's carry-in plus this year's allowance, so 23 days carried into a 23-day
year closes at 46 and a cap of 23 still expires 23 of them. That is why "carry
everything" is `null` and not `cap = allowance`. `splitAtCarryoverCap` in
`leave-allowance.ts` is the one place that arithmetic lives, and a `null` must never be
flattened to 0 on the way to it — the two are opposites. Nothing performs a rollover
yet: the figure drives the preview on Leave Settings and the leave type's card. A
per-child type has no cap at all (the dialog hides the field and sends 0) — that ledger
is bounded by the child's age, not by the leave year.

`EmployeeProfile.AnnualLeaveEntitlement` and `LeaveBalance` are **derived, never
edited per person**. Only three things write them, all from the allowance:
`CreateAdminUser` on hire, `UpdateLeaveType` when the allowance moves (it re-stamps
every profile and recomputes balances), and `DbInitializer.FixZeroEntitlementProfiles`
for anything sitting at 0. `EditEmployeeProfileRequest` deliberately carries neither
field — when it did, a dialog that had stopped showing the input still echoed a stale
value back.

Two rules that follow, both learned the hard way:

- **Never write a 0 entitlement.** `AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync`
  returns early on `<= 0`, so a 0 does not mean "no allowance" — it means *no balance
  check at all* for that employee. `UpdateLeaveType` refuses a 0 allowance outright for
  this reason.
- **Do not add an org-wide allowance or carryover setting back.** `AppSettings` used to
  carry a third allowance, `DefaultAnnualEntitlement`, free to disagree with the leave
  type and by default doing so (20 against 25). It is gone (migration
  `RemoveAppSettingsDefaultAnnualEntitlement`), and every profile was aligned to the
  allowance by `AlignEntitlementsWithAnnualLeaveAllowance`. `MaxCarryoverDays` followed
  it onto the leave type for the same reason (`MoveCarryoverCapToLeaveType`).

The client mirrors this in `client/src/lib/leave-allowance.ts`; a type that sets no
allowance reads as 0 and renders "—".

**There are two leave ledgers, and they are disjoint.** The pooled one is
`EmployeeProfile.LeaveBalance`, kept by `AnnualLeaveBalanceCalculator` for the type
flagged `AffectsBalance`. The other is per child, enforced by
`PerChildLeaveBalanceCalculator` for a type flagged `PerChildEntitlement` — paternity
leave, which is not one budget per employee but one per child:
`PerChildTotalWeeks` (18) weeks per child, `PerChildWeeksPerYear` (5) per leave year,
until the child reaches `ChildEligibleUntilAge` (15). A week is **5 business days**
(`PerChildLeaveCalculationService.BusinessDaysPerWeek`), so 18 weeks is 90 business
days and weekends and public holidays inside a request consume nothing.

`UpsertLeaveTypeRequestValidator` refuses a type that sets both flags: counted in
both, one day of leave would be charged twice.

**The per-child total can differ by birth order.** `PerChildTotalWeeks` is the
*first* child's figure; `PerChildTotalWeeksSecondChild` and
`PerChildTotalWeeksThirdChildOnwards` are the second's and the third-and-later's.
Cypriot maternity leave forced it — 22 weeks for the first child, 22 for the
second, 26 from the third — which one "weeks per child" column could not say.
Both later columns are **nullable, and null means "the same as the one before
it"** (`LeaveType.PerChildTotalWeeksFor`, mirrored by `resolvePerChildTotals` in
`client/src/lib/leave-allowance.ts`), so Paternity's `18 / null / null` reads 18
for everyone and migration `AddPerChildTotalWeeksByBirthOrder` backfills
nothing. A stored 0 reads the same way rather than as "nothing for a second
child" — the validator refuses saving one, and the fallback is the direction that
cannot silently refuse every request for a younger sibling. "Which child" is
**birth order among the employee's declared children, oldest first**
(`PerChildLeaveCalculationService.BirthOrder`, ties broken by `CreatedAt` then
`Id`), computed on every read like the age and stored nowhere, so declaring an
older sibling later moves everyone's position. `PerChildLeaveBalanceCalculator`
enforces the figure for the request's child; `GetChildLeaveEntitlements` reports
each child at their own total with a `BirthOrder`, plus the three resolved
figures on the summary so My Leave can state the policy without a child at each
position. The yearly cap is bounded by the **smallest** of the three. The dialog
shows three pre-filled fields ("1st child", "2nd child", "3rd child onwards") and
always sends numbers; the card's Enabled toggle sends the two columns back **as
stored, null included** — flattening a null to 0 there would turn 22 / 22 / 26
back into 22 for everyone.

**`PerChildEntitlement` is not a free-standing setting — only Maternity and
Paternity Leave may carry it** (`SystemLeaveTypes.PerChildEntitlementTypes`, also
enforced by `UpsertLeaveTypeRequestValidator`). The ledger is keyed by
`AnnualLeave.ChildId` and a request against a per-child type must name a child,
which only means anything for the leave a birth grants; it was previously a switch
on every type, so a per-child ledger could be put on sick leave. The edit dialog
therefore has **no toggle**: it shows the three numbers unconditionally for those
two types, driven by the server-derived `LeaveTypeDto.SupportsPerChildEntitlement`,
and shows no section at all for anything else. Note the dialog falls back to
18/5/15 when a stored number is 0 (`||`, not `??`) — a per-child type with a 0 in
any of the three is refused by the validator, so a 0 reaching the field would open
the form already invalid. Maternity Leave is seeded with those columns at 0, so
that fallback is reachable, not theoretical.

The per-child ledger is **stored nowhere** — it is a projection over approved leave
rows, grouped by `AnnualLeave.ChildId`. That is why a child turning 15 needs no job
and no recalculation: their usage stays in history, their remaining entitlement is
simply gone, and the employee's totals (which cover eligible children only) fall on
the next read. `GetChildLeaveEntitlements` reads usage through the same
`PerChildLeaveBalanceCalculator` helpers that enforce it, so a screen cannot promise
more than the API will approve.

Two differences from the pooled balance worth knowing:

- **A 0 refuses everything here, rather than switching the check off.** The opposite
  of `AnnualLeaveEntitlement`, where `CheckSufficientBalanceAsync` returns early on
  `<= 0`. `UpsertLeaveTypeRequestValidator` refuses saving a 0 in any of the three
  fields once `PerChildEntitlement` is on, but the runtime arithmetic agrees even if
  a 0 ever got in some other way: `RemainingDays` floors at zero, so a 0 total leaves
  nothing to approve rather than nothing to check.
- **The per-child check runs at creation even when the type requires approval**
  (`CreateAnnualLeave`), unlike the pooled check, which only runs at creation for an
  auto-approving type. A per-child refusal is something the employee can act on;
  waiting days for a manager to hit it helps nobody. It is re-checked on the
  transition into `Approved` in `UpdateLeaveStatus` — so, as with the pooled balance,
  several *pending* requests can each pass creation and the second *approval* is what
  fails.

**Who is offered a leave type.** Two rules, applied in order. A type whose
`LeaveType.AvailableTo` (`Both`/`Male`/`Female`) names one gender is offered only
to an employee whose recorded `User.Gender` matches. Maternity and Paternity Leave
additionally need at least one child young enough to qualify. Everything else is
offered to everybody.

`AvailableTo` is **fixed on the three built-in types and editable on every other**:
`SystemLeaveTypes.FixedAvailability` pins Annual Leave to `Both`, Maternity to
`Female` and Paternity to `Male`, `UpsertLeaveTypeRequestValidator` refuses any
other value for them, and `LeaveTypeDto.AvailabilityLocked` (derived from the name,
like `IsSystem`) tells the edit dialog to render the "Available to" radios
read-only. Until this column the two parental genders were hard-wired by name in
the rule and nothing else could be restricted; migration `AddLeaveTypeAvailableTo`
stamped the two rows, and `Both` is 0 so every other existing type reads as
offered to everyone. The card's Enabled toggle resubmits it like every other
column (see the trap at the end of this section). The card's "Available to" chip is
derived from it; the free-text `EligibilityScope`/`EligibilityNotes` chip that
used to sit beside it was read by no rule, and both columns are gone (migration
`RemoveLeaveTypeEligibilityChip`). **The client reads the three names ahead of the column**
(`fixedAvailability`/`resolveAvailability` in `parental-leave.ts`): an API built
before the column sends neither field, and a Maternity row the migration has not
reached still says `Both`, so the dialog, the card, the Enabled toggle and the
leave-form filter all treat the built-in types as fixed whatever the row says.
Nothing on the client trusts `availabilityLocked` alone.

`Application/AnnualLeaves/Commands/ParentalLeaveEligibility.cs` is the rule, called
from `CreateAnnualLeave` and `EditAnnualLeave`; `client/src/lib/parental-leave.ts`
mirrors it so the leave forms never show a card the API would refuse. Keep the two
in step — a disagreement shows up as a card that only fails when pressed. Three
things about it that are deliberate:

- **A stored `null` gender passes.** It means "nobody entered it", which is every
  account predating the column — not "neither". Failing closed would have stripped
  parental leave from the whole company until an admin filled the field in one
  person at a time. The eligible-child half still applies, and a custom type
  restricted to one gender is offered to a `null` for the same reason. But a
  `null` can no longer be *chosen* for an Employee or a Manager: the admin
  dialogs offer Male or Female only and both admin validators refuse a save
  without one, so those nulls are legacy rows on their way out, not a standing
  "offer me everything" answer. The one standing `null` is a **System Administrator's** — the
  field is hidden and refused for that role — so a System Administrator filing their own leave
  passes this half of the rule for every type. That is accepted, not overlooked.
- **The eligible-child rule is skipped for a type with its own per-child ledger.**
  Paternity Leave already refuses a request naming no child, a child that is not
  the employee's, or one who has aged out, each with a message naming the child and
  the date. A blunter pre-check in front of it would only make those messages
  worse. So in practice the new child rule bites on Maternity Leave, which keeps no
  per-child ledger.
- **Maternity Leave has all three per-child columns at 0**, so it has no cut-off age
  of its own and falls back to the per-child type's (Paternity's 15). Note the
  consequence: an employee expecting their first child cannot file maternity leave
  until the child is on file. That is the rule as specified, not an oversight.

On the client the filter applies to the employee's **own** request only —
`ApplyLeavePage`, and `AnnualLeaveForm` when it is not an admin filing or editing
on somebody else's behalf. An admin's own gender says nothing about the employee
they are filing for, so that path keeps the whole list and lets the server answer.
`AnnualLeaveForm` also keeps an existing request's own type on the list even when
the rule would no longer offer it, so editing an old request does not open on a
blank select.

**The attachment policy is enforced, not advertised — at approval.**
`LeaveType.AttachmentPolicy` (`None`/`Optional`/`Required`) decides whether a request
may be **approved** without a supporting document. Only `Required` refuses anything —
`Optional` is encouragement rendered in amber and `None` offers no upload at all, and
if either could refuse, an admin nudging a type towards documentation would lock
employees out of it instead.

`Application/AnnualLeaves/Commands/AttachmentPolicyRule.cs` is the rule, called
from `UpdateLeaveStatus`, `EditAnnualLeave` and the auto-approving branch of
`CreateAnnualLeave`; `client/src/lib/attachment-policy.ts` mirrors it so no form
offers a submit, and no manager page an Approve, the API is certain to refuse. Keep the two in step, the same way
`ParentalLeaveEligibility` and `parental-leave.ts` are kept in step.

Six things about it that are deliberate:

- **It gates approval, not filing.** It first refused at filing time, and that
  refused exactly the request Military Leave exists for: call-up papers are dated
  the day of service, so the request has to go in before the document exists. The
  rule now runs on every path into `Approved` — `UpdateLeaveStatus`, the status
  path of `EditAnnualLeave` (which also catches an edit clearing the evidence on
  an already-approved row), and the auto-approving branch of `CreateAnnualLeave`,
  where filing *is* approval. A request left Pending is never asked. The client
  mirrors the split: `isAttachmentBlockingSubmit` disables submit only where
  submitting would approve, and `isAwaitingDocument` disables Approve on Team
  Leave and All Leave (a bulk approval skips such rows rather than attempting
  them) and puts "Document needed before approval" on the employee's own pending
  row. That row now has an **Edit** button (`MyLeavePage`) — the API always let an
  employee edit their own pending request, but the row offered only Cancel, so
  there was nowhere to attach anything.
- **`None` means the section is not there.** `isAttachmentOffered` in
  `attachment-policy.ts` decides that: a type set to *No attachment needed* drops
  step 5 off `ApplyLeavePage` (and its "Attachments" summary row) and the evidence
  block off `AnnualLeaveForm` entirely, rather than rendering the dropzone under an
  "(optional)" label. Two consequences to keep. A form that hides the section must
  **drop any file staged behind it** — both forms do, in an effect on the flag —
  or it uploads on submit with nothing on screen saying so. And evidence a request
  *already carries* keeps the block visible in `AnnualLeaveForm` even under `None`:
  that dialog is the only place to open it, and a policy moved to `None` afterwards
  must not hide a document somebody actually filed. Note a fresh form with no type
  chosen reads as `none` too, so the step appears only once a type asks for one.
- **It was display-only until this rule.** The admin dialog saved the policy and
  the leave type's card rendered it, while `ApplyLeavePage` decided the same thing
  from the type's *name* — anything containing "sick" got the amber label, and
  everything else read "(optional)". So a type set to *Attachment required* changed
  nothing an employee could see and submitted happily with no document. The name
  now only picks the wording of step 5's subtitle ("a doctor's note" beats "a
  supporting document" where we can tell); the policy decides everything else.
- **No exemption at approval.** An admin approving on somebody's behalf is refused
  like a manager — the rule is about the leave type, not about who is clicking. But
  an edit that leaves a request Pending is not an approval, so an admin fixing the
  reason on an undocumented request (one filed before the policy was set to
  `Required`) is no longer refused; the request simply cannot be approved until a
  document is attached. A pending undocumented request under `Required` is
  therefore a hard stop for the approver, not a warning. That is the rule as
  chosen, not an oversight.
- **Whitespace is not an attachment.** `AnnualLeave.EvidenceUrl` is free text, so
  the check trims before believing it.

**A half day is stored and charged, not just offered.** `AnnualLeave.Duration`
(`Full`/`HalfDayMorning`/`HalfDayAfternoon`) is what makes the apply page's
"Half day (AM)" and "Half day (PM)" buttons mean anything.
`Application/AnnualLeaves/Commands/HalfDayRule.cs` is the rule, called from
`CreateAnnualLeave` and `EditAnnualLeave`; `client/src/lib/half-day.ts` mirrors it
so neither form offers a submit the API is certain to refuse. Keep the two in step,
the same way `AttachmentPolicyRule` and `attachment-policy.ts` are kept in step.

It was display-only before. The buttons showed for every type regardless of
`LeaveType.HalfDayAllowed`, the payload carried no duration at all, and the summary
panel's "Days deducted 0.5" was a number the server never charged — a half day was
stored and deducted as a whole one. `LeaveCalculationService.CalculateChargeableDays`
and the `LeaveDuration` enum were written for this and had no callers.

Six things about it that are deliberate:

- **A half day covers exactly one date**, and `HalfDayRule` refuses anything wider.
  This is where the bug was most visible: the calendar's first click sets the start
  and clears the end, which is right for a range and wrong for a half day — the form
  sat at "End date —, Working days 0" with submit disabled and no way forward but
  clicking the same cell twice. `collapseToHalfDay` in `half-day.ts` is the one place
  that correction lives, and both forms run it.
- **The charge is a flat 0.5, not `businessDays * 0.5`.** A half day is half a day,
  not half of however many days the range covers; the old expression would have
  billed 2.5 days for a week-long request calling itself a half day.
- **A half day on a weekend or public holiday charges 0 and is *not* refused**,
  matching a full-day request over the same dates, which counts 0 rather than being
  refused. A stricter rule for half days alone would be a surprise with nothing
  behind it.
- **`EmployeeProfile.LeaveBalance` is `decimal(5,2)`; `AnnualLeaveEntitlement` stays
  `int`** (migration `AddLeaveDurationAndFractionalBalance`). An allowance is stamped
  from `LeaveType.DefaultAllowance` and is always whole days — only what is left of
  one can be a fraction. `AnnualLeaveDto.TotalDays` is decimal for the same reason,
  and lost the `[Range(1, int.MaxValue)]` that would now reject 0.5.
- **Both ledgers charge 0.5, not just the pooled one.** `PerChildLeaveBalanceCalculator`
  counts chargeable days too, so a half day taken against a child consumes 0.5 of
  that child's ledger. Paternity Leave is seeded `HalfDayAllowed = false`, but an
  admin can turn it on, and a ledger that disagreed with the request would be worse
  than the restriction.
- **`AnnualLeaveForm` needs its own duration control because it is a full replace.**
  It posts every field it holds, so a field it does not hold is a field it silently
  resets — without the control, an admin fixing a typo in the reason would promote a
  half day to a full one and take another half day off the balance. Its reset effect
  is guarded on the leave type having *resolved*, not just on `halfDayOffered`: the
  type list lands a tick after the dialog opens, and until it does every type reads
  as "no half days", which would wipe the duration before anyone touched anything.

**Approval can take two stages: the manager's, then HR's.** The one "Requires
approval" switch is now two columns on the leave type, `RequiresManagerApproval`
(the renamed old column — migration `SplitLeaveApprovalIntoManagerAndHr` keeps
every value) and `RequiresHrApproval` (new, default off, so nothing changed until
an admin flips one). `Application/AnnualLeaves/Commands/ApprovalStageRule.cs` is
the rule, called from `CreateAnnualLeave`, `UpdateLeaveStatus` and the status path
of `EditAnnualLeave`; `client/src/lib/approval-stage.ts` mirrors it so a page never
offers an Approve the API refuses. Keep the two in step, the same way
`AttachmentPolicyRule` and `attachment-policy.ts` are kept in step.

| Manager | HR | Filed as | Who finishes it |
|---|---|---|---|
| off | off | `Approved` | nobody — filing is approval |
| on | off | `Pending` | a Manager in the department — HR sees it once approved, to cancel it before it starts |
| off | on | `AwaitingHrApproval` | an HR Administrator |
| on | on | `Pending` | a Manager's Approve moves it to `AwaitingHrApproval`; an HR Administrator's finishes it from there |

Eight things about it that are deliberate:

- **The client always asks for `Approved`; the server decides the stage.** A
  client-supplied `AwaitingHrApproval` is refused (`StageIsDerivedMessage`) —
  on a row already in that status it is a no-op, like any same-status request.
  `ApprovalStageRule.Resolve` opens with an idempotence guard — asking for the
  status a request already has is a no-op (`Outcome(current, null)`), so a
  retried Approve on an already-approved row never sends it back to HR. So
  the approve buttons on Team Leave, All Leave and both dashboards are one button
  whose *label* changes ("Approve & send to HR", from `approveOutcome`), not two.
  `approveOutcome` returns `'awaiting-hr'` from any status but `AwaitingHrApproval`
  when the type needs HR and the viewer is not HR, matching the server, which
  applies the HR check regardless of the starting status; the Team Leave view
  dialog layers `canApproveInDialog` / `canRejectInDialog` on top of the ordinary
  `canDecide` for the same reason — a Rejected request may be re-approved and an
  Approved one rejected there, not just a Pending one decided.
- **Each stage belongs to its own role; HR does not stand in for the manager.**
  A Manager cannot approve, reject or cancel a request that is with HR
  (`AwaitingHrMessage`); they had their say at stage one. An HR Administrator
  cannot approve, reject or cancel a `Pending` request on a type that asks for
  the manager (`WithManagerMessage`, `ApprovalStageRule.IsWithManager`) — whether
  or not HR comes after — and their pages leave such rows out altogether: the
  HR dashboard's queue and Leave Management (`isWithManager` in
  `approval-stage.ts`, applied to the list and the stat card; the status tab bar
  is not rendered for HR at all, since it would be mostly zeros) show a
  manager-stage request once the manager has **approved** it. A manager's
  rejection stays off HR's page too (`isManagersRejection`): a Rejected row is
  HR's to see only when the status history says the rejection came out of
  `AwaitingHrApproval`, i.e. HR made it; with no history the type decides, as
  for a Pending row. The HR Administrator's bell in the Topbar takes the
  manager's shape and lists the requests awaiting HR approval (plus submitted
  timesheets in their reach), where it used to hand them the employee's status
  feed.
  `canDecide`, `canApproveInDialog` and `canRejectInDialog` therefore take the
  leave type; a type not yet loaded reads as manager-only, matching the server's
  reading of a deleted one. The one Pending row HR may still decide is on a type
  whose manager switch is *off* — a legacy row, or one reconfigured after filing —
  since nobody else can. It used to be the other way round (HR's Approve from
  Pending finished a request in one step, as the HR sign-off), which put every
  manager's queue in front of HR with Approve/Reject to hand. "The caller is an
  HR Administrator" is `IsAdmin` on `UpdateLeaveStatus`, and `actsAsAdmin` (HR
  Administrator in scope) on `EditAnnualLeave` — both scoped by the same
  `ManagerAccessScopeResolver` test as a Manager, and that flag is what the rule
  reads. Note the consequence for a `Pending` row nobody can reach — a
  department whose only manager has since left, or an administrator's own
  department-less request filed before this rule: it waits for a manager to be
  assigned (new ones are routed at filing, next bullet but one). Tests that used
  the HR Administrator as a convenient approver seed their rows in
  `AwaitingHrApproval` now, or decide as the Manager.
- **An approved leave can be cancelled only until it starts.**
  `Application/AnnualLeaves/Commands/CancellationRule.cs` refuses `Approved` →
  `Cancelled` once the start date is behind today's UTC date
  (`AlreadyStartedMessage`), for whoever is cancelling — the days were taken, and
  handing them back to the balance would misstate an absence that happened. It
  measures only that transition: taking an approval back with Reject, or
  cancelling a request never approved, is unchanged. Called from
  `UpdateLeaveStatus` (which takes a nullable `NowUtc` test seam, like the
  attendance queries) and the status path of `EditAnnualLeave` (against the dates
  as edited). `canCancelApproved` in `approval-stage.ts` mirrors it, and is what
  puts the **Cancel** button on an HR Administrator's approved rows on Leave
  Management (a reason dialog, `updateLeaveStatus(id, 'Cancelled', reason)`; the
  delegate is stood down and the employee emailed as before). A System
  Administrator gets no such button — they neither file nor decide leave, and the
  server refuses them anyway.
- **Balance, per-child ledger, coverage announcement, `ApprovedAt`/`ApprovedById`
  move only into `Approved`.** `AwaitingHrApproval` charges nothing and tells the
  delegate nothing. The attachment policy runs on *both* steps out of `Pending`,
  so a manager cannot pass an undocumented request along.
- **`AwaitingHrApproval` is open like `Pending`** — for the overlap checks in both
  leave validators, the employee's Cancel (`DeleteAnnualLeave`), the
  pending-approvals reminder, the queues and the Pending tabs (`isOpenStatus`) —
  **but locked for editing like `Approved`.** The manager's bell in the Topbar and
  the pending-approvals digest count only what a manager can decide (`Pending`);
  an HR Administrator's bell counts the rows with HR, and they get their own
  digest of those in their departments. A manager who *did* approve specific dates at stage one would have
  an edit that kept the stage put different dates in front of HR under their
  name — but an HR-only type reaches this status straight from filing, with no
  manager stage to have approved anything, so the message told to whoever cannot
  edit it is neutral ("This request is awaiting HR approval…"), not manager-
  specific. Only an HR Administrator in scope (`actsAsAdmin`) may edit it;
  everyone else is told to cancel and file again.
- **Who is told.** `HrApprovalRecipients` (beside `ManagerNotificationRecipients`)
  is the HR Administrators whose `UserDepartment` rows cover the leave's
  department, or every active one for a department-less leave, and
  `HrApprovalNotification` mails them with the reason and the coverage line when
  a request reaches the HR stage — on filing for an HR-only type, on the manager's
  approval otherwise. The employee's status email for that step reads "approved
  by {manager} and is awaiting HR approval". A department with no HR
  Administrator assigned leaves an HR-stage request stuck, the same way a
  department with no manager leaves a `Pending` one.
- **A manager on leave, or no manager at all, hands the manager stage to HR.**
  At filing, `CreateAnnualLeave` asks `ManagerAvailability.CheckAsync` whether
  any of the managers who would be emailed about the request (the same set
  `ManagerNotificationRecipients` resolves) is *not* on an `Approved` leave
  covering today. If every one of them is away — or the set is empty
  (`Report.NoManager`) — `ApprovalStageRule.InitialStatus(leaveType,
  managerAvailable: false)` files the request straight into `AwaitingHrApproval`:
  HR is emailed with a "Note: Sent to HR for approval: {names} on leave." line
  (or "…: no manager in the department to decide it."), the absent manager gets
  no new-request email, and a history row (Pending → AwaitingHrApproval, same
  comment) tells the employee and the returning manager why it skipped them. A
  department-less request — an administrator's own — has no manager set either,
  so it goes the same way. The empty set used to read as *available* and file
  Pending, on the reasoning that HR could decide it there; HR no longer can
  (previous bullet but one), so it would have waited on nobody. Two edges are
  deliberate: the check runs at filing only, so a manager who goes on leave, or
  leaves the department, after the request came in does not move it; and "today"
  is the UTC date the leave rows themselves are stored on.
- **Changing the switches sweeps what is in flight** (`UpdateLeaveType`): every
  switch off approves every open row, balance-checked; HR off approves the rows
  with HR, whose manager stage is done; Manager off while HR stays on moves
  `Pending` rows to HR. HR-only becoming Manager-only in one save is a fourth
  case: the rows with HR move back to `Pending` (history "Moved to manager
  approval based on leave type settings."), since they never had a manager
  stage and the save just turned one on — `hrDropped` therefore requires that
  Manager was already on, not newly switched on in the same save, so this case
  is routed separately. Switching anything *on* alone moves nothing. As with the
  old auto-approval sweep, nothing is emailed.

The enum value is appended (`AwaitingHrApproval = 4`) so stored values keep their
meaning, and the card's Enabled toggle sends both flags (see the trap at the end
of this section). No seeded type turns HR approval on.

**The two limits on the leave type are enforced, in different units.**
`LeaveType.MinNoticeDays` bounds how soon a request may start and
`LeaveType.MaxConsecutiveDays` how long it may run.
`Application/AnnualLeaves/Commands/NoticePeriodRule.cs` and `MaxConsecutiveRule.cs`
are the rules, called from `CreateAnnualLeave` and `EditAnnualLeave`;
`client/src/lib/leave-limits.ts` mirrors both. Keep them in step, the same way
`AttachmentPolicyRule` and `attachment-policy.ts` are kept in step.

Both were display-only before. The admin dialog saved them and the type's card
rendered "Minimum 7 days notice required" and "Max 15 consecutive days per
request", while `ApplyLeavePage` warned "Short notice" below a hardcoded seven
days for every type alike and never mentioned length at all — the same shape of
guess as the old `name.includes('sick')` attachment sniff. A type asking 30 days
notice changed nothing an employee could see.

Five things about them that are deliberate:

- **Notice is counted in calendar days; the maximum in business days.** "30 days
  notice" is how an HR policy states it, and the card says "days notice" plainly.
  The maximum instead counts what `AnnualLeave.TotalDays` holds, so "17
  consecutive days" and "17 days deducted" are the same 17 — which is what the
  seeded data already assumed: Paternity Leave's 25 is exactly its
  `PerChildWeeksPerYear` of 5 at `BusinessDaysPerWeek`, and Maternity Leave's 90
  matches its own 90-day allowance.
- **A 0 in either is "no limit", not "nothing allowed"** — the opposite reading of
  a 0 `DefaultAllowance`, and the same trap `MaxCarryoverDays` documents.
- **Notice is re-checked on an edit only when the start date moves.** It is the
  one limit with a clock in it: a request filed properly in advance drifts towards
  its own start date every day it sits there, so checking it on every edit would
  strand it — the reason could not be corrected the morning before a trip. The
  maximum has no clock and is checked on every edit. Neither is re-checked in
  `UpdateLeaveStatus`: re-testing notice at approval would refuse leave purely
  because the manager was slow.
- **No exemption for an admin**, matching `AttachmentPolicyRule`. Note this bites
  on real data: Maternity is seeded at 30 days notice and Sabbatical at 60, and
  nothing enforced them before, so requests that were accepted yesterday are
  refused now.
- **`AnnualLeaveForm` blocks on notice but only *warns* on length.** Its
  `requestedDays` excludes weekends but not public holidays — that dialog has no
  holiday list — so the figure only ever errs high, and blocking on it would
  refuse requests `MaxConsecutiveRule` allows. `ApplyLeavePage` has the holiday
  set and blocks on both. A mirror may under-refuse; it must never over-refuse.

`ApplyLeavePage` also dims calendar days inside the notice period, so the limit
reads like the weekends and public holidays beside it — but only for a type that
actually asks for notice, since a 0 would otherwise put the earliest start at
today and quietly ban backdating, which no rule here does.

**A leave type can ask for a length of service.** `LeaveType.MinServiceMonths` is
how long somebody has to have worked here before the type is offered to them,
measured from `EmployeeProfile.EmploymentStartDate` to **the day they file**.
`Application/AnnualLeaves/Commands/MinimumServiceRule.cs` is the rule, called from
`CreateAnnualLeave` and `EditAnnualLeave`; `minServiceError` in
`client/src/lib/leave-limits.ts` mirrors it and `isLeaveTypeOffered` applies it, so
every surface that filters the type list — `ApplyLeavePage`, `MyLeavePage`,
`DashboardHome`, and `AnnualLeaveForm` for the employee's own request — hides the
card until the months are served, then shows it. Keep the two in step, the same
way `NoticePeriodRule` and `leave-limits.ts` are kept in step.

The seeded data promised this in prose and enforced nothing: Unpaid Leave's chip
read "Employees after 1yr" and Sabbatical's "Tenured employees (5+ years)".
Migration `AddLeaveTypeMinService` stamps those two at 12 and 60; everything else
is 0. Five things about it that are deliberate:

- **Measured to today, not to the leave's start date.** The type appears on the
  employee's anniversary and not before, so someone at seven weeks cannot book a
  two-month type for the autumn. That is the rule as chosen — it is what makes
  "appears once you have served N months" true — not an oversight. Doing it the
  other way would mean always showing the card and refusing early dates, the way
  the notice period does.
- **A 0 is "no minimum"**, the same reading as `MinNoticeDays` and
  `MaxConsecutiveDays`, and the opposite of a 0 `DefaultAllowance`. The validator
  caps it at 120 months.
- **A `null` start date passes**, on both sides. It means nobody entered it — an
  System Administrator never has one, since the validators refuse it for the role, and an Employee
  row predating the column has none until next saved — and refusing on a blank would
  take the type away from everyone until an admin filled the field in one person at
  a time. The same reasoning as a `null` `Gender` in `ParentalLeaveEligibility`.
- **Re-checked on every edit**, unlike notice. Service only grows, so a request
  accepted once can never later fail it; what an edit has to catch is a switch onto
  a type wanting more service than the employee has. No exemption for an admin
  filing on somebody's behalf, matching `AttachmentPolicyRule`.
- **Month arithmetic clamps the day** the way .NET's `DateOnly.AddMonths` does: 31
  January plus one month is 28 February. The JS `Date` overflows into March, so
  `leave-limits.ts` has its own `addMonthsClamped` rather than `setMonth`; without
  it a month-end hire's card would appear a few days after the API started
  accepting them.

Two plumbing notes. `GET /api/account/user-info` (`CurrentUserPayload`) now carries
`employmentStartDate`, since the client decides with it — like `gender`, dropping it
fails open. And `GetLeaveTypeList` projects its columns by hand; `AvailableTo` had
been left out of that projection, so a custom type restricted to one gender listed
as `Both` and the card's Enabled toggle would have written that `Both` back. Both
columns are projected now and `MinServiceMonthsPlumbingTests` pins the round trip.

**A leave type can pro-rate the first year.** `LeaveType.ProRateFirstYear` is a
switch on any type with a flat allowance: on, somebody who joins part-way through
a leave year gets that year's allowance of *this type* in proportion — remaining
months over twelve, the joining month counted in full, rounded **up** to the next
half day. A September start on 23 days is 23 × 4/12 = 7.67, so 8; the same start
in an April-to-March leave year is 7/12, so 13.5.
`LeaveCalculationService.ProRateFirstYearEntitlement` is the arithmetic and
`AnnualLeaveBalanceCalculator.EntitlementForLeaveYear` applies it per leave year,
inside both the approval-time check and `SyncCurrentYearBalanceAsync`. The switch
is refused on a per-child type (`UpsertLeaveTypeRequestValidator`): that budget is
bounded by the child's age, not the leave year, so there is nothing to scale, and
the dialog hides it for the two.

**Only the balance type's pro-rating is enforced.** The server never enforces a
non-balance type's `DefaultAllowance` — sick leave's 10 days is a figure the
balance rows quote, not a quota the API refuses past — so the switch on such a
type scales what the rows *say* and nothing else. A September joiner on 10 sick
days reads "of 3.5", and an eleventh sick day is not refused. That is consistent
with how those allowances already behave, and it was chosen with eyes open rather
than overlooked: making it real would mean enforcing every per-type allowance,
which is a separate feature.

Six things about it that are deliberate:

- **Nothing per person is written.** `EmployeeProfile.AnnualLeaveEntitlement`
  stays the full allowance — the "never edited per person" rule under
  [Leave is configured once](#domain-model-summary) still holds — and the
  pro-rating is applied to the one leave year the start date falls in. That is
  why the second year is full without a year-end job or a re-stamp, and why
  flipping the switch on a database full of profiles is safe: it changes what is
  enforced, not what is stored. A start date before the leave year, or none on
  file, is the full allowance; one after the leave year ends is 0 — and note that
  0 reaches `CalculateRemainingBalance`, not the `<= 0` early return, so it
  refuses rather than unpolices.
- **`LeaveBalance` is re-synced by everything that moves an input.** `CreateAdminUser`
  stamps the pro-rated figure on hire (the entitlement whole beside it),
  `EditEmployeeProfile` re-syncs when the start date moves, and `UpdateLeaveType`
  re-syncs every profile when the switch flips, the way an allowance move does.
  `ProRatedFirstYearPlumbingTests` pins all three.
- **The screens quote a computed figure, not the stored one.**
  `EmployeeProfileDto.CurrentYearEntitlement` is worked out in
  `GetEmployeeProfileList` on every read; Dashboard, My Leave and Apply Leave
  read it through `currentYearEntitlement()` in `client/src/lib/leave-allowance.ts`,
  which falls back to the stored entitlement for an API predating the field
  (`??`, not `||` — a genuine 0 is an answer). The carryover preview on Leave
  Settings keeps the stored figure, because next year is a full year.
- **The client mirrors the arithmetic only for the types the server does not
  compute.** `proRateFirstYearAllowance` in `leave-allowance.ts` is the mirror of
  `ProRateFirstYearEntitlement`, and `allowanceForLeaveTypeThisYear` applies it to
  a non-balance type's own allowance — on the balance rows (`buildLeaveBalanceRows`,
  given a `firstYear` context by Dashboard and My Leave) and on the admin's request
  view (`allowanceForRequest`, whose hover then says "Pro-rated for the first year
  from 10 days/year" rather than calling the figure an override). The balance
  type's figure must always come from the server, never from the mirror, so the
  two rows on one panel cannot drift apart. Keep the mirror in step with the C#,
  the same way `leave-limits.ts` is kept in step with `NoticePeriodRule`.
- **Months, not days, and the joining month counts whatever the day.** "One
  twelfth per month" is how an HR policy states it; a 16 September start and a
  1 September start both get 4/12. Rounding up rather than to the nearest means
  the arithmetic never short-changes the employee, and a half day is already a
  unit the balance understands (`LeaveBalance` is `decimal(5,2)`).
- **It reads the same `EmploymentStartDate` as `MinimumServiceRule`** and, like
  it, passes a `null` — nobody entered it, which is not "started today". The
  dialog shows the switch on every type but the two per-child ones and sends
  `false` for those; the card's Enabled toggle resubmits it like every other
  column (see the trap at the end of this section).

**Coverage is announced, not just recorded.** `AnnualLeave.DelegateId` — the
colleague nominated on step 3 of the apply form — used to be a private note: stored,
rendered in a detail drawer, and told to nobody, so the nominated colleague found
out in the corridor or not at all.
`Application/AnnualLeaves/Commands/CoverageNotification.cs` is the rule that emails
them, and it is called from all three places a leave can reach `Approved`: the
auto-approving branch of `CreateAnnualLeave`, `UpdateLeaveStatus`, and the status
path of `EditAnnualLeave` (the one an admin uses from the edit dialog rather than
the approve button). Five things about it are deliberate:

- **Nothing is announced before approval.** A request can sit `Pending` for days
  and then be rejected, and a team that rearranged itself around a trip that never
  happened is worse off than one told late. The single exception is the `Coverage`
  line on the manager's new-request email, which is part of what the approver is
  deciding.
- **No delegate means no announcement at all**, not an announcement saying nobody
  is covering. The message is about coverage, so with nobody covering there is
  nothing to send — which also keeps the department's inbox for the absences
  somebody actually arranged cover for. The approver's email says
  "Coverage: Nobody nominated" precisely because they are the one person who needs
  to know it was left empty.
- **Neither message carries the leave's `Reason`.** Step 4 of the apply form
  promises the reason stays private; it reaches the manager deciding the request
  and nobody else.
- **The department is the employee's own department, and a null one announces to
  nobody** — a System Administrator has no department, and "the same department as nobody" is not
  a match, the same trap `ManagerNotificationRecipients` documents. A deactivated
  account is never mailed either: a leaver covers nothing.
- **Leaving `Approved` stands the delegate down**, with a note to them alone —
  cancelled leave, or an approval taken back. The department hears nothing further,
  because an absence that went away is visible on the calendar. Swapping the
  delegate on an already-approved leave likewise tells the new delegate only. Note
  what follows: **changing the dates of an announced absence re-announces nothing**,
  so the department keeps the dates it was first told.

`client/src/components/annual-leave/TeamLeavePage.tsx` renders "Covered by X" under
the employee's name on each row, not only in the view dialog, so a manager scanning
next week's absences can see who is holding the fort without opening anything.

**Coverage is mandatory for an Employee and a Manager, and the handover travels
with it.** `Application/AnnualLeaves/Commands/CoverageRule.cs` refuses a request
from either role that names no `DelegateId`, on create and on edit — called from
the two validators, where the other delegate checks already live.
`client/src/lib/coverage.ts` mirrors it so step 3 of `ApplyLeavePage` reads
"(required)" and submit is held until somebody is nominated, and the edit dialog
(`AnnualLeaveForm`) has a "Covered by" select — it used to carry the existing
delegate through untouched with no way to set one, which the rule made untenable:
a request filed before it has to be given a delegate the next time it is saved.
Keep the two in step, the same way `AttachmentPolicyRule` and
`attachment-policy.ts` are kept in step. Six things about it that are deliberate:

- **A System Administrator's own leave is exempt.** A System Administrator has no department, so the picker,
  which offers department colleagues, would offer them nobody, and a required
  field with nothing to put in it is a form that cannot be submitted. The rule
  reads the *employee's* stored role — the payload carries none — so an admin
  filing on somebody's behalf is held to it, matching the other rules. The client
  mirror reads roles unknown as *not* required (an admin editing somebody else's
  request; the DTO carries no role) and lets the server answer: a mirror may
  under-refuse, never over-refuse.
- **A department with no other non-admin member cannot file leave.** That is the
  rule as chosen, not an oversight; the empty picker says so and points at the
  admin.
- **The handover — `AnnualLeave.CoverageNote` (1000 chars) and
  `CoverageAttachmentUrl` — is written to the delegate, so a request naming
  nobody keeps neither.** `CoverageHandover.Apply` drops both on create and edit
  when `DelegateId` is blank; without that, a full-replace edit that dropped the
  delegate would strand a note nobody would ever see. Both forms only show the
  fields once a delegate is chosen for the same reason.
- **Both reach the delegate in the coverage email, and nobody else.**
  `CoverageNotification.AnnounceAsync` adds the note as a detail block and the
  document as a real file attachment; the department's round-robin message is
  unchanged, and the approver's "new request" email still carries the delegate's
  name only. `IEmailService` gained an attachments overload with a default
  implementation (so fakes compile unchanged); `EmailService` fills
  `EmailMessage.Attachments`, which SMTP already sent and Brevo now does too
  (`TransactionalEmailRequest.Attachment`, base64). A handover file that no
  longer resolves drops off the email rather than stopping it, and the loader
  checks the stored file's *purpose*, so a crafted request pointing the handover
  at somebody's evidence cannot get that file mailed out.
- **The document has its own `StoredFilePurpose.CoverageHandover` and its own
  endpoint (`POST /api/annualleaves/coverage-upload`).** `GetStoredFile` lets the
  delegate named on the leave open it — the one reader evidence must never have —
  plus the employee, the uploader, a System Administrator and an in-scope Manager. It accepts
  Word and Excel as well as PDF and images: `FileSignatureValidator` learned the
  four Office kinds, which share a container signature (ZIP, or OLE for the
  legacy pair) and are told apart by a *marker* further in (`word/`, `xl/`, or the
  UTF-16 stream name), so a plain ZIP renamed `.docx` is still refused. Evidence
  accepts Word too now — the apply page had always advertised `.doc/.docx` there
  and refused them on the round trip.
- **An admin filing on behalf needs the *employee's* colleagues, not their own.**
  `GetTeammateList.Query.ForUserId` does that, and the controller passes it
  through for a System Administrator only. `getTeammates(forUserId?)` on the client therefore
  takes an argument, and must be wrapped in a query function rather than passed
  bare to `useQuery`, which would hand it the query context in that slot.

Timing is unchanged: the delegate is told at **approval**, not at filing — see
the first bullet of the previous section for why. A test that submits the apply
page has to nominate somebody first; the apply-page test files do it in their
render helpers (`nominateDelegate`), which also wait for the picker dialog to
close, because MUI marks the rest of the page `aria-hidden` while it is open and
role queries against the form then find nothing.

**The employment start date is role-scoped, not universal.**
`EmployeeProfile.EmploymentStartDate` is when somebody joined — which was recorded
nowhere before. `CreatedAt` is when the *row* was written, so it reads as the day an
admin got round to keying the account in, and as the same day for everybody migrated
in at once.

It lives in the **Profile** section of the admin dialogs, beside the department and
the job title, and it carries that section's rule: **required for an Employee and a
Manager, refused for a System Administrator**, exactly as `DepartmentId` is. The section is already
hidden for a System Administrator, so the scoping needed no new surface — and refusing rather than
ignoring means a promotion to System Administrator *clears* the date rather than stranding a row the
System Administrator's own dialog cannot show. `CreateAdminUserValidator` and
`EditEmployeeProfileRequestValidator` are the rules;
`client/src/lib/validation/person.ts` mirrors them. Keep the two in step, the same
way `AttachmentPolicyRule` and `attachment-policy.ts` are kept in step.

Four things about it that are deliberate:

- **The column is nullable and nothing backfills it.** A hire date for a real
  person is not ours to invent, so the rule is what makes it mandatory: every
  account predating the column has to be given one the next time it is saved.
  That is the same trade `PersonFieldRules.ValidDateOfBirth` documents, and it
  bites the same way — an admin fixing a typo in someone's email has to supply a
  start date first. The demo seed *does* set one (two years back), because a
  seeded record the rule refuses is a demo database that has to be repaired by
  hand before anything can be saved.
- **A future date is accepted; one before their 16th birthday is not.** The
  opposite of the date of birth on both counts. A hire keyed in before their first
  day is ordinary, while a start date decades before the person was born is a typed
  year — so the check reuses `PersonFieldRules.MinimumAgeYears` and is skipped when
  no date of birth is on file, since there is then no age to disagree with.
- **The edit path reads the date of birth from the database, not the payload**
  (`EditEmployeeProfileRequest` carries none). `AdminUsersPanel`'s edit mutation
  therefore saves the *user* before the profile, so the date being checked against
  is the one just stored. Reordering those two calls breaks the age check silently.
- **Two rules read it, and neither edits the allowance.** `MinimumServiceRule`
  measures it against `LeaveType.MinServiceMonths` to decide whether a type is
  offered yet — see [A leave type can ask for a length of service](#domain-model-summary)
  below. And when the balance type sets `ProRateFirstYear`, the first leave year's
  *balance* is scaled from it — see [A leave type can pro-rate the first year](#domain-model-summary)
  below. Neither touches `AnnualLeaveEntitlement`, which is stamped from the leave
  type in full and is never set per person (see
  [Leave is configured once](#domain-model-summary)); the pro-rating is applied on
  every read and check, not written.

Two more traps worth knowing, both found the hard way:

- **`Child` has no soft-delete query filter, and `EmployeeProfile`'s does not
  propagate to it.** A child row outlives a soft-deleted profile. Handlers must
  resolve a child's owner through the filtered `EmployeeProfiles` set (by
  `Child.EmployeeProfileId`) rather than through the `Child.EmployeeProfile`
  navigation, which EF Core nulls out for a soft-deleted owner — and `null` is
  `ChildAccessResolver`'s sentinel for "the caller themselves". Reading the
  navigation instead would let any authenticated employee pass as the owner of an
  orphaned child; see the comment in `Application/Children/Commands/DeleteChild.cs`.
- **Timesheet dates are calendar dates, not instants.** `Timesheet.PeriodStart` and
  `TimesheetEntry.Date` are stored at midnight with no zone and come back as
  `2026-09-21T00:00:00`, so compare them on the first ten characters, the way
  `isoDateOnly` in `NewTimesheetPage` and `TimesheetDailyBreakdown` (the five day
  cards shared by the HR Administrator's expanded row on All Timesheets and the
  manager's View dialog on Team Timesheets) do. Never build a day key from `setHours(0,0,0,0)` and `toISOString()`: east of
  Greenwich that reads local midnight back as the previous UTC day, and the
  breakdown showed Monday's entries under Tue and dropped Friday's altogether
  (`AllTimesheetsPage.test.tsx` and `TeamTimesheetPage.test.tsx` pin it).
- **The leave-type card's Enabled/Disabled toggle resubmits the whole leave type**
  (`toggleActive` in `client/src/components/admin/LeaveTypesPanel.tsx`), not just
  `isActive`. Any new `LeaveType` column has to be added to that payload as well as
  the edit dialog's, or flipping the switch silently zeroes it.
- **Every `Restrict` foreign key onto `User` has to be unpicked in `DeleteAdminUser`**
  (`CleanupUserDependenciesAsync`) and in its mirror `DbInitializer.CleanupUserDependencies`,
  or the delete dies in SQL Server with a raw `DbUpdateException` and the admin sees a
  500. `StoredFile.UploadedById` was the one missed: it only bites once somebody has
  uploaded something, so a developer box with untouched demo accounts deleted fine and
  production could not delete anyone with a profile photo. The rule for a file is in
  `ReleaseUploadedFilesAsync`: one only the leaver referred to goes with them, one a
  surviving leave still points at is handed to the admin doing the deleting.
  `DeleteUserStoredFileCleanupTests` runs the handler over SQLite so the constraint is
  real; the in-memory provider would pass whatever the sweep forgot.

## Key Configuration

- **DB:** SQL Server. The connection string lives in **`API/appsettings.json`** —
  `Server=.` (local default instance), database `jpeople_dev`. There is no
  `appsettings.Development.json`; both `appsettings.json` and
  `appsettings.Production.json` are gitignored, so a fresh clone has neither and
  you must create `API/appsettings.json` before the first run.
  Startup runs `context.Database.MigrateAsync()` and the app **exits** if the
  database is unreachable. If the API dies moments after launch — typically after
  a reboot, when it starts before SQL Server finishes coming up — that is the
  cause; check `API/Logs/worktrack-<date>.jsonl` for "Database migration or
  seeding failed" and just start it again.
- **File uploads:** Stored in the database, not on a CDN. `Application/Files/` holds
  the `StoreFile` command (one place for signature, size and extension validation)
  and the `GetStoredFile` query (per-purpose read authorization). `User.ImageUrl` and
  `AnnualLeave.EvidenceUrl` hold a relative `/api/files/{id}` path served by
  `API/Controllers/FilesController.cs`. Rows predating this still hold absolute
  Cloudinary URLs and keep rendering — the client's `resolveFileUrl` helper
  (`client/src/lib/api/file-url.ts`) accepts both shapes.
- **Email:** Pluggable provider architecture (`Infrastructure/Services/Email/`). `IEmailProvider` has two implementations — `BrevoEmailProvider` (Brevo transactional HTTP API) and `SmtpEmailProvider` (MailKit; Gmail/Office365/Brevo relay). `EmailService` selects one at startup via `Email:Provider` (`"Brevo"` or `"Smtp"`) in `appsettings.json`. Brevo config in the `Brevo` section (`ApiKey`); SMTP config in `MailSettings`. Note: the Brevo account has "Authorised IPs" enabled. This host's public **IPv4** is allowlisted but its rotating IPv6 privacy addresses are not, so the Brevo HTTP client is pinned to IPv4 via a `SocketsHttpHandler.ConnectCallback` in `Infrastructure/DependencyInjection.cs` (otherwise .NET prefers IPv6 → intermittent 401 "unrecognised IP"). See https://app.brevo.com/security/authorised_ips.
- **Logging:** Serilog (`API/Extensions/LoggingExtensions.cs`). Console plus
  newline-delimited JSON in `Logs/worktrack-<date>.jsonl` (14 days). Every request
  log line carries a `CorrelationId`, which is also the `X-Correlation-ID` response
  header and the `traceId` in error bodies — see `API/Middleware/CorrelationIdMiddleware.cs`.
  Override levels with a `Serilog` section in appsettings.
- **Health probes:** `GET /health` is liveness (no checks); `GET /health/ready` checks
  the database (Unhealthy → 503) and the configured mail provider (Degraded → still
  200, result cached 5 minutes). Both anonymous and exempt from rate limiting. See
  `API/Extensions/HealthCheckExtensions.cs`.
  **Give a readiness probe more than 10 seconds.** The mail check is allowed 10s and
  is only cached for 5 minutes, so roughly every 5 minutes one probe pays the full
  cost of reaching the provider — 8s when it is unreachable. A probe that gives up
  sooner disconnects mid-response, and `GlobalExceptionMiddleware` now reports that
  as the caller hanging up (logged at Information) rather than as an unhandled 500;
  it used to be the latter, which is how it was found.
- **OAuth:** None. No external providers are registered — social sign-in was
  removed along with public self-registration (its callback provisioned an
  account for any unrecognised email). `AccountController.Login` is the only
  sign-in path, and `MapIdentityApi` is deliberately not mapped;
  `Tests/WorkTrack.Tests/PublicRegistrationRemovedTests.cs` keeps it that way.
- **Attendance lateness follows the Organization settings, in one place.**
  `Application/Attendance/Support/WorkingDaySchedule.cs` reads
  `AppSettings.WorkingHoursStart`/`WorkingHoursEnd` and converts every stored
  UTC instant into `AppSettings.TimeZoneId` before comparing. A check-in is late
  when its local time is a whole minute or more past the start; the company
  dashboard's "not checked in" issue and synthetic feed rows wait a further
  `NotCheckedInGraceMinutes` (60). Consumers: `GetCompanyAttendance` (issues and
  activity feed), `GetTeamAttendance` (the "Late check-in" note),
  `GetMyAttendanceHistory` (the `late` grade), `GetTeamAttendanceHistory` (the
  check-in chart's minutes, now local) and the daily attendance report in
  `ReminderDispatcher`. Before it, the four queries each hardcoded a UTC hour
  (10:00 on the dashboard and board, 09:00 on the strip) while only the report
  read the settings, so at UTC+3 nothing on screen was late before 13:00 and the
  morning's absences were never flagged. Do not add a new clock hour anywhere in
  `Application/Attendance/`; take the schedule. Two related traps: the
  `TimeZoneId` must be an IANA id (`UpdateAppSettingsValidator` refuses
  anything the host cannot resolve, and `WorkingDaySchedule` falls back to UTC
  for one it cannot) — the settings page used to offer labels like
  "UTC-5 (Eastern)" that no rule could use (migration
  `NormalizeLegacyTimeZoneLabels` rewrote the four) — and every attendance query
  carries a nullable `NowUtc` on its `Query`, a test seam the controllers leave
  null, because the issues and the feed depend on the time of day.
  **The working day is net of the configured break.** `AppSettings.BreakMode`
  (`none`/`fixed`/`flexible`), `BreakStart`/`BreakEnd` (the fixed window, read
  in fixed mode only) and `BreakMinutes` (read in flexible mode only) are set in
  the Working Week section of Organization settings; `WorkingDaySchedule.BreakMinutes`
  is what they come to, and `ScheduledMinutes` is the hours minus it, so the
  daily report's overtime line judges 08:00–17:00 with an hour's lunch as an
  eight-hour day. A window or duration that does not fit the day reads as no
  break there, and `UpdateAppSettingsValidator` refuses saving one
  (`Application/Settings/Support/BreakRules.cs`, shared with the handler's
  backstop). Employees still record their own breaks; nothing pauses anyone.
  `client/src/lib/break-policy.ts` mirrors the reading (`breakAllowanceMinutes`,
  `describeBreakPolicy`, and `breakSettingsError` with the server's messages) so
  My Attendance quotes an allowance the server counts and the settings card holds
  Save rather than taking a 400. Keep the two in step, the same way
  `leave-limits.ts` is kept in step with `NoticePeriodRule`.
  **The break taken is judged against that allowance in one place.**
  `WorkingDaySchedule.BreakVariance(state, nowUtc)` is minutes over (positive) or
  under (negative) `BreakMinutes`, 0 exactly on it, and `null` when there is
  nothing to say: no break configured (a 0 allowance is "no policy", not "no
  breaks"), or a day still open that has not gone over — a shortfall is only news
  once the day is checked out of, since the lunch may still come, while going over
  is reported the moment it happens, a running break included
  (`BreakMinutesTaken`, which adds the open break the calculator's
  `TotalBreakMinutes` leaves out). Every surface carries the server's figure and
  none re-derives it: `TeamMemberAttendanceDto.BreakMinutes`/`BreakVarianceMinutes`
  on the team board ("Break 1h 20m · 20 min over" under the member's row),
  `TodayStateDto`/`DayHistoryDto.BreakVarianceMinutes` on the employee's own page,
  a "N over break allowance" issue on the company dashboard (over only — the panel
  flags what needs attention), and a "Break allowance" section in the daily
  attendance report (over and under, checked-out days only, omitted entirely
  when no break is configured rather than saying "Nobody"). The client words it
  with `describeBreakVariance` in `break-policy.ts` and reads a missing field as
  `null`, so an older API says nothing. `WorkingDayScheduleBreakVarianceTests`,
  `AttendanceBreakAllowanceTests` and `DailyAttendanceReportTests` pin the server;
  `TeamAttendancePage.test.tsx` and `AttendancePage.test.tsx` the client.
- **Reminders follow the Working Week, in one place.** `ReminderBackgroundService`
  ticks once a minute and asks `Application/Reminders/ReminderSchedule.cs` (pure,
  clock and calendar as inputs) whether each reminder on Notification Settings is
  due: its `HH:mm` is read on the org's clock (`AppSettings.TimeZoneId`, via
  `Application/Settings/Support/WorkingWeek.cs`, the shared reading of the weekday
  preset plus public holidays), **nothing is sent on a non-working day**, and a
  weekly reminder goes out on the **first working day of the week** (Monday to
  Sunday, as the timesheet week runs), so a Monday holiday moves it to Tuesday
  rather than skipping the week. It used to fire on `DateTime.Now` — right only
  while the server sat in the org's zone, as the developer box does — every day of
  the year, with weekly ones on Monday alone; only the check-in, check-out and
  daily-report dispatchers asked about the day, each against the UTC date. The
  dispatcher's own checks stay (the on-demand `run-reminder` endpoint bypasses the
  schedule) and delegate to `WorkingWeek`; "today" there is the org-local date,
  while the attendance snapshot it guards still reads the UTC calendar day, which
  is how attendance events are recorded everywhere. The client's
  `lib/working-week.ts` words it ("Every working day at…", "On the first working
  day of each week at…", and the info line naming the days, holiday country and
  zone). `ReminderScheduleTests` pins the server; `orgSettingsWorkingWeek.test.tsx`
  the client. `department-digest` is catalogued and switched on by default but has
  **no dispatcher** — the scheduler logs "no dispatcher implementation" and sends
  nothing.
- **Account deactivation:** `User.IsActive` gates sign-in, enforced inside
  Identity by `API/Security/ActiveUserSignInManager.cs` (overrides
  `CanSignInAsync`), so no sign-in path can miss it. A refusal surfaces as
  `SignInResult.NotAllowed` — the same result an unconfirmed email gives — which
  is why `Login` re-checks `IsActive` to choose between 403 "deactivated" and
  401 "not verified". Toggled by `Application/AdminUsers/Commands/SetAdminUserActive.cs`
  (`PUT /api/adminusers/{id}/active`), which refuses self-deactivation and
  rotates the security stamp so live sessions die at the next revalidation —
  `SecurityStampValidatorOptions.ValidationInterval` is lowered to 1 minute in
  `Program.cs` for that reason. Deliberately distinct from `LockoutEnd`, which
  is the 15-minute brake on password guessing (see `API/Security/LockoutPolicy.cs`).
## Improvements & Roadmap

The following areas have been identified for future enhancement to improve scalability, security, and developer experience:

### 1. Frontend & Routing
- **Standardized Routing:** Replace custom hash-based routing with `react-router` for better deep linking and browser history support.
- **Form Management:** Integrate `react-hook-form` and `zod` for robust client-side validation.
- **Code Splitting:** Implement `React.lazy` for page-level components.

### 2. API & Backend
- **Versioning:** Implement API versioning (e.g., `/api/v1`) to manage breaking changes.
- **Soft Deletes:** Add `IsDeleted` support for `EmployeeProfile` and `Project` entities.

### 3. Security & Resilience
- **Audit Logging:** Add a domain-level audit log to track status changes and sensitive modifications.

### 4. Developer Experience (DX)
- **Containerization:** Add `Dockerfile` and `docker-compose.yml` for simplified environment setup.
- **Test Coverage:** Expand unit and integration tests for leave balance logic and timesheet validations.
- **API Documentation:** Enhance Swagger with XML comments and better DTO descriptions.
