# External Integrations

**Analysis Date:** 2026-08-21

## APIs & External Services

**Email Delivery (Pluggable):**
- Brevo (HTTP API) - Transactional emails (account lifecycle: welcome, password reset, email change)
  - SDK/Client: CloudinaryDotNet (no dedicated SDK; uses HttpClient in `Infrastructure/Services/Email/Providers/BrevoEmailProvider.cs`)
  - Auth: API key in `Brevo:ApiKey` env var (`API/appsettings.json`)
  - Selection: Enabled by setting `Email:Provider` to `"Brevo"` in `API/appsettings.json`
  - Limitation: Requires allowlisted IPv4 (configured hosts only); IPv6 privacy addresses will fail. Workaround: Dual-stack machine forces IPv4 via `SocketsHttpHandler.ConnectCallback` in `Infrastructure/DependencyInjection.cs`
  
- SMTP (Gmail/Brevo relay) - Alternative email provider via MailKit
  - SDK/Client: MailKit 4.17.0 (SMTP client)
  - Auth: Credentials in `MailSettings:Mail`, `MailSettings:Password` (`API/appsettings.json`)
  - Config: `MailSettings:Host`, `MailSettings:Port` (Gmail: `smtp.gmail.com:587`)
  - Selection: Enabled by setting `Email:Provider` to `"Smtp"` (default in current config is `"Brevo"`)

**File Storage:**
- Cloudinary - Profile image uploads and evidence file uploads
  - SDK/Client: CloudinaryDotNet 1.28.0
  - Auth: `Cloudinary:CloudName`, `Cloudinary:ApiKey`, `Cloudinary:ApiSecret` (`API/appsettings.json`)
  - Service: `IFileUploadService` implemented by `CloudinaryFileUploadService` (`Infrastructure/Services/CloudinaryFileUploadService.cs`)
  - Dependency injection: Registered in `Infrastructure/DependencyInjection.cs`

**Public Holidays API:**
- Nager.Date (date.nager.at/api/v3) - Public holiday data for leave balance calculations
  - SDK/Client: `NagerHolidayClient` (custom HttpClient wrapper, `API/Services/NagerHolidayClient.cs`)
  - Base URL: `https://date.nager.at/api/v3/`
  - Resilience: Standard handler (retry with exponential backoff, circuit breaker at 10% failure rate, per-attempt 8s timeout, total 40s timeout)
  - Configuration: Registered in `API/Program.cs` via `AddHttpClient<NagerHolidayClient>()`
  - Public API: No authentication required

**Chat Notifications:**
- Slack - Incoming webhooks for admin/manager notifications
  - SDK/Client: HttpClient (custom, `Infrastructure/Services/SlackNotificationService.cs`)
  - Auth: Webhook URL in `Slack:WebhookUrl` (`API/appsettings.json`)
  - Service: `IChatNotificationService` implemented by `SlackNotificationService`
  - Behavior: Swallows exceptions (short 5s timeout); failures are logged but do not break user requests
  - Configuration: Registered in `Infrastructure/DependencyInjection.cs`
  - Disabled when webhook URL is empty

## Data Storage

**Primary Database:**
- SQL Server - All application data (users, leave requests, timesheets, projects, departments)
  - Connection: `DefaultConnection` env var in `API/appsettings.json`
  - Development: local SQL Server instance, connection string at `ConnectionStrings:DefaultConnection` in `API/appsettings.json` (server, database and login redacted here — read the file if you need them)
  - Production: Remote SQL Server at `185.190.143.89` (git-ignored credentials in `API/appsettings.Production.json`)
  - Client: Entity Framework Core 9.0.0 with Microsoft.EntityFrameworkCore.SqlServer provider
  - Migrations: Auto-applied on startup via `context.Database.MigrateAsync()` in `API/Program.cs`
  - No Repository layer: Handlers in `Application/*/Queries/` and `Application/*/Commands/` inject `AppDbContext` directly

**Test Databases:**
- In-Memory (EF Core): Fast, no constraints, used for most tests (`Microsoft.EntityFrameworkCore.InMemory`)
- SQLite in-memory: Enforces constraints and transactions, used only for rollback/transaction tests (`Microsoft.EntityFrameworkCore.Sqlite`)

**Caching:**
- None currently (all database queries, no Redis)
- React Query handles client-side cache invalidation via `queryClient.invalidateQueries()`

## Authentication & Identity

**Auth Provider:**
- ASP.NET Core Identity - Built-in, stored in SQL Server
  - User login: Identity cookie + form-based auth (no bearer tokens)
  - Session management: Identity cookie (secure, httpOnly, SameSite=Lax)
  - Lockout: Brute-force protection (max 5 failed attempts / 5 minutes)
  - Email confirmation: Required on login (configured in `API/Program.cs`)
  - Roles: Admin, Manager, Employee (custom `Role` model, stored in Identity tables)
  - No external OAuth in current setup

**Authorization:**
- Policy-based via `AddAuthorization` in `API/Program.cs`
- Policies: `AnnualLeaveRead`, `AnnualLeaveCreate`, `AnnualLeaveUpdate`, `AnnualLeaveDelete`, `EmployeeProfileUpdate`
- Manager scope: Queries return only their department's records (enforced in handlers, not DB-level)

