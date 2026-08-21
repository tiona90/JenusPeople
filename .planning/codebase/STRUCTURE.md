# Codebase Structure

**Analysis Date:** 2026-08-21

## Directory Layout

```
WorkTrack/
├── API/                           # ASP.NET Core Web API (entry point)
│   ├── Program.cs                 # Service setup, middleware pipeline, auth policy config
│   ├── Controllers/               # HTTP endpoint handlers (thin routers to MediatR)
│   │   ├── BaseApiController.cs   # Base class with Mediator, HandleResult<T>(), Paged<T>()
│   │   ├── AnnualLeavesController.cs      # Leave CRUD (dispatches to MediatR, manages SignalR audiences)
│   │   ├── TimesheetsController.cs        # Timesheet CRUD (dispatches to MediatR)
│   │   ├── TimesheetEntriesController.cs  # Entry CRUD (EXCEPTION: direct AppDbContext, no MediatR)
│   │   ├── AdminUsersController.cs        # User management
│   │   ├── DepartmentsController.cs       # Department CRUD
│   │   ├── ProjectsController.cs          # Project CRUD
│   │   ├── AccountController.cs           # Login, logout, password reset, email verification
│   │   └── ...
│   ├── Hubs/                      # SignalR hubs
│   │   └── NotificationsHub.cs    # Real-time notifications (group management by role/department)
│   ├── Middleware/                # HTTP pipeline middleware
│   │   ├── CorrelationIdMiddleware.cs     # Adds correlation ID to all requests/responses
│   │   ├── GlobalExceptionMiddleware.cs   # Catches unhandled exceptions, logs, returns error response
│   │   └── SecurityHeadersMiddleware.cs   # Security headers (HSTS, CSP, X-Frame-Options, etc.)
│   ├── Extensions/                # Service setup and helper extensions
│   │   ├── LoggingExtensions.cs   # Serilog configuration
│   │   ├── HealthCheckExtensions.cs       # Health check endpoints (/health, /health/ready)
│   │   ├── SwaggerExtensions.cs   # Swagger/OpenAPI setup
│   │   └── ...
│   ├── BackgroundServices/        # Hosted services
│   │   └── ReminderBackgroundService.cs   # Scheduled leave reminders (ticks every minute)
│   ├── HealthChecks/              # Health check implementations
│   ├── Services/                  # API-layer services (e.g., AccountEmailSender)
│   ├── Models/                    # API-specific request/response DTOs
│   │   └── ApiErrorResponse.cs    # Standard error response shape
│   ├── Security/                  # Security policies (LockoutPolicy, SeedPolicy)
│   ├── DTOs/                      # API request/response objects
│   └── wwwroot/                   # Published React SPA + static assets
│
├── Application/                   # Business logic layer (MediatR CQRS)
│   ├── Core/                      # Shared abstractions
│   │   ├── Result.cs              # Result<T> success/failure pattern
│   │   ├── ValidationBehavior.cs  # MediatR pipeline behavior (runs validators)
│   │   ├── PagedResult.cs         # Paged result wrapper for list queries
│   │   └── MappingProfiles.cs     # AutoMapper profiles
│   ├── [Feature]/                 # Each feature (e.g., AnnualLeaves, Timesheets, etc.)
│   │   ├── Commands/              # Write operations
│   │   │   ├── CreateAnnualLeave.cs       # (nested: Command, Handler, Validator)
│   │   │   ├── EditAnnualLeave.cs
│   │   │   ├── DeleteAnnualLeave.cs
│   │   │   ├── UpdateLeaveStatus.cs       # Approve/reject
│   │   │   └── ...
│   │   ├── Queries/               # Read operations
│   │   │   ├── GetAnnualLeaveList.cs      # List with role-scoped filtering
│   │   │   ├── GetAnnualLeaveDetails.cs   # Single entity
│   │   │   └── ...
│   │   ├── DTOs/                  # Feature request/response objects
│   │   │   ├── AnnualLeaveDto.cs
│   │   │   ├── CreateAnnualLeaveRequest.cs
│   │   │   └── ...
│   │   ├── Validators/            # FluentValidation validators
│   │   ├── Support/               # Helper classes (e.g., ManagerAccessScopeResolver)
│   │   └── ...
│   ├── Accounts/                  # Authentication/account features
│   ├── AdminUsers/                # Admin user management
│   ├── AnnualLeaves/              # Annual leave requests
│   ├── Attendance/                # Check-in/check-out
│   ├── Departments/               # Department management
│   ├── EmployeeProfiles/          # Employee profiles
│   ├── Holidays/                  # Public holidays (via Nager API)
│   ├── LeaveStatusHistories/      # Audit trail for status changes
│   ├── LeaveTypes/                # Leave type setup
│   ├── Projects/                  # Project management
│   ├── ProjectActivityTypes/      # Activity type setup
│   ├── Reminders/                 # Leave/timesheet reminders
│   ├── Settings/                  # Application settings
│   ├── Timesheets/                # Timesheet submission/approval
│   ├── TimesheetStatusHistories/  # Audit trail for status changes
│   ├── UserDepartments/           # User-department relationships
│   └── Application.csproj         # References: Domain, Persistence
│
├── Domain/                        # Core entities (no dependencies except Identity)
│   ├── User.cs                    # Extends IdentityUser
│   ├── Role.cs                    # Extends IdentityRole
│   ├── AnnualLeave.cs             # Leave request entity
│   ├── Timesheet.cs               # Timesheet entity
│   ├── TimesheetEntry.cs          # Hours logged against a project
│   ├── Project.cs                 # Project entity
│   ├── Department.cs              # Department entity
│   ├── EmployeeProfile.cs         # User profile (links to Department)
│   ├── AttendanceEvent.cs         # Check-in/out events
│   ├── LeaveType.cs               # Leave type definition
│   ├── LeaveStatusHistory.cs      # Status change audit trail
│   ├── PublicHoliday.cs           # Public holidays
│   ├── ProjectActivityType.cs     # Activity type
│   ├── AuditLog.cs                # General audit log
│   ├── AppSettings.cs             # Key-value settings
│   ├── AppRoles.cs                # Role constants (Admin, Manager, Employee)
│   ├── Interfaces/                # Service contracts
│   │   ├── IEmailService.cs       # Email sending (implemented by Infrastructure)
│   │   ├── IFileUploadService.cs  # File upload (implemented by Infrastructure)
│   │   ├── IAccountEmailSender.cs # Account lifecycle emails
│   │   ├── IChatNotificationService.cs    # Chat notifications (contract only)
│   │   └── ...
│   ├── Enums/                     # Enums (AnnualLeaveStatus, TimesheetStatus, etc.)
│   └── Domain.csproj              # No project references
│
├── Persistence/                   # Data access layer (EF Core)
│   ├── AppDbContext.cs            # EF Core DbContext
│   │                              # Extends IdentityDbContext<User, Role, ...>
│   │                              # All DbSet<T> declarations, OnModelCreating config
│   ├── Migrations/                # EF Core migrations (auto-generated)
│   │   ├── 20240101000000_InitialCreate.cs
│   │   ├── 20240115000000_AddAuditLog.cs
│   │   └── ...
│   ├── Interceptors/              # EF Core interceptors
│   │   └── AuditingSaveChangesInterceptor.cs    # Logs entity changes to AuditLog
│   ├── Persistence.csproj         # References: Domain
│   └── [Configurations/]          # (Optional) Entity type configs if moved out of OnModelCreating
│
├── Infrastructure/                # External services
│   ├── Services/                  # Service implementations
│   │   ├── Email/                 # Email providers
│   │   │   ├── IEmailProvider.cs  # Interface for email backends
│   │   │   ├── BrevoEmailProvider.cs     # HTTP API to Brevo
│   │   │   ├── SmtpEmailProvider.cs      # MailKit for SMTP
│   │   │   └── EmailService.cs    # Decorator/router selecting provider
│   │   ├── File/                  # File upload service
│   │   │   └── CloudinaryFileUploadService.cs   # Uploads to Cloudinary
│   │   ├── Holidays/              # Public holidays client
│   │   │   └── NagerHolidayClient.cs    # HTTP client to Nager API
│   │   └── ...
│   ├── DependencyInjection.cs     # Service registration
│   ├── Infrastructure.csproj      # References: Domain
│   └── [Settings/]                # Configuration option classes
│
├── Tests/                         # Test project (xUnit, Moq, EF in-memory)
│   ├── WorkTrack.Tests.csproj
│   ├── TestSupport.cs             # TestDb, TransactionalTestDb, WebApplicationFactory setup
│   ├── [Feature]/                 # Tests organized by feature
│   │   ├── AnnualLeaves/
│   │   ├── Timesheets/
│   │   └── ...
│   └── ...
│
├── client/                        # React 19 frontend (Vite)
│   ├── src/
│   │   ├── main.tsx               # ReactDOM.createRoot, StoreProvider
│   │   ├── App.tsx                # React Router root (BrowserRouter, Routes)
│   │   │                          # Auth gate, AppShell layout, SignalR connection
│   │   │                          # Global error listener
│   │   ├── components/            # Page and UI components
│   │   │   ├── admin/             # Admin pages (users, settings, data maint)
│   │   │   ├── annual-leave/      # Leave pages
│   │   │   │   ├── ApplyLeavePage.tsx    # Form to request leave
│   │   │   │   ├── MyLeavePage.tsx       # Employee's own leave history
│   │   │   │   ├── TeamLeavePage.tsx     # Manager's team leave view
│   │   │   │   ├── AllLeaveAdminPage.tsx # Admin all-company view
│   │   │   │   ├── AnnualLeaveForm.tsx   # Shared form component
│   │   │   │   ├── AnnualLeaveCard.tsx   # Leave item card
│   │   │   │   └── ...
│   │   │   ├── attendance/        # Check-in/out and attendance pages
│   │   │   ├── timesheet/         # Timesheet pages
│   │   │   ├── auth/              # Login, password reset, email verification
│   │   │   ├── layout/            # Sidebar, Topbar, Page layouts
│   │   │   └── ui/                # Reusable UI components (buttons, modals, etc.)
│   │   ├── lib/                   # Utilities and state management
│   │   │   ├── api/               # HTTP API client modules
│   │   │   │   ├── client.ts      # Axios instance setup (baseURL, interceptors)
│   │   │   │   ├── account.ts     # Auth API (login, logout, profile)
│   │   │   │   ├── annual-leaves.ts       # Leave API client
│   │   │   │   ├── timesheets.ts  # Timesheet API client
│   │   │   │   ├── projects.ts    # Project API client
│   │   │   │   ├── departments.ts # Department API client
│   │   │   │   ├── error-events.ts        # Custom error event dispatcher
│   │   │   │   └── ...
│   │   │   ├── mobx/              # MobX state (auth, UI navigation)
│   │   │   │   ├── rootStore.ts   # Root store combining all stores
│   │   │   │   ├── authStore.ts   # Auth state (user, login, logout, hydrate)
│   │   │   │   ├── uiStore.ts     # UI state (sidebar open, current section, navigate func)
│   │   │   │   └── StoreProvider.tsx     # Context provider
│   │   │   ├── react-query/       # React Query setup (hooks, invalidation)
│   │   │   │   └── [hooks]
│   │   │   ├── hooks/             # Custom React hooks
│   │   │   ├── validation/        # Zod schemas for form validation
│   │   │   ├── types/             # TypeScript type definitions
│   │   │   └── ...
│   │   └── test/                  # Test utilities and helpers
│   ├── vite.config.ts             # Vite build config
│   ├── tsconfig.json              # TypeScript config
│   ├── package.json               # Dependencies (React 19, react-router-dom, MobX, React Query, etc.)
│   └── eslint.config.mjs          # ESLint rules
│
├── Annualleave.sln                # Visual Studio solution file
├── CLAUDE.md                       # Project instructions (guidelines, patterns, config notes)
├── DEPLOY.md                       # Deployment guide (IIS, environment setup, etc.)
├── README.md                       # Project overview
├── global.json                     # Global .NET SDK version pinning
├── appsettings.json               # Default app settings (includes config for Email, CORS, Seed, etc.)
├── appsettings.Development.json   # Development-specific settings (if present, overrides defaults)
├── .env.example                   # Example environment variables
├── docker-compose.yml             # Docker Compose for local development
├── Dockerfile                      # (if present) Container image
└── ...
```

