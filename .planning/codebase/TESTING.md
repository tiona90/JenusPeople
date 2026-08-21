# Testing Patterns

**Analysis Date:** 2026-08-21

## Test Framework

### Backend

**Runner:**
- xUnit (v2.x) — visible in `Tests/WorkTrack.Tests/*.cs` via `[Fact]` and `[Theory]` attributes
- Config: Project file references MsTest/xUnit implicitly; no explicit config file

**Assertion Library:**
- xUnit's built-in assertions: `Assert.True()`, `Assert.Equal()`, `Assert.Single()`, `Assert.Throws<T>()`, etc.
- No Fluent Assertions or NSubstitute visible; fakes are hand-written

**Run Commands:**
```bash
dotnet test                                      # Run all tests
dotnet test --filter "ClassName" --verbosity:q  # Run specific class
dotnet test --configuration Release              # Run in Release mode
```

**CI:**
- `.github/workflows/main.yml` runs: `dotnet test Annualleave.sln --configuration Release --no-build`
- Both pull requests and pushes to main trigger CI
- Runs on Ubuntu (ubuntu-latest runner)

### Frontend

**Runner:**
- Vitest 3.2.7 (configured in `client/vite.config.ts`)
- Environment: `jsdom` (browser-like DOM for testing React)
- Globals enabled (no imports of `describe`, `it`, `expect` needed)

**Assertion Library:**
- Vitest's built-in assertions
- `@testing-library/react` (`screen`, `render`, `within`, `fireEvent`, `waitFor`)
- `@testing-library/jest-dom` (matchers like `toBeInTheDocument()`)

**Run Commands:**
```bash
cd client
npm run test                    # Run all tests (vitest run)
npm run test -- --watch       # Watch mode
npm run test -- --coverage    # Coverage report
```

**CI:**
- No test command in `.github/workflows/main.yml` for frontend (only lint + build)
- Linting: `npm run lint` runs ESLint, not tests

## Test File Organization

### Backend

**Location:**
- Separate directory: `Tests/WorkTrack.Tests/` (co-located with solution, not in source trees)
- Each test file corresponds to a handler, query, or feature

**Naming:**
- Feature-focused: `CreateAdminUserCommandTests.cs`, `AnnualLeaveTotalDaysTests.cs`, `AttendanceActionsTests.cs`
- Suffix is always `Tests` (not `Test`)
- Pattern: `[FeatureName][ComponentType]Tests.cs`

**Structure:**
```
Tests/
└── WorkTrack.Tests/
    ├── CreateAdminUserCommandTests.cs
    ├── DeleteAnnualLeaveTests.cs
    ├── AttendanceActionsTests.cs
    ├── TestSupport.cs                    (Test database, fake services)
    ├── TransactionalTestDb.cs            (SQLite in-memory for transactions)
    ├── ApiRouteTableFixture.cs           (Shared fixtures)
    └── ... (48 test files)
```

### Frontend

**Location:**
- Co-located with source: `client/src/components/admin/AdminUsersPanel.test.tsx` (alongside `AdminUsersPanel.tsx`)
- All test files under `client/src/`

**Naming:**
- `ComponentName.test.tsx` or `module.test.ts`
- Example: `AdminUsersPanel.test.tsx`, `settingsPlacement.test.tsx`

**Current Coverage:**
- 8 test files in total
- Primary test file: `AdminUsersPanel.test.tsx` (comprehensive, 380+ lines)
- Most panels/pages have corresponding test files

## Test Structure

### Backend (xUnit)

**Suite Organization:**