**Account Lifecycle:**
- No public self-registration (endpoint intentionally not mapped)
- Accounts created only via `POST /api/AdminUsers` (admin action)
- Emails: Welcome invite, password reset, email verification sent via configured email provider

## Monitoring & Observability

**Error Tracking:**
- None (future roadmap item: consider Sentry)

**Logs:**
- Serilog - Structured JSON logging
  - Output: Console (stdout for IIS ANCM) + newline-delimited JSON to `Logs/worktrack-<date>.jsonl` (14-day retention)
  - Enrichment: Every log line carries `CorrelationId` (also in `X-Correlation-ID` response header and error body `traceId`)
  - Middleware: `CorrelationIdMiddleware` stamps request at pipeline entry; `RequestLoggingMiddleware` logs one line per request
  - Configuration: `LoggingExtensions.cs` (can override levels via `Serilog` section in appsettings)

**Health Probes:**
- GET `/health` - Liveness check (no dependencies tested)
- GET `/health/ready` - Readiness check (tests database connectivity and mail provider; 5-min cache)
- Response: 200 OK (Healthy) or 503 Unavailable (Unhealthy)
- Exempt from rate limiting, anonymous access

**Request Instrumentation:**
- Rate limiting: 100 req/min per client IP (global); 5 login attempts/min (strict sliding window)
- CORS policy: Single-origin same-site in production; localhost allowed in Development

## CI/CD & Deployment

**Hosting:**
- IIS with ANCM (ASP.NET Core Module v2) on Windows Server `185.190.143.89`
- Single IIS site at `https://jpeople-dev.jenusplanet.com`
- React SPA published into `wwwroot/`; API endpoints under `/api` and `/hubs`
- WebSocket Protocol Windows feature required (for SignalR)

**Build Process:**
- PowerShell script: `build-release.ps1` produces `publish/jpeople/` (API + wwwroot + web.config + appsettings)
- Publish includes compiled API, appsettings.*.json (except Production which is git-ignored), and built SPA
- Copy entire `publish/jpeople/` to server (e.g., `C:\sites\jpeople`)

**CI Pipeline:**
- GitHub Actions (`.github/workflows/main.yml`)
- Triggers: Pull requests and push to main branch
- Backend job: Restore → Build (Release) → Test (xUnit)
- Frontend job: npm ci → Lint (ESLint) → Build (TypeScript + Vite)
- Node.js 24, .NET from `global.json` (10.0.100)
- NuGet and npm caching enabled

**Docker Compose (Local Development):**
- `docker-compose.yml` defines `db` (SQL Server 2022), `api` (.NET), `client` (Node.js)
- Requires `.env` file with `SA_PASSWORD` (SQL Server sa password, min 8 chars with mixed case/digit/symbol)
- Volumes: `mssql_data` (database), `client_node_modules` (preserves Alpine-built packages)
- Ports: 1433 (SQL), 5000 (API), 5174 (Client)

## Environment Configuration

**Development:**
- Required env vars: None (all defaults in appsettings.json)
- Secrets location: `.env` file (gitignored) for Docker; appsettings.json for local dev
- Mock/stub services: Brevo API key (real key in appsettings.json, tested against live Brevo); Slack webhook optional (empty = disabled)
- Local database: SQL Server, configured via `ConnectionStrings:DefaultConnection` in `API/appsettings.json` (credentials deliberately not reproduced here)
- Client origin: `http://localhost:5174` (Vite dev server); proxied to `http://127.0.0.1:5000` for API calls

**Production:**
- Secrets location: `API/appsettings.Production.json` (git-ignored, deployed separately)
- Database: remote SQL Server with a SQL login, configured via `ConnectionStrings:DefaultConnection` in `API/appsettings.Production.json` (host and credentials deliberately not reproduced here)
- CORS: `https://jpeople-dev.jenusplanet.com` only (no localhost)
- Email: Brevo API key or SMTP credentials (configured in Production appsettings)
- Seeding: Opt-in flags (`Seed:Enabled`, `Seed:DemoData`, `Seed:AllowInProduction`) in appsettings.Production.json (all default to false)

## Webhooks & Callbacks

**Incoming:**
- SignalR hub at `/hubs/notifications` - WebSocket connection for server → client notifications
  - Trigger: Background service broadcasts `notificationsUpdated` events (e.g., leave approved, timesheet submitted)
  - Client: `App.tsx` listens and calls `queryClient.invalidateQueries()` to refresh caches
  - No verification required (WebSocket upgrade)

**Outgoing:**
- Slack incoming webhook (optional) - Admin/manager notifications
  - Endpoint: URL from `Slack:WebhookUrl` config
  - Trigger: Leave requests, timesheet changes, system alerts
  - Service: `SlackNotificationService.SendMessageAsync()` (swallows failures)
  - Retry: None (exceptions logged, message dropped if Slack is down)

---

*Integration audit: 2026-08-21*
*Update when adding/removing external services*
