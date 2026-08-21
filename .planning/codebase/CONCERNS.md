# Codebase Concerns

**Analysis Date:** 2026-08-21

## Tech Debt

**Timesheet status transitions lack full state-machine validation:**
- Issue: `UpdateTimesheetStatus` command's validator (`Application/Timesheets/Validators/UpdateTimesheetStatusValidator.cs` lines 25-27) only enforces that the target status is Approved or Rejected, but does not validate which source statuses can transition to which targets. The `SubmitTimesheet` command has partial transition logic (Rejected→Resubmitted at line 54), but the approve/reject path has none.
- Files: `Application/Timesheets/Commands/UpdateTimesheetStatus.cs` (line 78 directly assigns status), `Application/Timesheets/Validators/UpdateTimesheetStatusValidator.cs` (lines 25-27)
- Impact: A timesheet could theoretically be moved from Draft directly to Approved, or from Approved to Rejected, without passing through Submitted/Resubmitted states. Concurrency safeguards (RowVersion at `Domain/Timesheet.cs` line 45) catch some race conditions, but not invalid state sequences from the application layer.
- Fix approach: Add state-transition validation to `UpdateTimesheetStatusValidator`: only allow Approved/Rejected from Submitted or Resubmitted states. Document valid state graph in the enum or a separate validator constant.

**N+1 query risk in annual leave filtering:**
- Issue: `Application/AnnualLeaves/Queries/GetAnnualLeaveList.cs` line 51 checks `al.Employee.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin)` to filter out admin leaves from manager views. Employee is included (line 30), but UserRoles and its nested Role are not. When the LINQ query evaluates this predicate in SQL, it may trigger lazy loading of UserRoles for each leave record.
- Files: `Application/AnnualLeaves/Queries/GetAnnualLeaveList.cs` (line 51)
- Impact: On a large dataset, this can cause N+1 queries (one to fetch leaves, one per leave to fetch UserRoles if lazy loading is enabled). Performance degrades with employee count.
- Fix approach: Either (1) include UserRoles and Role at lines 30-32 with `.ThenInclude()`, or (2) refactor the filter to query the database directly without navigating related collections in the WHERE clause (e.g., pre-fetch admin user IDs and filter on EmployeeId instead).

**Secrets remain in committed configuration (working-tree risk):**
- Issue: `API/appsettings.json` and `API/appsettings.Production.json` contain live plaintext credentials — the SQL Server connection-string password, the Gmail app password under `MailSettings`, and `Brevo:ApiKey`. (Values deliberately NOT reproduced in this document; read the files directly if you need them.) The `.gitignore` excludes these files, so they are NOT currently tracked in git. However, they sit unencrypted in the working tree and would leak permanently if `.gitignore` is modified or bypassed with `git add -f`.
- Files: `API/appsettings.json` (lines 9, 27, 33), `API/appsettings.Production.json` (line 3)
- Impact: If `.gitignore` rules are removed or a developer runs `git add -f appsettings.json`, secrets leak into git history permanently. Current state is safe, but architecture is fragile.
- Fix approach: Migrate to environment variables or user secrets (already partially done in `Program.cs` line 35 with `AddUserSecrets`). Ensure appsettings files are never committed; validate in pre-commit hook. Document this in deployment guide.

## Known Issues

**Brevo API connectivity depends on unstable IPv6 workaround:**
- Symptoms: The Brevo email provider intermittently fails with 401 "Unrecognised IP" when outbound IPv6 addresses rotate (dual-stack hosts with privacy-address rotation).
- Files: `Infrastructure/DependencyInjection.cs` lines 37-63
- Workaround: IPv4 is manually pinned via `SocketsHttpHandler.ConnectCallback()` (lines 42-62) to keep the source address stable and allowlisted. This works but couples the application to Brevo's "Authorised IPs" allowlist configuration and assumes the host's IPv4 is stable.
- Root cause: Brevo's "Authorised IPs" feature doesn't distinguish between stable and rotating addresses; .NET prefers IPv6 on dual-stack hosts. The allowlist was manually configured with the host's IPv4 address but not its IPv6 privacy addresses.
- Recommendation: Contact Brevo to request either (1) IPv6 privacy address support in their allowlist, or (2) a token-based auth mechanism that doesn't depend on IP. Until then, the IPv4 pinning must be maintained and its failure mode (degraded email) must be monitored via the `EmailProviderHealthCheck` (see `API/HealthChecks/EmailProviderHealthCheck.cs`).

## Fragile Areas

**Controllers bypass MediatR for signaling and entry CRUD:**
- Files: `API/Controllers/TimesheetEntriesController.cs` (direct CRUD against `AppDbContext`), `API/Controllers/AnnualLeavesController.cs` (lines 60-71, 221-225 query AppDbContext for SignalR audiences)
- Why fragile: `CLAUDE.md` documents these as deliberate deviations ("exceptions to follow away from, not copy"), but they sit outside the CQRS pattern. Consistency changes to leave/timesheet logic must touch both handler files and controller files. Tests for the controllers run against real EF (TestDb or TransactionalTestDb), not mocks, so test complexity is higher.
- Safe modification: When adding features to timesheets/entries, check both `DeleteTimesheet.Command` handler (`Application/Timesheets/Commands/DeleteTimesheet.cs`) and `TimesheetEntriesController` to ensure authorization rules match. For signaling, ensure `NotifyForLeaveAsync` and `NotifyLeaveAudienceAsync` patterns are used consistently across all mutation points.

