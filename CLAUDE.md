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

**Full-replace update DTOs:** `AdminUpdateUserDto` is a replace, not a patch — `UpdateAdminUser` assigns every field it carries unconditionally, so a `null` in the request genuinely clears the stored value. Anything added to it has to follow that: mixing in a "a null leaves the stored answer alone" rule for one field gives a field that looks editable but quietly refuses to be cleared. A nullable field on such a DTO therefore needs a way for the UI to *send* null — hence the explicit "Not specified" radio beside `User.Gender`.

**Authorization:** Policy-based (`"AnnualLeaveRead"`, `"AnnualLeaveCreate"`, etc.) defined in `API/Program.cs`. Roles: `Admin`, `Manager`, `Employee`. Managers are scoped to their departments in queries.

### Frontend Patterns

**Hash-based routing:** `App.tsx` reads `uiStore.currentPage` (a hash-style string) to decide which component to render. There is no router library — navigation happens by setting `uiStore.currentPage`.

**State split:** MobX (`authStore`, `uiStore`) holds client-only UI/auth state. React Query handles all server state (fetching, caching, invalidation).

**Real-time:** SignalR hub at `/hubs/notifications` sends `notificationsUpdated` events. `App.tsx` listens and calls `queryClient.invalidateQueries()` to refresh relevant caches.

**API client:** Axios instance at `client/src/lib/api/client.ts` (base URL `http://localhost:5000/api`, includes credentials). API modules in `client/src/lib/api/` are thin wrappers returning typed responses.

## Domain Model Summary

