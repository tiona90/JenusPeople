# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WorkTrack is a full-stack leave management and timesheet tracking application built with ASP.NET Core 10 (Clean Architecture) and React 19 (TypeScript + Vite).

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

The solution follows **Clean Architecture** with strict layer boundaries:

```
Domain          → no dependencies
Persistence     → depends on Domain
Application     → depends on Domain (MediatR CQRS)
Infrastructure  → depends on Application (Email, Config)
API             → depends on all layers
client/         → React SPA (separate)
```

### Backend Patterns

**CQRS via MediatR:** All business logic lives in `Application/*/Queries/` and `Application/*/Commands/`. Controllers are thin — they dispatch to MediatR and call `HandleResult<T>()`.

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
| `TimesheetEntry` | `TimesheetId`, `ProjectId`, `Date`, `HoursWorked` (decimal 4,2) |
| `Project` | `Name` (unique), `Code` (unique), `IsActive`, `DepartmentId` |
| `EmployeeProfile` | Links `User` to `Department`, tracks leave entitlement |

Status enums: `AnnualLeaveStatus` (Pending, Approved, Rejected, Cancelled); `TimesheetStatus` (Draft=0, Submitted=1, Approved=2, Rejected=3, Resubmitted=4).

## Key Configuration

- **DB:** SQL Server, connection string in `API/appsettings.Development.json` (`WorkTrack` database, trusted connection)
- **Cloudinary:** Used for profile image and evidence file uploads
- **Email:** Resend API (`API/appsettings.json` → `Resend:ApiToken`) with SMTP fallback
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
- **Structured Logging:** Integrate Serilog for better production observability.

### 3. Security & Resilience
- **Rate Limiting:** Implement built-in ASP.NET Core rate limiting.
- **Audit Logging:** Add a domain-level audit log to track status changes and sensitive modifications.

### 4. Developer Experience (DX)
- **Containerization:** Add `Dockerfile` and `docker-compose.yml` for simplified environment setup.
- **Test Coverage:** Expand unit and integration tests for leave balance logic and timesheet validations.
- **API Documentation:** Enhance Swagger with XML comments and better DTO descriptions.