```csharp
public class CreateAdminUserCommandTests : IDisposable
{
    private const string ExistingEmail = "taken@test.local";

    private readonly ServiceProvider _services;

    public CreateAdminUserCommandTests()
    {
        var collection = new ServiceCollection();
        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"create-admin-user-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        
        collection.AddIdentityCore<User>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>();

        _services = collection.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();
    private UserManager<User> Users => _services.GetRequiredService<UserManager<User>>();

    private async Task SeedAsync()
    {
        var db = Db;
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        await db.SaveChangesAsync();
        // ... more seeding
    }

    private static AdminCreateUserDto Payload(
        string email = "newjoiner@test.local",
        string displayName = "New Joiner",
        int departmentId = 1,
        string? role = AppRoles.Employee) => new() { ... };

    private Task<Result<AdminUserDto>> Handle(AdminCreateUserDto payload, FakeAccountEmailSender mail) =>
        new CreateAdminUser.Handler(Db, Users, mail, NullLogger<CreateAdminUser.Handler>.Instance)
            .Handle(new CreateAdminUser.Command { User = payload }, CancellationToken.None);

    /* ── Test Methods ─────────────────────── */

    [Fact]
    public async Task A_created_user_comes_back_in_the_shape_the_admin_panel_reads()
    {
        await SeedAsync();
        var mail = new FakeAccountEmailSender();

        var result = await Handle(Payload(), mail);

        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Value!;
        Assert.Equal("newjoiner@test.local", dto.Email);
        Assert.Equal("newjoiner@test.local", dto.UserName);
        Assert.Equal("New Joiner", dto.DisplayName);
        Assert.True(dto.EmailConfirmed);
        Assert.Equal([AppRoles.Employee], dto.Roles);
        Assert.True(dto.InviteEmailSent);
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("")]
    public async Task Email_is_validated(string badEmail)
    {
        await SeedAsync();
        var result = await Validate(Payload(email: badEmail));
        
        Assert.False(result.IsValid);
    }
}
```

**Key Patterns:**
- `IDisposable` fixture pattern — test class sets up ServiceProvider in constructor, disposes in `Dispose()`
- Derived properties for commonly-needed services: `private AppDbContext Db => _services.GetRequiredService<AppDbContext>()`
- Helper methods for common operations: `SeedAsync()`, `Payload()`, `Handle()`, `Validate()`
- Test names are descriptive sentences: `A_created_user_comes_back_in_the_shape_the_admin_panel_reads()`
- Comments separate test sections with divider lines (`/* ── The happy path ──── */`)

### Frontend (Vitest + React Testing Library)

**Suite Organization:**

```typescript
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createAdminUser } from '../../lib/api'
import AdminUsersPanel from './AdminUsersPanel'

// Mock entire API module
vi.mock('../../lib/api', () => ({
    getAdminUsers: vi.fn(),
    getAppSettings: vi.fn(),
    createAdminUser: vi.fn(),
    updateAdminUser: vi.fn(),
    // ... other functions
}))

const api = vi.mocked(await import('../../lib/api'))

const DEPARTMENT = { id: 7, name: 'Engineering', code: 'ENG', isActive: true }
const ANNUAL_LEAVE_TYPE = { id: 1, name: 'Annual Leave', isActive: true, affectsBalance: true, defaultAllowance: 25 }

beforeEach(() => {
    vi.clearAllMocks()
    api.getAdminUsers.mockResolvedValue([])
    api.getDepartments.mockResolvedValue([DEPARTMENT] as never)
    api.getLeaveTypes.mockResolvedValue([ANNUAL_LEAVE_TYPE] as never)
    // ... more setup
})

function renderPanel() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
        <QueryClientProvider client={queryClient}>
            <AdminUsersPanel />
        </QueryClientProvider>,
    )
}

async function openCreateDialog() {
    renderPanel()
    const addUser = await screen.findByText('+ Add user')
    fireEvent.click(addUser)
    return screen.getByRole('dialog')
}

describe('AdminUsersPanel — Create User', () => {
    it('offers no password field', async () => {
        const dialog = await openCreateDialog()
        expect(within(dialog).queryByLabelText(/password/i)).not.toBeInTheDocument()
        expect(dialog.querySelectorAll('input[type="password"]')).toHaveLength(0)
    })

    it('will not submit without a display name', async () => {
        const dialog = await openCreateDialog()
        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newjoiner@example.test' } })
        
        expect(within(dialog).getByText('Display name is required')).toBeInTheDocument()
        expect(within(dialog).getByRole('button', { name: /^create$/i })).toBeDisabled()
    })

    it('sends the account details without a password', async () => {
        const dialog = await openCreateDialog()
        api.createAdminUser.mockResolvedValue({
            id: 'u1',
            userName: 'newjoiner@example.test',
            email: 'newjoiner@example.test',
            displayName: 'New Joiner',
            imageUrl: '',
            emailConfirmed: true,
            roles: ['Employee'],
            inviteEmailSent: true,
        })

        fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newjoiner@example.test' } })
        fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Joiner' } })
        await selectDepartment(dialog)
        fireEvent.click(within(dialog).getByRole('button', { name: /^create$/i }))

        await waitFor(() => expect(createAdminUser).toHaveBeenCalledTimes(1))
        expect(api.createAdminUser.mock.calls[0][0]).toEqual({
            email: 'newjoiner@example.test',
            displayName: 'New Joiner',
            roles: ['Employee'],
            departmentId: DEPARTMENT.id,
            managerId: null,
            jobTitle: null,
            annualLeaveEntitlement: ANNUAL_LEAVE_TYPE.defaultAllowance,
            phoneNumber: null,
            dateOfBirth: null,
        })
    })
})

describe('AdminUsersPanel — presence', () => {
    const USERS = [
        { id: 'u-in', userName: 'in@example.test', displayName: 'Checked In', roles: ['Employee'] },
        // ...
    ]

    beforeEach(() => {
        api.getAdminUsers.mockResolvedValue(USERS as never)
        api.getUserPresence.mockResolvedValue([
            { userId: 'u-in', status: 'online', checkInAt: '2026-08-04T08:00:00Z', lastActivityAt: '2026-08-04T08:00:00Z' },
            // ...
        ])
    })

    it('badges one user Online, one Away, and the rest Offline', async () => {
        renderPanel()
        expect(await screen.findByText('Online')).toBeInTheDocument()
        expect(screen.getAllByText('Online')).toHaveLength(1)
        expect(screen.getAllByText('Away')).toHaveLength(1)
        expect(screen.getAllByText('Offline')).toHaveLength(2)
    })
})
```