## Directory Purposes

**API/**
- Purpose: HTTP entry point; controllers route requests to MediatR handlers, middleware handles cross-cutting concerns (auth, logging, errors), SignalR hub manages real-time connections
- Contains: Controllers, middleware, health checks, hubs, background services, API-specific DTOs and models
- Key files: `Program.cs` (setup), `Controllers/BaseApiController.cs` (base handler), `Hubs/NotificationsHub.cs` (real-time), `Middleware/GlobalExceptionMiddleware.cs` (error handling)

**Application/**
- Purpose: Business logic; houses all use-case handlers (commands and queries), validators, and feature-specific DTOs
- Contains: MediatR handlers organized by feature (AnnualLeaves, Timesheets, etc.), each with Commands/, Queries/, DTOs/, Validators/ subdirectories
- Key files: `Core/Result.cs` (return type), `Core/ValidationBehavior.cs` (auto-validation), feature handlers like `AnnualLeaves/Commands/CreateAnnualLeave.cs`

**Domain/**
- Purpose: Core business entities with no external dependencies; service contracts (interfaces)
- Contains: Entity classes (User, AnnualLeave, Timesheet, Project, Department), enums (AnnualLeaveStatus, TimesheetStatus), service interfaces (IEmailService, IFileUploadService)
- Key files: Entity definitions, `AppRoles.cs` (role constants), `Interfaces/` directory with service contracts

**Persistence/**
- Purpose: EF Core configuration and database migrations; AppDbContext is the single entry point for all database operations
- Contains: `AppDbContext.cs` (DbSet declarations and entity configuration), Migrations/ (auto-generated migration history), interceptors (AuditingSaveChangesInterceptor)
- Key files: `AppDbContext.cs`, `Migrations/` (sorted by timestamp)

**Infrastructure/**
- Purpose: External service implementations; email providers (Brevo, SMTP), file upload (Cloudinary), public holidays API client (Nager)
- Contains: Service implementations, dependency injection registration
- Key files: `DependencyInjection.cs` (service registration), `Services/Email/` (pluggable providers), `Services/File/CloudinaryFileUploadService.cs`

**Tests/**
- Purpose: Unit and integration tests; test database setup (in-memory EF, SQLite)
- Contains: xUnit test classes, test fixtures, WebApplicationFactory for integration testing
- Key files: `TestSupport.cs` (TestDb, TransactionalTestDb, WebApplicationFactory), test classes per feature

**client/src/**
- Purpose: React SPA frontend
- **components/**: Page-level and UI components organized by feature (admin, annual-leave, timesheet, auth)
  - **Feature folders** (annual-leave/, timesheet/): Pages, forms, and components specific to that feature
  - **layout/**: Sidebar, Topbar, shared page layouts
  - **ui/**: Reusable UI elements (buttons, modals, tables, etc.)
- **lib/api/**: Axios HTTP client modules, one per API domain (account, annual-leaves, projects, etc.)
  - Each module wraps the Axios instance with typed request/response
  - `client.ts`: Axios instance with interceptor for error event dispatch
- **lib/mobx/**: MobX state (authStore for session/user, uiStore for UI navigation)
  - `StoreProvider.tsx`: React context to inject stores
  - `authStore.ts`: User auth, login, logout, hydration
  - `uiStore.ts`: Current route/section, sidebar toggle, navigate callback
- **lib/react-query/**: React Query hooks and setup
- **lib/validation/**: Zod schemas for form validation (applied client-side and sent to server)
- **App.tsx**: React Router root (BrowserRouter, Routes, auth gate, layout shell, SignalR, global error handling)
- **main.tsx**: App bootstrap (ReactDOM, StoreProvider)

**Logs/**
- Purpose: Runtime logs (Serilog newline-delimited JSON)
- Generated: Yes (created at runtime)
- Committed: No (gitignore'd)

**publish/**
- Purpose: Build output directory
- Generated: Yes
- Committed: No (gitignore'd)

## Key File Locations

**Entry Points:**
- **Backend**: `API/Program.cs` — WebApplicationBuilder setup, service registration, middleware pipeline, database init
- **Frontend**: `client/src/main.tsx` — React root, `client/src/App.tsx` — Router and layout root
- **API base URL**: `client/src/lib/api/client.ts` (default: `http://localhost:5000/api`)
- **Database connection**: `API/Program.cs` line ~225 (SQL Server connection string from appsettings)

**Configuration:**
- `appsettings.json` — Default settings (logging, CORS, email provider, seed data, etc.)
- `appsettings.Development.json` — (if present) Development overrides
- `.env` file — (never committed, created manually) Environment variables for secrets
- `client/package.json` — Frontend dependencies, build scripts
- `CLAUDE.md` — Project guidelines, patterns, configuration notes

**Core Logic:**
- **Backend business logic**: `Application/*/Commands/` and `Application/*/Queries/` (MediatR handlers)
- **Database schema**: `Persistence/AppDbContext.cs` (entity configs) + `Persistence/Migrations/` (schema history)
- **Frontend pages**: `client/src/components/[feature]/` (e.g., `components/annual-leave/ApplyLeavePage.tsx`)
- **Frontend state**: `client/src/lib/mobx/` (auth, UI navigation), `client/src/lib/react-query/` (server state)

**Testing:**
- **Backend tests**: `Tests/` (xUnit, organized by feature, TestDb setup in TestSupport.cs)
- **Frontend tests**: `client/src/**/*.test.tsx` (co-located with components, vitest)
- **Test database setup**: `Tests/TestSupport.cs` (TestDb in-memory, TransactionalTestDb SQLite)

## Naming Conventions

**Files:**
- **Command handlers**: `[VerbNoun].cs` (e.g., `CreateAnnualLeave.cs`, `UpdateLeaveStatus.cs`)
- **Query handlers**: `Get[Noun][OptionalDetails].cs` (e.g., `GetAnnualLeaveList.cs`, `GetAnnualLeaveDetails.cs`)
- **Controllers**: `[PluralEntity]Controller.cs` (e.g., `AnnualLeavesController.cs`, `TimesheetsController.cs`)
- **Components**: `[PascalCase].tsx` (e.g., `ApplyLeavePage.tsx`, `AnnualLeaveForm.tsx`)
- **Hooks**: `use[Feature].ts` (e.g., `useAnnualLeaves.ts`)
- **DTOs**: `[Entity]Dto.cs` or `[Verb][Entity]Request.cs` / `[Entity]Response.cs`
- **Tests**: `[ClassBeingTested].Tests.cs` or `[Filename].test.tsx`

**Directories:**
- **Feature modules**: Singular noun (Domain entities) or plural noun (Application/API) — e.g., `AnnualLeaves/`, `Timesheets/`, `Departments/`
- **Organizational layers**: Lowercase + feature (e.g., `Application/Core/`, `API/Controllers/`, `client/src/lib/api/`)
- **By domain**: `API/Controllers/`, `Application/AnnualLeaves/`, organized by feature not by type (not `/Commands` at root, but `/AnnualLeaves/Commands`)

**Naming Patterns (Code):**
- **Classes**: PascalCase (User, AnnualLeave, CreateAnnualLeave)
- **Methods**: PascalCase (Handle, GetAsync, SendEmailAsync)
- **Properties**: PascalCase (UserId, StartDate, TotalHours)
- **Local variables/parameters**: camelCase (userId, startDate, handler)
- **Constants**: UPPER_SNAKE_CASE (AppRoles.Admin, CorrelationIdHeader = "X-Correlation-ID")
- **TypeScript**: camelCase for variables (authStore, useAnnualLeaves), PascalCase for components and types (ApplyLeavePage, AnnualLeaveDto)

## Where to Add New Code

**New Feature (e.g., Leave Policies):**
- **Backend**:
  - Create `Application/LeavePolicies/` directory
  - Add `Commands/Create|Update|DeleteLeavePolicy.cs` files (each with nested Command, Handler, Validator)
  - Add `Queries/GetLeavePolicies.cs`, `GetLeavePolicyDetails.cs` files
  - Add `DTOs/LeavePolicyDto.cs`, `CreateLeavePolicyRequest.cs`, etc.
  - Create `API/Controllers/LeavePoliciesController.cs` inheriting BaseApiController
  - Add entity `Domain/LeavePolicy.cs`
  - Add `Persistence/Migrations/Add[Feature]` and configure in `AppDbContext.OnModelCreating`
  - Add `Infrastructure/Services/LeavePolicy[Service].cs` if external service needed
- **Frontend**:
  - Create `client/src/components/leave-policies/` directory
  - Add pages: `LeavePoliciesPage.tsx`, `CreateLeavePolicyPage.tsx`, `EditLeavePolicyPage.tsx`
  - Add components: `LeavePoliciesTable.tsx`, `LeavePolicyForm.tsx`
  - Add API module: `client/src/lib/api/leave-policies.ts`
  - Add React Query hooks: `client/src/lib/react-query/hooks-leave-policies.ts`
  - Add Zod schema: `client/src/lib/validation/leavePolicy.ts`
  - Add routes in `client/src/App.tsx` under the appropriate layout

**New Endpoint (e.g., GET /api/annual-leaves/summary):**
- Create query: `Application/AnnualLeaves/Queries/GetAnnualLeaveSummary.cs`
- Add action to controller: `AnnualLeavesController.GetSummary()` → dispatch and HandleResult
- Add API module method: `client/src/lib/api/annual-leaves.ts` → export function call

**New Component (e.g., Reusable Modal):**
- Add to `client/src/components/ui/[ComponentName].tsx`
- Export from `client/src/components/ui/index.ts` (barrel file) if used widely
- Example pattern: see `Sidebar.tsx`, `Topbar.tsx` in `client/src/components/layout/`

**Utilities/Helpers:**
- **Backend**: `Application/Core/` (shared) or feature-specific `Support/` subdirectory
  - Example: `Application/AnnualLeaves/Support/ManagerAccessScopeResolver.cs`
- **Frontend**: `client/src/lib/hooks/` (custom hooks), `client/src/lib/types/` (shared types)

**Tests:**
- **Backend**: `Tests/[Feature]/[ClassName].Tests.cs`
  - Use `WebApplicationFactory<Program>` for integration tests
  - Inject `TestDb` for fast unit tests or `TransactionalTestDb` for constraint validation
  - Reference: `Tests/TestSupport.cs` for setup
- **Frontend**: `client/src/components/[feature]/[FileName].test.tsx` (co-located)
  - Use vitest + @testing-library/react
  - Reference: `client/src/components/admin/AdminUsersPanel.test.tsx` for pattern

## Special Directories

**Logs/:**
- Purpose: Runtime application logs
- Generated: Yes (created by Serilog at runtime)
- Committed: No (`*.jsonl` and `*.log` in .gitignore)
- Pattern: `worktrack-{YYYY-MM-DD}.jsonl` (14-day retention policy)

**publish/:**
- Purpose: Build output for deployment
- Generated: Yes (dotnet publish output)
- Committed: No (in .gitignore)

**Migrations/ (in Persistence/):**
- Purpose: EF Core migration history
- Generated: Yes (by `dotnet ef migrations add`)
- Committed: Yes (part of source control for reproducibility)

**wwwroot/ (in API/):**
- Purpose: Static assets served by ASP.NET (React SPA publish output)
- Generated: Yes (by `npm run build` into this directory)
- Committed: No (spa build output in .gitignore, only server runs published app)

---

*Structure analysis: 2026-08-21*
