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
Infrastructure  → Domain (Email, Cloudinary, config)
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

**Authorization:** Policy-based (`"AnnualLeaveRead"`, `"AnnualLeaveCreate"`, etc.) defined in `API/Program.cs`. Roles: `Admin`, `Manager`, `Employee`. Managers are scoped to their departments in queries.

### Frontend Patterns

**Hash-based routing:** `App.tsx` reads `uiStore.currentPage` (a hash-style string) to decide which component to render. There is no router library — navigation happens by setting `uiStore.currentPage`.

**State split:** MobX (`authStore`, `uiStore`) holds client-only UI/auth state. React Query handles all server state (fetching, caching, invalidation).

**Real-time:** SignalR hub at `/hubs/notifications` sends `notificationsUpdated` events. `App.tsx` listens and calls `queryClient.invalidateQueries()` to refresh relevant caches.

**API client:** Axios instance at `client/src/lib/api/client.ts` (base URL `http://localhost:5000/api`, includes credentials). API modules in `client/src/lib/api/` are thin wrappers returning typed responses.

## Domain Model Summary

| Entity | Key Fields |
|--------|-----------|
| `User` | Extends `IdentityUser`; has `DisplayName`, `ImageUrl` |
| `AnnualLeave` | `EmployeeId`, `StartDate/EndDate`, `Status` (enum), `TotalDays` (computed, no weekends) |
| `Timesheet` | `EmployeeId`, `PeriodStart/End`, `TotalHours`, `Status` (Draft→Submitted→Approved/Rejected) |
| `TimesheetEntry` | `TimesheetId`, `ProjectId`, `Date`, `HoursWorked` (decimal 4,2), optional `ActivityTypeId`, `ProjectTypeId` and `ProjectComponentId`. One entry per project **+ type + component** per date |
| `Project` | `Name` (unique), `Code` (unique), `IsActive`; belongs to many `Department` via `ProjectDepartment` (which departments can see it), narrows activities via `ProjectActivityAssignment`, components via `ProjectComponentAssignment`, and its kinds of engagement via `ProjectTypeAssignment` |
| `EmployeeProfile` | Links `User` to `Department`, tracks leave entitlement |
| `ProjectComponent` | Org-wide catalogue of deliverables (DM, Lasernet, jDocs): `Name` (unique), `Icon`, `ColorKey`, `IsActive`. Projects declare theirs via `ProjectComponentAssignment`, and a `TimesheetEntry` logs against one — narrowed by its project the same way the activity is |
| `ProjectType` | Org-wide catalogue of engagement kinds (Task, Issue, Inquiry, Support): `Name` (unique), `Icon`, `ColorKey`, `IsActive`. Projects carry any number via `ProjectTypeAssignment`, or none; a type projects still carry cannot be deleted. A `TimesheetEntry` also logs against one — narrowed to the types its project carries, and the field that narrows its project picker |

Status enums: `AnnualLeaveStatus` (Pending, Approved, Rejected, Cancelled); `TimesheetStatus` (Draft=0, Submitted=1, Approved=2, Rejected=3, Resubmitted=4).

## Key Configuration

- **DB:** SQL Server, connection string in `API/appsettings.Development.json` (`WorkTrack` database, trusted connection)
- **Cloudinary:** Used for profile image and evidence file uploads
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
- **OAuth:** Google and GitHub OAuth configured in `appsettings.json`; both are optional (skipped if `ClientId` is empty)
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