**Key Patterns:**
- Global setup with `beforeEach()` for mock configuration
- Entire API module mocked: `vi.mock('../../lib/api', () => ({ ... }))`
- Fixture functions at module level: `renderPanel()`, `openCreateDialog()`
- Tests use `screen` queries extensively: `screen.findByText()`, `screen.getByRole()`
- Scoping with `within(dialog)` to test dialog contents
- `waitFor()` for async assertion after user interactions
- Describe blocks organize tests by feature (`AdminUsersPanel — Create User`, `AdminUsersPanel — presence`)
- Test names are sentences: `'offers no password field'`, `'will not submit without a display name'`

## Mocking

### Backend

**Framework:** Hand-written fake implementations (`IDisposable` or similar interface)

**Patterns:**

```csharp
/// <summary>
/// Records what the handler asked for instead of sending it,
/// and can refuse the way a rejecting mail provider does.
/// </summary>
private sealed class FakeAccountEmailSender : IAccountEmailSender
{
    public bool Result { get; set; } = true;
    public User? Invited { get; private set; }

    public string BuildClientUrl(string route, IDictionary<string, string?>? query = null) 
        => $"https://test.local{route}";

    public Task<bool> SendWelcomeInviteAsync(User user, CancellationToken cancellationToken = default)
    {
        Invited = user;
        return Task.FromResult(Result);
    }

    public Task<bool> SendPasswordResetAsync(User user, CancellationToken cancellationToken = default) 
        => Task.FromResult(Result);
}
```

**What to Mock:**
- External dependencies: `IEmailService`, `IAccountEmailSender`, `IFileUploadService`
- Keep mocks simple and focused (no complex verification chains)

**What NOT to Mock:**
- `AppDbContext` — use real EF providers (`TestDb` or `TransactionalTestDb`)
- Domain logic — test with real entities
- MediatR handlers themselves — test via handler's public interface

### Frontend

**Framework:** Vitest's `vi.mock()` and `vi.fn()`

**Patterns:**