**Authentication and role-scoped access:**
- Files: Authorization policies in `API/Program.cs` (lines 340-356), manager scoping in `Application/Core/ManagerAccessScope.cs` and `Application/Timesheets/Support/TimesheetAccess.cs`
- Why fragile: Manager department-scoping is implemented per-feature (leaves, timesheets) via different helper classes (`ManagerAccessScopeResolver`, `TimesheetAccess`). If a new query or command is added without invoking the scope resolver, managers may see all records instead of just their departments.
- Safe modification: Always call `ManagerAccessScopeResolver.ResolveAsync()` in queries and `TimesheetAccess.AuthorizeWriteAsync()` in commands that deal with department-scoped data. Copy the pattern from `GetAnnualLeaveList.cs` (lines 41-51) and verify in tests that non-admin managers return only their departments.

## Stale Documentation

**CLAUDE.md roadmap describes completed work:**
- Issue: The "Improvements & Roadmap" section (lines 108-128 of `CLAUDE.md`) lists "Standardized Routing: Replace custom hash-based routing with `react-router`" and "Form Management: Integrate `react-hook-form` and `zod`" as future enhancements.
- Reality: `client/package.json` already includes `react-router-dom` (^7.15.1), `react-hook-form` (^7.76.1), and `zod` (^4.4.3). The frontend codebase uses react-router with proper Routes/Outlet pattern (`client/src/App.tsx` lines 2, 252, 262, 317), not hash-based navigation with `uiStore.currentPage`.
- Impact: Developers following `CLAUDE.md` expect to introduce routing and form libraries, potentially duplicating work or creating parallel implementations.
- Fix approach: Update `CLAUDE.md` section to reflect actual frontend architecture (react-router with URL-based navigation, react-hook-form + zod for validation). Document the actual patterns in use. Remove completed items from the roadmap.

## Outstanding UX Improvements

**Six UX simplification prompts awaiting implementation:**
- Files: `UX-SIMPLIFICATION-PROMPTS.md`, `UX-SIMPLIFICATION-PROMPTS-MANAGER.md`, `UX-SIMPLIFICATION-PROMPTS-ADMIN.md`
- What's not done:
  1. Dashboard dedupe: "Apply for leave" appears 4 ways on employee dashboard; remove redundant Quick Actions tile
  2. Dashboard stats: Weekly hours total is duplicated between hero banner and "This week" card; keep card only
  3. Empty charts: "2026 leave activity" and "2026 hours by month" charts show empty 12-month grids before data exists; add lightweight empty state
  4. Timesheet re-entry: Daily project/activity selection repeats; add "copy to rest of week" action
  5. Attendance vs Timesheet: Numbers differ without explanation (check-in time vs logged work); add clarifying caption
  6. Sensitive leave icons: Bereavement/Sick/Maternity/Paternity use whimsical cartoons; swap to neutral MUI icons
- Impact: Moderate UX friction; users see visual redundancy and empty charts. These are polish improvements, not blockers.

## Test Coverage Gaps

**React/TypeScript frontend has minimal test coverage:**
- What's tested: Auth screens (`client/src/components/auth/`), admin users panel, departments panel, a few page components
- What's not tested: The vast majority of leave/timesheet/attendance UI components, form submissions, real-time SignalR updates, offline behavior
- Files: Only 8 test files under `client/src/` for ~50+ component files
- Risk: Regressions in forms, data display, and role-based visibility are not caught by CI. The offline-aware PWA service-worker and SignalR invalidation logic (`App.tsx` lines 213-237) have no tests.
- Priority: High for user-facing features (apply leave, submit timesheet, approval workflows). Medium for admin and dashboard pages.

**Backend test setup uses both in-memory and SQLite providers:**
- Status: `Tests/WorkTrack.Tests/TestSupport.cs` offers `TestDb` (EF in-memory, fast but no constraints) and `TransactionalTestDb` (SQLite in-memory, real transactions). Tests choose based on whether they need to verify constraint/transaction behavior.
- Coverage quality: Good. Tests exist for leave balance, timesheet status, manager authorization, email delivery, and health checks.
- Gap: No integration tests against a real SQL Server database; all tests use ephemeral in-memory or SQLite. This means schema mismatches, missing indexes, or SQL Server-specific behavior (e.g., computed column serialization) are not caught until production.

## Performance Considerations

**No pagination enforced on unbounded list queries:**
- Pattern observed: Most list queries in `Application/*/Queries/` include optional pagination (e.g., `GetAnnualLeaveList.cs` lines 70-73), but pagination is client-supplied and defaults to returning all records if `Page`/`PageSize` are null.
- Files: `Application/AnnualLeaves/Queries/GetAnnualLeaveList.cs`, `Application/Timesheets/Queries/GetTimesheetList.cs` (likely others)
- Impact: A client that forgets to supply pagination parameters gets a full table fetch. On production (not affected by test data limits), this could timeout or exhaust memory if the table is large.
- Improvement: Enforce a server-side default page size (e.g., 50 records) and cap the maximum page size so clients cannot request 10,000 records in one call. Document in API contracts.

**Database indexes not explicitly documented:**
- Files: Persistence layer EF configurations under `Persistence/Configurations/`
- Concern: Without explicit index definitions in EF, the database schema depends on auto-generated indexes from migrations. Performance on frequently-queried columns (e.g., `EmployeeId`, `DepartmentId`, `Status`) may be suboptimal if indexes were not created by the migration.
- Recommendation: Review `Persistence/Migrations/` for columns used in WHERE clauses (leave filters by EmployeeId, department, status; timesheet filters by EmployeeProfileId, department, status) and ensure indexes are created. EF's `.HasIndex()` API can be used in configuration files.

---

*Concerns audit: 2026-08-21*
