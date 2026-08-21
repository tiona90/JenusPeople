<!-- refreshed: 2026-08-21 -->
# Architecture

**Analysis Date:** 2026-08-21

## System Overview

WorkTrack is a full-stack leave management and timesheet tracking application. It consists of a layered ASP.NET Core 10 backend and a React 19 frontend communicating via REST API and real-time SignalR updates.

```text
┌─────────────────────────────────────────────────────────────────┐
│                      Frontend (React 19)                         │
│   Pages, Forms, Components, API Client, State Management         │
│  `client/src/components/`, `client/src/lib/api/`                │
└────────────────────────────┬────────────────────────────────────┘
                             │ (HTTP REST + SignalR WebSocket)
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                API Layer (ASP.NET Core 10)                       │
│    Controllers, Middleware, Hubs, Health Checks, Security        │
│  `API/Controllers/`, `API/Hubs/NotificationsHub.cs`             │
├──────────────────────────────────────────────────────────────────┤
│                Application Layer (MediatR CQRS)                  │
│    Handlers, Queries, Commands, Validators, Business Logic      │
│  `Application/*/Commands/`, `Application/*/Queries/`            │
├──────────────────────────────────────────────────────────────────┤
│           Persistence Layer (EF Core + AppDbContext)             │
│    Database context, entity configurations, migrations          │
│  `Persistence/AppDbContext.cs`, `Persistence/Migrations/`       │
│  Domain interceptors, model building rules                       │
├──────────────────────────────────────────────────────────────────┤
│  Infrastructure Layer (Email, Cloudinary, Nager Holidays)        │
│    Email providers, file upload, external service clients       │
│  `Infrastructure/Services/`, `Infrastructure/DependencyInjection.cs`
└──────────────────────────────────────────────────────────────────┘
                             │
┌────────────────────────────▼───────────────────────────────────┐
│                   Domain Layer (Entities)                       │
│    User, AnnualLeave, Timesheet, Project, EmployeeProfile      │
│    Service contracts: IEmailService, IFileUploadService        │
│  `Domain/*.cs`, `Domain/Interfaces/`                          │
└─────────────────────────────────────────────────────────────────┘
                             │
┌────────────────────────────▼───────────────────────────────────┐
│              SQL Server Database (Trusted Connection)            │
│  Database: "WorkTrack", local development machine               │
└─────────────────────────────────────────────────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| Controllers | Thin routing layer; dispatch to MediatR, check auth, map Result<T> to HTTP | `API/Controllers/*.cs` |
| MediatR Handlers | Encapsulate business logic; query/command handlers with FluentValidation | `Application/*/Commands/`, `Application/*/Queries/` |
| AppDbContext | EF Core context; direct DbContext injection into handlers (no repository layer) | `Persistence/AppDbContext.cs` |
| SignalR Hub | Real-time notifications; manages client group membership by role/department | `API/Hubs/NotificationsHub.cs` |
| Infrastructure Services | Email, file upload, holiday API client (pluggable email providers) | `Infrastructure/Services/` |
| React Components | Pages, forms, layouts; hooks for data fetching via React Query | `client/src/components/`, `client/src/features/` |
| MobX Stores | Session auth state, UI navigation state (uiStore, authStore) | `client/src/lib/mobx/` |
| React Query | Server state caching, automatic invalidation on SignalR updates | `client/src/lib/react-query/` |
| Axios Client | HTTP request wrapper with error event dispatch to listeners | `client/src/lib/api/client.ts` |

## Pattern Overview

**Overall:** Layered architecture with deliberate non-inverted dependencies and direct AppDbContext injection into handlers.

**Key Characteristics:**
- MediatR CQRS with FluentValidation pipeline behavior for automatic request validation
- Result<T> pattern (Success/Failure/Conflict/Forbidden) to avoid throwing business errors
- Direct EF Core queries in handlers; no repository abstraction
- Controllers are thin, dispatching to handlers and mapping responses
- SignalR groups by role and department scope for real-time invalidation
- React Router for navigation; MobX for session/UI state; React Query for server state
- Axios error events broadcast to global listeners (API error toast in App.tsx)

## Layers

**API (HTTP Entry Point):**
- Purpose: HTTP routing, request/response mapping, authentication, authorization checks
- Location: `API/`
- Contains: Controllers, middleware, health checks, SignalR hubs, background services
- Depends on: Application, Infrastructure
- Used by: React client via HTTP and WebSocket

**Application (Business Logic):**
- Purpose: Encapsulate use cases; run validation, authorization, and workflows
- Location: `Application/`
- Contains: MediatR handlers (Queries and Commands), FluentValidation validators, DTOs, support classes
- Depends on: Domain, Persistence
- Used by: API controllers dispatch requests to handlers; handlers return Result<T>

**Persistence (Data Access):**
- Purpose: EF Core context, entity configurations, migrations, interceptors
- Location: `Persistence/`
- Contains: AppDbContext, OnModelCreating entity fluent configs, migrations, audit interceptors
- Depends on: Domain
- Used by: Application handlers query and mutate via context.DbSet<T>

**Infrastructure (External Services):**
- Purpose: Email, file upload, public holidays API, configuration
- Location: `Infrastructure/`
- Contains: Service implementations (email providers, file upload to Cloudinary, Nager client), dependency injection setup
- Depends on: Domain (implements its interfaces)
- Used by: Application handlers, API services

**Domain (Core Entities):**
- Purpose: Business entities, enums, service contracts, no external dependencies
- Location: `Domain/`
- Contains: User, AnnualLeave, Timesheet, Project, EmployeeProfile, Departments, LeaveTypes, etc.; IEmailService, IFileUploadService interfaces
- Depends on: (nothing — only ASP.NET Identity)
- Used by: All other layers

## Data Flow

### Primary Leave Request Flow

1. **React Form** (`client/src/components/annual-leave/ApplyLeavePage.tsx`)
   - User fills form with start date, end date, leave type, reason
   - Form validated with zod schema (client-side)
   - Submit → Axios POST to `/api/annual-leaves`

2. **HTTP Request** 
   - Axios client sets credentials, serializes JSON
   - Request arrives at controller with authorization header

3. **Controller** (`API/Controllers/AnnualLeavesController.cs:CreateAnnualLeave`)
   - `[Authorize(Policy = "AnnualLeaveCreate")]` checks auth
   - Dispatches `CreateAnnualLeave.Command` to MediatR
   - Calls `NotifyForLeaveAsync()` if successful to resolve notification audience
   - Returns `HandleResult()` to map Result<T> → HTTP status

4. **MediatR ValidationBehavior** (`Application/Core/ValidationBehavior.cs`)
   - Intercepts command before handler
   - Runs `CreateAnnualLeave.Validator` (FluentValidation)
   - On validation failure, throws ValidationException (caught by global exception middleware)
   - On success, passes to handler

5. **Handler** (`Application/AnnualLeaves/Commands/CreateAnnualLeave.cs`)
   - Injects `AppDbContext` directly (no repository)
   - Maps DTO to domain entity (AutoMapper)
   - Queries database for EmployeeProfile, LeaveType
   - Creates AnnualLeave entity, determines status (Pending or Approved)
   - **Transaction**: `context.Database.BeginTransactionAsync()` if auto-approval
   - Calls `context.SaveChangesAsync()`
   - Sends email notifications to managers via `IEmailService.SendEmailAsync()`
   - Returns `Result<string>.Success(leaveId)`

6. **Controller Maps Response**
   - Result is Success → returns `HandleResult()` → `Ok(leaveId)` (200 + ID)
   - Result is Failure → `HandleResult()` → maps to 404/409/403/400 based on ErrorKind

7. **HTTP Response** → React
   - 200 + leave ID on success
   - On error, global error listener fires (Axios interceptor)
   - Dispatches `API_ERROR_EVENT` custom event

8. **React Error Handler** (`client/src/App.tsx:onApiError`)
   - Listens for API_ERROR_EVENT
   - Shows error toast at bottom-right (deduplicates within 2s)

9. **Real-Time Notification** (if approved automatically)
   - Handler calls `_notificationsHub.Clients.User(...).SendAsync("notificationsUpdated")`
   - SignalR connected clients receive `notificationsUpdated` event
   - App.tsx listener calls `queryClient.invalidateQueries()`
   - React Query refetches affected queries (annualLeaves, leaveStatusHistories, etc.)
   - UI re-renders with fresh data

### Timesheet Entry CRUD Flow (Exception Case)

**Note:** Most CRUD uses MediatR pattern above. Timesheet entries are an exception.

1. **React** → `Axios POST /api/timesheets/{id}/entries`
2. **Controller** (`API/Controllers/TimesheetEntriesController.cs`)
   - Does NOT dispatch to MediatR
   - Injects `AppDbContext` directly
   - Validates write access via `TimesheetAccess.AuthorizeWriteAsync()`
   - Creates TimesheetEntry entity directly
   - Calls `context.SaveChangesAsync()`
   - Recalculates `Timesheet.TotalHours` and saves again
   - Returns entity or error
   - No SignalR notification here

### SignalR Audience Resolution Flow

When a leave is created/updated:

1. **Handler completes, signals via controller**
   - `NotifyForLeaveAsync(leaveId)` in `AnnualLeavesController`
   - Queries AppDbContext to load `(EmployeeId, DepartmentId)` from the leave

2. **Controller resolves SignalR audience**
   ```
   - Notify: employee (User(employeeId))
   - Notify: all admins (Group("role:Admin"))
   - Notify: managers of that department (Group("dept-mgr:{departmentId}"))
   ```

3. **SignalR broadcasts**
   - Sends `notificationsUpdated` to connected clients in those groups
   - Clients invalidate React Query caches

**Client SignalR Connection** (`client/src/App.tsx:AppInner`)
- Establishes HubConnectionBuilder to `/hubs/notifications`
- Receives `notificationsUpdated` event
- Invalidates multiple query keys: leaveStatusHistories, annualLeaves, timesheets, etc.
- React Query re-fetches affected data

**State Management:**
- **Auth state** (MobX `authStore`): User object, roles, hydration on app load
- **UI state** (MobX `uiStore`): Current page/route, navigation function, sidebar open/close
- **Server state** (React Query): Leave lists, timesheet entries, department data, cached and refetched on SignalR events
- **Global errors** (Axios interceptor + custom event): API errors dispatched as events, captured in App.tsx and shown as toast

## Key Abstractions

**Result<T> Pattern:**
- Purpose: Return success or failure without throwing for business errors
- Examples: `Application/Core/Result.cs`, all handlers return Result<T> or Result<PagedResult<T>>
- Pattern: Handlers return Result.Success(value), Result.Failure(error), Result.Conflict(error), Result.Forbidden(error), Result.Invalid(error)
- Controllers map via `HandleResult<T>()` to HTTP status codes (200/404/409/403/400)

**MediatR CQRS:**
- Purpose: Centralize request handling and cross-cutting concerns (validation, logging)
- Examples: `Application/AnnualLeaves/Commands/CreateAnnualLeave.cs`, `Application/AnnualLeaves/Queries/GetAnnualLeaveList.cs`
- Pattern: Command implements IRequest<Result<T>>, Query implements IRequest<T>, both have nested Handler class

**ManagerAccessScope:**
- Purpose: Determine which departments a manager can see/act on
- Used in: Query handlers (GetAnnualLeaveList, GetTimesheetList) and authorization checks
- Pattern: Resolves from manager's EmployeeProfile.DepartmentId and direct reports

**PagedResult<T>:**
- Purpose: Return paginated lists with metadata (total count, page, size)
- Pattern: Handlers return PagedResult; controller maps to plain array + X-Total-Count, X-Page, X-Page-Size headers

## Entry Points

**Backend:**
- **Program.cs** (`API/Program.cs`): Service setup (EF, MediatR, SignalR, auth, health checks), middleware pipeline configuration
- **BaseApiController** (`API/Controllers/BaseApiController.cs`): Base for all controllers; Mediator property, HandleResult method
- **NotificationsHub** (`API/Hubs/NotificationsHub.cs`): SignalR hub; on-connect group registration by role/department

**Frontend:**
- **App.tsx** (`client/src/App.tsx`): React Router root; BrowserRouter, Routes, auth gate, AppShell layout, SignalR connection, global error listener
- **Index entry** (`client/src/main.tsx`): ReactDOM.createRoot, StoreProvider setup, App render

## Architectural Constraints

- **Threading:** Single-threaded event loop (Node/browser frontend); ASP.NET Core background service for scheduled reminders (ReminderBackgroundService, ticks every minute)
- **Global state (Backend):** AppDbContext is scoped per request; no module-level singletons except for configuration and Serilog logger
- **Global state (Frontend):** MobX stores (authStore, uiStore) are singletons; React Query client is global; Axios instance is global
- **Circular imports:** None detected; layered dependencies are acyclic
- **Database transactions:** Used only in CreateAnnualLeave when auto-approval syncs balance; otherwise SaveChangesAsync commits implicitly
- **Async/Await:** Handlers are fully async; controller actions are async and await MediatR dispatch
- **Rate limiting:** Global fixed-window (100 req/min per IP); sliding-window stricter policy (5 attempts/min) on auth endpoints
- **CORS:** Configured for specific origins in prod; localhost allowed in dev (Vite dev server)
- **Authentication:** ASP.NET Core Identity with cookie-based session; no bearer tokens exposed (MapIdentityApi deliberately unmapped)
- **Authorization:** Policy-based (AnnualLeaveRead, AnnualLeaveCreate, etc.); scope by role + department for managers
- **SignalR reconnection:** Automatic with backoff; clients set `withCredentials: true` for cookie auth

## Anti-Patterns

### Raw AppDbContext in Controllers

**What happens:** TimesheetEntriesController directly queries and mutates the database instead of using MediatR
**Why it's wrong:** Breaks the pattern where business logic lives in handlers; makes authorization checks scattered between controller and handler (if one existed)
**Do this instead:** Move entry CRUD to Commands/Queries in Application/Timesheets/ (e.g., CreateTimesheetEntry.Command, UpdateTimesheetEntry.Command), then dispatch from controller to handler. See `Application/AnnualLeaves/Commands/CreateAnnualLeave.cs` as the correct pattern.

### Direct Email Sending in Handlers

**What happens:** CreateAnnualLeave handler calls `emailService.SendEmailAsync()` after database commit
**Why it's wrong:** Email failures don't trigger transaction rollback; if email is critical, a failure leaves the leave created but manager not notified
**Do this instead:** Either queue emails (background job queue) after SaveChangesAsync, or move email to a separate domain service called post-transaction with error handling, or use outbox pattern to ensure mail is sent. Current approach is pragmatic for non-critical notifications but assumes email fire-and-forget is acceptable.

### No Soft Deletes

**What happens:** Departments and Projects are hard-deleted; deletes are prevented by foreign-key constraints instead of filtering deleted records
**Why it's wrong:** Historical data loss; audits cannot show who deleted what; cascade deletes can accidentally wipe related records
**Do this instead:** Add `IsDeleted` bool to Department and Project, then query with `.Where(d => !d.IsDeleted)` in handlers. Mark as concern: see CONCERNS.md.

## Error Handling

**Strategy:** MediatR ValidationBehavior catches validation errors early; handlers return Result<T> for business errors; global exception middleware catches unhandled exceptions

**Patterns:**
- **Validation errors**: FluentValidation throws ValidationException in pipeline behavior; caught by GlobalExceptionMiddleware → 400 Bad Request with field errors
- **Business errors**: Handler returns Result.Failure(error) or Result.Conflict(error); controller maps to appropriate status code
- **Authorization errors**: Handler returns Result.Forbidden(error); controller maps to 403
- **Not found errors**: Handler returns Result.Failure(error); controller maps to 404 (default)
- **Unhandled exceptions**: GlobalExceptionMiddleware catches, logs with Serilog, returns 500 with correlation ID
- **Database exceptions**: EF Core exceptions bubble to middleware (e.g., unique constraint violation); logged as error with context

## Cross-Cutting Concerns

**Logging:** 
- Serilog configured in `API/Extensions/LoggingExtensions.cs`
- Console output + newline-delimited JSON to `Logs/worktrack-{date}.jsonl` (14-day retention)
- Every request gets a correlation ID (middleware), logged in every line, returned as X-Correlation-ID header
- Levels configurable via appsettings.json Serilog section

**Validation:** 
- FluentValidation validators in same folder as command/query (e.g., `CreateAnnualLeave.Validator`)
- MediatR ValidationBehavior auto-runs validators before handler
- Per-field errors returned as 400 Bad Request with field-level error messages

**Authentication:** 
- ASP.NET Core Identity with cookie session (sign-in creates HttpOnly secure cookie)
- Cookie policy: SameSite=Lax, Secure=Always (outside dev)
- No bearer tokens; identity cookie is the sole auth mechanism

**Authorization:**
- Policy-based (defined in Program.cs): "AnnualLeaveRead", "AnnualLeaveCreate", "AnnualLeaveUpdate", etc.
- Checked via `[Authorize(Policy = "...")]` on actions
- Manager scope resolved dynamically via ManagerAccessScopeResolver in queries

---

*Architecture analysis: 2026-08-21*