| Entity | Key Fields |
|--------|-----------|
| `User` | Extends `IdentityUser`; has `DisplayName`, `ImageUrl`, `IsActive` (may this account sign in — a leaver is switched off rather than deleted, since `DeleteAdminUser` nulls out every approval they gave). `DateOfBirth` and `Gender` are recorded HR data an admin maintains on the Users panel; **nothing consults `Gender`** — in particular it does not gate maternity or paternity leave, and the eligibility notes on those leave types stay decorative. The per-child paternity entitlement below is deliberately gender-neutral. It is nullable and `null` means "not specified", which the dialog offers explicitly so a value set by mistake can be taken back — see **Full-replace update DTOs** under [Backend Patterns](#backend-patterns) |
| `AnnualLeave` | `EmployeeId`, `StartDate/EndDate`, `Status` (enum), `TotalDays` (computed, no weekends). `ChildId` is nullable — required on a request against a `PerChildEntitlement` type, `null` on every row predating the feature (and on any request against a type that isn't per-child), and a `null` `ChildId` counts against no per-child ledger |
| `LeaveType` | `Name`, `IsActive`, `AffectsBalance` (is it deducted from the enforced pool), `DefaultAllowance` and `MaxCarryoverDays` — the allowance and the year-end cap that bounds it, both per type and both edited **only** on Leave Types. See [Leave is configured once](#domain-model-summary). `PerChildEntitlement` plus its three numbers (`PerChildTotalWeeks`, `PerChildWeeksPerYear`, `ChildEligibleUntilAge`) configure the second, per-child ledger — see [the two leave ledgers](#domain-model-summary) below the table. Annual, Maternity and Paternity Leave are **built-in** (`Domain/SystemLeaveTypes.cs`): they cannot be renamed or deleted, though every other setting on them stays editable. Keyed by name, which is sound only because the name is frozen and already unique case-insensitively; `LeaveTypeDto.IsSystem` derives the flag so the client keeps no copy of the list. Annual leave additionally cannot be **disabled** — it is the type the enforced pool is a budget for — but Maternity and Paternity can be, for an organisation that does not offer them |
| `Timesheet` | `EmployeeId`, `PeriodStart/End`, `TotalHours`, `Status` (Draft→Submitted→Approved/Rejected), `DepartmentId` (nullable — the department it was filed under, kept for history so it outlives its author's move; null when the author has none, i.e. an Admin, matching `AnnualLeave.DepartmentId`) |
| `TimesheetEntry` | `TimesheetId`, `ProjectId`, `Date`, `HoursWorked` (decimal 4,2), optional `ActivityTypeId`, `ProjectTypeId` and `ProjectComponentId`. One entry per project **+ type + component** per date |
| `Project` | `Name` (unique), `Code` (unique), `IsActive`; belongs to many `Department` via `ProjectDepartment` (which departments can see it), narrows activities via `ProjectActivityAssignment`, components via `ProjectComponentAssignment`, and its kinds of engagement via `ProjectTypeAssignment` |
| `EmployeeProfile` | Links `User` to `Department`, tracks leave entitlement. `DepartmentId` is **nullable, and null is what an Admin gets** — the role sees every department, so belonging to one grants nothing, and an invented assignment counted for real (headcount, attendance warnings, `DeleteDepartment` blockers). The validators enforce it both ways: required for Employee/Manager, refused for Admin. Anything grouping profiles by department must skip the nulls. `AnnualLeaveEntitlement` and `LeaveBalance` are the pool the API enforces on approval, but are **derived from the annual-leave allowance, never edited per person** — see [Leave is configured once](#domain-model-summary) below the table. **A stored 0 switches the balance check off entirely** (`AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync`), so never write one. `HasChildren` is a tri-state (`null` = never asked, `false` = declared none, `true` = has some) — `HasChildrenDeclaration` refuses `false` while any `Child` row still points at the profile |
| `Child` | One declared child of an `EmployeeProfile`: `Name`, `DateOfBirth`. **Age and eligibility are never stored** — both are computed on every read (`PerChildLeaveCalculationService`), which is what makes a child aging out of paternity leave automatic. Deleting a child with leave against them is refused (`DeleteChild`, and the FK is `Restrict`): the row is what the per-child ledger is queried by, so removing it would erase the record of leave actually taken. An aged-out child is kept and reads as ineligible |
| `ProjectComponent` | Org-wide catalogue of deliverables (DM, Lasernet, jDocs): `Name` (unique), `Icon`, `ColorKey`, `IsActive`. Projects declare theirs via `ProjectComponentAssignment`, and a `TimesheetEntry` logs against one — narrowed by its project the same way the activity is |
| `ProjectType` | Org-wide catalogue of engagement kinds (Task, Issue, Inquiry, Support): `Name` (unique), `Icon`, `ColorKey`, `IsActive`. Projects carry any number via `ProjectTypeAssignment`, or none; a type projects still carry cannot be deleted. A `TimesheetEntry` also logs against one — narrowed to the types its project carries, and the field that narrows its project picker |
| `StoredFile` | An uploaded file's bytes in the database: `Content` (varbinary(max)), `FileName`, `ContentType` (**detected**, never the caller's claim), `Sha256` (also the HTTP ETag), `SizeBytes`, `UploadedById`. `Purpose` (`ProfileImage`, `LeaveEvidence`) drives both what the upload accepts and who may read it back |

Status enums: `AnnualLeaveStatus` (Pending, Approved, Rejected, Cancelled); `TimesheetStatus` (Draft=0, Submitted=1, Approved=2, Rejected=3, Resubmitted=4).

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
copied the configured cap onto the `AffectsBalance` type first). A cap of 0 is an
ordinary policy — nothing carries over — unlike a 0 allowance, which is a hazard.
`client/src/lib/leave-allowance.ts` reads both figures (`annualLeaveAllowance`,
`annualCarryoverCap`).

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

Two more traps worth knowing, both found the hard way:

- **`Child` has no soft-delete query filter, and `EmployeeProfile`'s does not
  propagate to it.** A child row outlives a soft-deleted profile. Handlers must
  resolve a child's owner through the filtered `EmployeeProfiles` set (by
  `Child.EmployeeProfileId`) rather than through the `Child.EmployeeProfile`
  navigation, which EF Core nulls out for a soft-deleted owner — and `null` is
  `ChildAccessResolver`'s sentinel for "the caller themselves". Reading the
  navigation instead would let any authenticated employee pass as the owner of an
  orphaned child; see the comment in `Application/Children/Commands/DeleteChild.cs`.
- **The leave-type card's Enabled/Disabled toggle resubmits the whole leave type**
  (`toggleActive` in `client/src/components/admin/LeaveTypesPanel.tsx`), not just
  `isActive`. Any new `LeaveType` column has to be added to that payload as well as
  the edit dialog's, or flipping the switch silently zeroes it.

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
- **OAuth:** None. No external providers are registered — social sign-in was
  removed along with public self-registration (its callback provisioned an
  account for any unrecognised email). `AccountController.Login` is the only
  sign-in path, and `MapIdentityApi` is deliberately not mapped;
  `Tests/WorkTrack.Tests/PublicRegistrationRemovedTests.cs` keeps it that way.
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