```typescript
vi.mock('../../lib/api', () => ({
    getAdminUsers: vi.fn(),
    getAppSettings: vi.fn(),
    getDepartments: vi.fn(),
    createAdminUser: vi.fn(),
    updateAdminUser: vi.fn(),
    // ... other functions
}))

const api = vi.mocked(await import('../../lib/api'))

beforeEach(() => {
    vi.clearAllMocks()
    api.getAdminUsers.mockResolvedValue([])
    api.getDepartments.mockResolvedValue([DEPARTMENT] as never)
})

// In test:
api.createAdminUser.mockResolvedValue({ /* response */ })
fireEvent.click(createButton)
await waitFor(() => expect(api.createAdminUser).toHaveBeenCalledTimes(1))
expect(api.createAdminUser.mock.calls[0][0]).toEqual({ /* expected payload */ })
```

**What to Mock:**
- API calls (`lib/api` modules)
- React Query: no mocking of QueryClient needed; mocking API automatically mocks queries
- External libraries: only if necessary (React Router, MobX stores usually left unmocked)

**What NOT to Mock:**
- React Testing Library utilities (`render`, `screen`, `fireEvent`)
- Component logic (test via user interactions, not by mocking internal functions)
- Hooks like `useState` or `useMemo` (Vitest doesn't support this well; test behavior instead)

## Fixtures and Factories

### Backend

**Test Data Pattern:**

```csharp
private static AdminCreateUserDto Payload(
    string email = "newjoiner@test.local",
    string displayName = "New Joiner",
    int departmentId = 1,
    string? role = AppRoles.Employee) => new()
{
    Email = email,
    DisplayName = displayName,
    DepartmentId = departmentId,
    Roles = role is null ? [] : [role],
    JobTitle = "Engineer",
};

// Usage in tests:
var result = await Handle(Payload(), mail);
var result2 = await Handle(Payload(email: "other@test.local"), mail);
```

**Seeding Pattern:**

```csharp
private async Task SeedAsync()
{
    var db = Db;
    db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
    await db.SaveChangesAsync();

    foreach (var role in new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.Employee })
    {
        await Roles.CreateAsync(new Role { Name = role });
    }

    db.ChangeTracker.Clear();  // Detach seeded entities so handler works with fresh query
}
```

**Location:**
- In test class as private methods
- No separate factory files; minimal test data setup

### Frontend

**Module-Level Constants:**

```typescript
const DEPARTMENT = { id: 7, name: 'Engineering', code: 'ENG', isActive: true }

const ANNUAL_LEAVE_TYPE = {
    id: 1, name: 'Annual Leave', isActive: true, affectsBalance: true, defaultAllowance: 25,
    allowanceUnit: 'days/year',
}

const USERS = [
    { id: 'u-in', userName: 'in@example.test', email: 'in@example.test', displayName: 'Checked In', roles: ['Employee'] },
    { id: 'u-out', userName: 'out@example.test', email: 'out@example.test', displayName: 'Never In', roles: ['Employee'] },
]
```

**Fixture Functions:**

```typescript
function renderPanel() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
        <QueryClientProvider client={queryClient}>
            <AdminUsersPanel />
        </QueryClientProvider>,
    )
}

async function openCreateDialog() {
    renderPanel()
    const addUser = await screen.findByText('+ Add user')
    fireEvent.click(addUser)
    return screen.getByRole('dialog')
}

async function selectDepartment(dialog: HTMLElement) {
    fireEvent.mouseDown(within(dialog).getByRole('combobox'))
    const option = await screen.findByRole('option', { name: `${DEPARTMENT.name} (${DEPARTMENT.code})` })
    fireEvent.click(option)
}
```

**Location:**
- In test file, module-scoped constants and functions
- No separate fixture library

## Coverage

### Requirements

**Backend:** Not enforced explicitly (no coverage thresholds in CI)

**Frontend:** Not enforced explicitly (CI doesn't run tests at all, only lint + build)

### View Coverage

**Backend:**
```bash
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover
```

**Frontend:**
```bash
cd client
npm run test -- --coverage
```

## Test Types

### Backend

**Unit Tests:**
- Focus: Individual handler/query logic, calculations, validation
- Example: `CreateAdminUserCommandTests` — handler accepts payload, returns expected DTO
- Use `TestDb` (EF in-memory) for speed; no transactions tested here

**Integration Tests:**
- Focus: Handler + real EF Core + database constraints
- Example: `DuplicateTimesheetTests` — confirms unique constraint blocks duplicate entries
- Use `TransactionalTestDb` (SQLite in-memory) to test transaction behavior
- Always use this provider when asserting on constraint or transaction behavior

**End-to-End (Controller) Tests:**
- Focus: HTTP routing, authorization policies, response bodies
- Example: `ApiRouteTableFixture` — verifies all routes are registered and callable
- Limited coverage; most E2E logic tested via unit/integration tests

### Frontend

**Component Tests:**
- Focus: User interactions, rendering, conditional display
- Example: `AdminUsersPanel.test.tsx` — tests role selection UI, form validation, API calls
- Use `render()` + `screen` queries to test from user perspective
- All 8 test files are component tests; no unit tests for isolated functions

**No E2E Tests:**
- Cypress/Playwright not set up
- CI does not run any frontend tests (only lint + build)

## Common Patterns

### Async Testing

**Backend (xUnit):**
```csharp
[Fact]
public async Task Handler_returns_success_result()
{
    await SeedAsync();
    var result = await Handle(Payload(), mail);
    
    Assert.True(result.IsSuccess, result.Error);
}

[Theory]
[InlineData("invalid@")]
[InlineData("")]
public async Task Email_validation_fails_for(string badEmail)
{
    var result = await Validate(Payload(email: badEmail));
    Assert.False(result.IsValid);
}
```

**Frontend (Vitest):**
```typescript
it('sends the account details without a password', async () => {
    const dialog = await openCreateDialog()  // await for async rendering
    api.createAdminUser.mockResolvedValue({ /* ... */ })

    fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newjoiner@example.test' } })
    fireEvent.click(within(dialog).getByRole('button', { name: /^create$/i }))

    await waitFor(() => expect(api.createAdminUser).toHaveBeenCalledTimes(1))  // await for async handler
    expect(api.createAdminUser.mock.calls[0][0]).toEqual({ /* expected payload */ })
})
```

### Error Testing

**Backend (xUnit):**
```csharp
[Fact]
public async Task Duplicate_email_returns_conflict_status()
{
    await SeedAsync();
    var existing = new User { Email = "taken@test.local", UserName = "taken@test.local" };
    var db = Db;
    db.Users.Add(existing);
    await db.SaveChangesAsync();

    var result = await Handle(Payload(email: "taken@test.local"), new FakeAccountEmailSender());

    Assert.False(result.IsSuccess);
    Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
}
```

**Frontend (Vitest):**
```typescript
it('reports whether the welcome email went out', async () => {
    const dialog = await openCreateDialog()

    api.createAdminUser.mockResolvedValue({
        id: 'u1',
        email: 'newjoiner@example.test',
        displayName: 'New Joiner',
        inviteEmailSent: false,  // Simulate failed send
    })

    fireEvent.change(within(dialog).getByLabelText(/email/i), { target: { value: 'newjoiner@example.test' } })
    fireEvent.change(within(dialog).getByLabelText(/display name/i), { target: { value: 'New Joiner' } })
    fireEvent.click(within(dialog).getByRole('button', { name: /^create$/i }))

    expect(await screen.findByText(/the welcome email could not be sent/i)).toBeInTheDocument()
})
```

### Transaction Testing

**Backend Only (Frontend has no transactions):**

```csharp
[Fact]
public async Task Balance_sync_must_not_be_written_if_leave_write_fails()
{
    // Use TransactionalTestDb to test actual transactions
    await using var db = await TransactionalTestDb.CreateAsync();
    var interceptor = new FailOnNthSaveInterceptor(failOnSave: 2);
    
    // Attach a second context to the same connection
    await using var watcher = TransactionalTestDb.Attach(db);
    
    // Handler attempts two saves: leave + balance. Fail the second.
    interceptor.Arm();
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => handler.Handle(command, CancellationToken.None));
    
    Assert.Contains("Simulated failure", exception.Message);
    
    // Verify the leave was NOT written
    var leaves = await db.AnnualLeaves.ToListAsync();
    Assert.Empty(leaves);
}
```

**Key Distinction:**
- `TestDb` (in-memory): Cannot test transactions; use for fast unit tests
- `TransactionalTestDb` (SQLite): Tests real transactions, unique constraints, foreign keys

---

*Testing analysis: 2026-08-21*
