# Per-Child Paternity Leave Entitlement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Paternity Leave's flat 14-day allowance with a per-child entitlement — 18 weeks per child, only while that child is under 15, capped at 5 weeks per child per leave year, tracked separately for each child.

**Architecture:** A new `Child` entity hanging off `EmployeeProfile` stores date of birth only; age and eligibility are computed on every read, so a child ages out with no background job and no column to go stale. A pure `PerChildLeaveCalculationService` in `Domain` holds the arithmetic; a `PerChildLeaveBalanceCalculator` in `Application` (a sibling of the existing `AnnualLeaveBalanceCalculator`) loads inputs from `AppDbContext` and returns a human-readable refusal string or `null`. All calendar work reuses the existing `LeaveCalculationService`. The three rule numbers become admin-editable columns on `LeaveType`, gated by a `PerChildEntitlement` toggle.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core (SQL Server), MediatR, FluentValidation, AutoMapper, xUnit. React 19 + TypeScript + Vite, MUI 7, MobX, TanStack React Query, react-hook-form + zod, vitest 3.2.7.

**Spec:** `docs/superpowers/specs/2026-09-10-paternity-per-child-leave-design.md` — read it alongside this plan. Every task argues from it.

## Global Constraints

**Rule values (exact, from the spec):**
- 1 week of per-child leave = **5 business days**. 18 weeks = **90 business days**; 5 weeks = **25 business days**.
- Lifetime entitlement: **18 weeks per eligible child**.
- Annual cap: **5 weeks per eligible child per leave year**.
- Eligibility ends at age **15** — the child must be under 15 on the request's **end date**.
- The "year" is the configured leave year (`AppSettings.LeaveYearStartMonth`), not the calendar year.
- Only `Approved` leave counts as used. `Pending`, `Rejected`, `Cancelled` do not.
- `AnnualLeave.ChildId` is **nullable**. Pre-existing paternity rows keep `null` and count against no child.

**Codebase rules (from CLAUDE.md — non-negotiable):**
- **No repository layer.** Inject `AppDbContext` straight into handlers. Never introduce `IRepository`/`IUnitOfWork`.
- Handlers return `Result<T>` and **never throw for business errors**. Controllers stay thin: dispatch to MediatR, then `HandleResult<T>()`.
- FluentValidation validators live in the same folder as the command/query and auto-run via MediatR's `ValidationBehavior`.
- Business logic belongs in `Application/*/Commands/` and `Application/*/Queries/`, not in controllers.
- **Never write a 0 `AnnualLeaveEntitlement`** — `CheckSufficientBalanceAsync` returns early on `<= 0`, disabling the balance check. (The new per-child columns are the deliberate opposite: a 0 there refuses everything.)
- Data repairs must be **EF migrations**, never `DbInitializer` changes: `Seed:Enabled` is `false` in Production, so the seeder never runs on the deployed host.
- Handler tests run against a real EF provider, not mocks. `TestDb` (EF in-memory) for ordinary handler tests; `TransactionalTestDb` (SQLite in-memory) whenever a constraint, unique index, foreign key or transaction is the subject.

**Commands (this repo has three known traps — use these exact forms):**

Backend tests:
```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
`--artifacts-path` is required because a running dev API locks `API/bin` (`MSB3027`, "locked by .NET Host"); `DOTNET_EnableWriteXorExecute=0` is required because the testhost otherwise dies with "Failed to create RW mapping for RX memory". Add `--filter FullyQualifiedName~ClassName` to run one class. Full suite is ~713 tests in ~25 s.

Migrations (`dotnet ef` has no `--artifacts-path`, so use env vars, and restore first):
```bash
export UseArtifactsOutput=true ArtifactsPath=C:/temp/wt-ef
dotnet restore Annualleave.sln
dotnet ef migrations add <Name> --project Persistence --startup-project API
```
Without the `dotnet restore` you get `NETSDK1004: Assets file project.assets.json not found`.

Frontend tests — **never `npx vitest`** (a stray vitest 5 at `C:\Users\user\node_modules` wins and fails every test with "Invalid Chai property: toBeInTheDocument"):
```bash
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client <paths>
```
Check the banner says `RUN v3.2.7`. Also: the Bash tool's cwd persists between calls, so prefer absolute paths.

Frontend build/lint:
```bash
cd /c/Practice/Own/2026/WorkTrack/client && npm run build && npm run lint
```

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `Domain/Child.cs` | The `Child` entity. DOB only — no age, no eligibility flag. |
| `Domain/Services/PerChildLeaveCalculationService.cs` | Pure arithmetic: weeks↔business days, age, eligibility window, remaining. |
| `Application/AnnualLeaves/Commands/LeaveYearQueries.cs` | Shared DB helpers (holiday set, leave-year start month) used by both balance calculators. |
| `Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs` | Loads inputs, enforces ownership, eligibility, lifetime cap, annual cap. Returns an error string or `null`. |
| `Application/Children/DTOs/ChildDto.cs` | One child as the client sees it. |
| `Application/Children/DTOs/UpsertChildRequest.cs` | Add/edit payload. |
| `Application/Children/DTOs/ChildLeaveEntitlementDto.cs` | Per-child ledger row + the employee's totals wrapper. |
| `Application/Children/Validators/UpsertChildRequestValidator.cs` | Name and DOB rules. |
| `Application/Children/Support/ChildAccessResolver.cs` | Which employee's children may this caller see or edit. |
| `Application/Children/Commands/CreateChild.cs` | Add a child; sets `HasChildren = true`. |
| `Application/Children/Commands/UpdateChild.cs` | Rename / correct DOB. |
| `Application/Children/Commands/DeleteChild.cs` | Refuses while leave references the child. |
| `Application/Children/Queries/GetChildList.cs` | The list, with computed age and eligibility. |
| `Application/Children/Queries/GetChildLeaveEntitlements.cs` | The ledger. |
| `API/Controllers/ChildrenController.cs` | Thin CRUD + entitlements. |
| `client/src/lib/types/child.ts` | `Child`, `ChildLeaveEntitlement`, `ChildLeaveEntitlementSummary`. |
| `client/src/lib/api/children.ts` | Thin axios wrappers. |
| `client/src/components/annual-leave/ChildLeavePicker.tsx` | The required child select on the request form. |
| `client/src/components/layout/ChildrenSection.tsx` | The children block inside the Edit profile dialog. |
| `Tests/WorkTrack.Tests/PerChildLeaveCalculationServiceTests.cs` | Domain arithmetic. |
| `Tests/WorkTrack.Tests/PerChildLeaveEntitlementTests.cs` | Ownership, eligibility, the 15th-birthday boundary. |
| `Tests/WorkTrack.Tests/PerChildLeaveCapTests.cs` | Lifetime cap, annual cap, both worked examples. |
| `Tests/WorkTrack.Tests/ChildCrudTests.cs` | CRUD, delete-restrict, `HasChildren` invariant. |
| `Tests/WorkTrack.Tests/PaternityLeaveTypeConfigTests.cs` | The four `LeaveType` columns and their validation. |
| `client/src/components/annual-leave/ChildLeavePicker.test.tsx` | Picker rendering and blocked submit. |

**Modified:** `Domain/EmployeeProfile.cs`, `Domain/AnnualLeave.cs`, `Domain/LeaveType.cs`, `Persistence/AppDbContext.cs`, `Persistence/DbInitializer.cs`, `Application/Core/MappingProfiles.cs`, `Application/AnnualLeaves/Commands/{AnnualLeaveBalanceCalculator,CreateAnnualLeave,EditAnnualLeave,UpdateLeaveStatus}.cs`, `Application/AnnualLeaves/DTOs/{BaseAnnualLeaveDto,AnnualLeaveDto}.cs`, `Application/AnnualLeaves/Queries/{GetAnnualLeaveList,GetAnnualLeaveDetails}.cs`, `Application/LeaveTypes/DTOs/{UpsertLeaveTypeRequest,LeaveTypeDto}.cs`, `Application/LeaveTypes/Validators/UpsertLeaveTypeRequestValidator.cs`, `Application/LeaveTypes/Queries/GetLeaveTypeList.cs`, `Application/Accounts/DTOs/UpdateProfileDto.cs`, `API/Controllers/AccountController.cs`, `client/src/lib/types/{leave-type,annual-leave,auth,index}.ts`, `client/src/lib/api/index.ts`, `client/src/lib/leave-allowance.ts`, `client/src/lib/validation/leave.ts`, `client/src/components/layout/Sidebar.tsx`, `client/src/components/annual-leave/{AnnualLeaveForm,ApplyLeavePage,MyLeavePage}.tsx`, `client/src/components/admin/LeaveTypesPanel.tsx`, `CLAUDE.md`.

---

### Task 1: Pure per-child arithmetic

The one place weeks become days and a date of birth becomes an age. Pure — no `DbContext`, no clock — so every later task can rely on it without setting up a database.

**Files:**
- Create: `Domain/Services/PerChildLeaveCalculationService.cs`
- Test: `Tests/WorkTrack.Tests/PerChildLeaveCalculationServiceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Domain.Services.PerChildLeaveCalculationService` with `const int BusinessDaysPerWeek = 5`, `int WeeksToBusinessDays(int weeks)`, `decimal BusinessDaysToWeeks(int businessDays)`, `int AgeOn(DateOnly dateOfBirth, DateOnly onDate)`, `DateOnly LastEligibleDate(DateOnly dateOfBirth, int maxAge)`, `bool IsEligibleOn(DateOnly dateOfBirth, DateOnly onDate, int maxAge)`, `int RemainingDays(int totalDays, int usedDays)`.

- [ ] **Step 1: Write the failing tests**

Create `Tests/WorkTrack.Tests/PerChildLeaveCalculationServiceTests.cs`:

```csharp
using Domain.Services;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Paternity leave is stated in weeks, but every other leave calculation in the app
/// counts business days. This service is the only place the two meet, and the only
/// place a date of birth becomes an age — nothing stores a child's age, so that a
/// child ages out of eligibility with no job to run and no column to go stale.
/// </summary>
public class PerChildLeaveCalculationServiceTests
{
    [Theory]
    [InlineData(18, 90)]
    [InlineData(5, 25)]
    [InlineData(1, 5)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void A_week_is_five_business_days(int weeks, int expectedDays)
    {
        Assert.Equal(expectedDays, PerChildLeaveCalculationService.WeeksToBusinessDays(weeks));
    }

    [Theory]
    [InlineData(90, 18.0)]
    [InlineData(25, 5.0)]
    [InlineData(6, 1.2)]
    [InlineData(0, 0.0)]
    public void Days_read_back_as_weeks_for_display(int days, decimal expectedWeeks)
    {
        Assert.Equal(expectedWeeks, PerChildLeaveCalculationService.BusinessDaysToWeeks(days));
    }

    [Fact]
    public void Age_turns_over_on_the_birthday_not_before_it()
    {
        var dob = new DateOnly(2011, 3, 4);

        Assert.Equal(14, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2026, 3, 3)));
        Assert.Equal(15, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2026, 3, 4)));
        Assert.Equal(15, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2026, 3, 5)));
    }

    /// <summary>
    /// A 29 February child completes a year of life on 1 March in a common year.
    /// Treating 28 February as the birthday would hand them a day of entitlement
    /// they are no longer entitled to.
    /// </summary>
    [Fact]
    public void A_leap_day_child_ages_on_the_first_of_March_in_a_common_year()
    {
        var dob = new DateOnly(2012, 2, 29);

        Assert.Equal(14, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2027, 2, 28)));
        Assert.Equal(15, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2027, 3, 1)));
        // 2028 is a leap year, so the birthday is the real one.
        Assert.Equal(15, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2028, 2, 28)));
        Assert.Equal(16, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2028, 2, 29)));
    }

    [Fact]
    public void The_last_eligible_date_is_the_day_before_the_fifteenth_birthday()
    {
        Assert.Equal(
            new DateOnly(2026, 3, 3),
            PerChildLeaveCalculationService.LastEligibleDate(new DateOnly(2011, 3, 4), maxAge: 15));

        Assert.Equal(
            new DateOnly(2027, 2, 28),
            PerChildLeaveCalculationService.LastEligibleDate(new DateOnly(2012, 2, 29), maxAge: 15));
    }

    [Fact]
    public void Eligibility_ends_on_the_fifteenth_birthday()
    {
        var dob = new DateOnly(2011, 3, 4);

        Assert.True(PerChildLeaveCalculationService.IsEligibleOn(dob, new DateOnly(2026, 3, 3), 15));
        Assert.False(PerChildLeaveCalculationService.IsEligibleOn(dob, new DateOnly(2026, 3, 4), 15));
    }

    /// <summary>
    /// Floored at zero, mirroring LeaveCalculationService.CalculateRemainingBalance:
    /// a negative remaining figure is meaningless on a screen and dangerous in a
    /// comparison.
    /// </summary>
    [Theory]
    [InlineData(90, 25, 65)]
    [InlineData(90, 90, 0)]
    [InlineData(90, 120, 0)]
    public void Remaining_never_goes_negative(int total, int used, int expected)
    {
        Assert.Equal(expected, PerChildLeaveCalculationService.RemainingDays(total, used));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PerChildLeaveCalculationServiceTests
```
Expected: build failure — `PerChildLeaveCalculationService` does not exist (`CS0103`/`CS0246`).

- [ ] **Step 3: Write the implementation**

Create `Domain/Services/PerChildLeaveCalculationService.cs`:

```csharp
namespace Domain.Services;

/// <summary>
/// Per-child leave arithmetic: the week-to-business-day conversion, and the age
/// and eligibility questions a date of birth answers.
///
/// Pure, like <see cref="LeaveCalculationService"/>: no DbContext, no clock, no I/O.
/// Business-day counting, holiday exclusion and leave-year windows are NOT repeated
/// here — callers use <see cref="LeaveCalculationService"/> for those, so there is
/// only ever one calendar in the app.
///
/// Nothing stores a child's age. It is computed on every read, which is what makes
/// a child aging out of eligibility automatic: no nightly job, and no IsEligible
/// column that can disagree with the date of birth beside it.
/// </summary>
public static class PerChildLeaveCalculationService
{
    /// <summary>
    /// The one place a week becomes days. A "week" of per-child leave is a working
    /// week, so weekends and public holidays inside a request do not consume it.
    /// </summary>
    public const int BusinessDaysPerWeek = 5;

    public static int WeeksToBusinessDays(int weeks)
        => weeks <= 0 ? 0 : weeks * BusinessDaysPerWeek;

    /// <summary>
    /// Days read back as weeks, for display only. One decimal place: a 6-day
    /// request is "1.2 weeks", which is honest, where rounding to 1 would not be.
    /// </summary>
    public static decimal BusinessDaysToWeeks(int businessDays)
        => businessDays <= 0
            ? 0m
            : Math.Round(businessDays / (decimal)BusinessDaysPerWeek, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Whole years completed on <paramref name="onDate"/>. Zero before birth.
    /// </summary>
    public static int AgeOn(DateOnly dateOfBirth, DateOnly onDate)
    {
        if (onDate <= dateOfBirth) return 0;

        var age = onDate.Year - dateOfBirth.Year;
        if (onDate < BirthdayInYear(dateOfBirth, onDate.Year)) age--;
        return age;
    }

    /// <summary>
    /// The last date a request for this child may END on — the day before the
    /// birthday on which they reach <paramref name="maxAge"/>. Named in the
    /// refusal message, so an employee learns the boundary rather than guessing.
    /// </summary>
    public static DateOnly LastEligibleDate(DateOnly dateOfBirth, int maxAge)
        => BirthdayInYear(dateOfBirth, dateOfBirth.Year + maxAge).AddDays(-1);

    public static bool IsEligibleOn(DateOnly dateOfBirth, DateOnly onDate, int maxAge)
        => AgeOn(dateOfBirth, onDate) < maxAge;

    /// <summary>
    /// Floored at zero, mirroring
    /// <see cref="LeaveCalculationService.CalculateRemainingBalance"/>.
    /// </summary>
    public static int RemainingDays(int totalDays, int usedDays)
        => Math.Max(0, totalDays - usedDays);

    /// <summary>
    /// A 29 February birthday falls on 1 March in a common year: the child
    /// completes a year of life the day after 28 February, not on it.
    /// </summary>
    private static DateOnly BirthdayInYear(DateOnly dateOfBirth, int year)
        => dateOfBirth is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(year)
            ? new DateOnly(year, 3, 1)
            : new DateOnly(year, dateOfBirth.Month, dateOfBirth.Day);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PerChildLeaveCalculationServiceTests
```
Expected: PASS, 7 test methods (17 cases with the theory rows).

- [ ] **Step 5: Commit**

```bash
git add Domain/Services/PerChildLeaveCalculationService.cs Tests/WorkTrack.Tests/PerChildLeaveCalculationServiceTests.cs
git commit -m "Add pure per-child leave arithmetic

A week of per-child leave is five business days, and a child's age is computed
from their date of birth on every read rather than stored, so eligibility ends
at 15 with no job to run and no column to go stale."
```

---

### Task 2: Schema — the Child entity and the new columns

Everything the rest of the plan writes to. One schema migration covers all four changes so there is a single deploy step.

**Files:**
- Create: `Domain/Child.cs`
- Modify: `Domain/EmployeeProfile.cs` (add `HasChildren`, `Children`), `Domain/AnnualLeave.cs` (add `ChildId`, `Child`), `Domain/LeaveType.cs` (add four columns), `Persistence/AppDbContext.cs` (`DbSet<Child>`, config in three places)
- Create: `Persistence/Migrations/<timestamp>_AddChildLeaveEntitlement.cs` (generated)
- Test: `Tests/WorkTrack.Tests/ChildCrudTests.cs` (schema half only; CRUD arrives in Task 9)

**Interfaces:**
- Consumes: Task 1's service (not yet — schema only).
- Produces: `Domain.Child` with `string Id`, `string EmployeeProfileId`, `EmployeeProfile? EmployeeProfile`, `string Name`, `DateOnly DateOfBirth`, `DateTime CreatedAt`, `ICollection<AnnualLeave> LeaveRequests`. `EmployeeProfile.HasChildren` (`bool?`) and `EmployeeProfile.Children` (`ICollection<Child>`). `AnnualLeave.ChildId` (`string?`) and `AnnualLeave.Child` (`Child?`). `LeaveType.PerChildEntitlement` (`bool`), `LeaveType.PerChildTotalWeeks`, `LeaveType.PerChildWeeksPerYear`, `LeaveType.ChildEligibleUntilAge` (all `int`). `AppDbContext.Children`.

- [ ] **Step 1: Write the failing test**

Create `Tests/WorkTrack.Tests/ChildCrudTests.cs`. Both tests use `TransactionalTestDb` because a foreign key is the subject and the EF in-memory provider enforces none.

```csharp
using Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A child's row carries the ledger that proves how much per-child leave was taken
/// for them, so it cannot be deleted out from under approved leave. A child who has
/// aged out is kept, not deleted — their history still matters, and they simply read
/// as ineligible.
///
/// TransactionalTestDb, not TestDb: the EF in-memory provider enforces no foreign
/// keys at all, so an assertion about one passes whether or not the model says
/// anything.
/// </summary>
public class ChildCrudTests
{
    private static async Task<EmployeeProfile> SeedProfileAsync(AppDbContext db, string userId = "user-1")
    {
        var user = new User { Id = userId, UserName = $"{userId}@example.com", Email = $"{userId}@example.com", DisplayName = "Andreas Georgiou" };
        var profile = new EmployeeProfile { Id = $"profile-{userId}", UserId = userId };
        db.Users.Add(user);
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    [Fact]
    public async Task Leave_holds_its_child_row_in_place()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        var profile = await SeedProfileAsync(db);

        var child = new Child { EmployeeProfileId = profile.Id, Name = "Andreas", DateOfBirth = new DateOnly(2019, 3, 4) };
        db.Children.Add(child);
        await db.SaveChangesAsync();

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = profile.UserId,
            EmployeeProfileId = profile.Id,
            ChildId = child.Id,
            StartDate = new DateTime(2026, 3, 2),
            EndDate = new DateTime(2026, 3, 6),
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        db.Children.Remove(child);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_profile_takes_its_children_with_it()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        var profile = await SeedProfileAsync(db);

        db.Children.Add(new Child { EmployeeProfileId = profile.Id, Name = "Maria", DateOfBirth = new DateOnly(2022, 9, 12) });
        await db.SaveChangesAsync();

        db.EmployeeProfiles.Remove(profile);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Children.ToListAsync());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~ChildCrudTests
```
Expected: build failure — `Child`, `AppDbContext.Children` and `AnnualLeave.ChildId` do not exist.

- [ ] **Step 3: Create the entity**

Create `Domain/Child.cs`:

```csharp
namespace Domain;

/// <summary>
/// One declared child of an employee. Children hang off the HR record rather than
/// the login because the entitlement they carry is an HR fact, and
/// <see cref="EmployeeProfile"/> is already where leave entitlement lives.
///
/// Stores a date of birth and nothing derived from it — see
/// <see cref="Services.PerChildLeaveCalculationService"/>. A child who has passed
/// the eligibility age is kept, not deleted: their approved leave is still history,
/// and the row is what the per-child ledger is queried by.
/// </summary>
public class Child
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string EmployeeProfileId { get; set; } = string.Empty;
    public EmployeeProfile? EmployeeProfile { get; set; }

    /// <summary>First name is enough to tell one employee's children apart in a picker.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Date only, like <see cref="User.DateOfBirth"/>. Age is never stored.</summary>
    public DateOnly DateOfBirth { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AnnualLeave> LeaveRequests { get; set; } = new List<AnnualLeave>();
}
```

- [ ] **Step 4: Add the entity members**

In `Domain/EmployeeProfile.cs`, after `public int LeaveBalance { get; set; }`:

```csharp
    /// <summary>
    /// Whether this employee has declared having children — a genuine tri-state:
    /// null means they have never been asked, false that they declared none, true
    /// that they have some. Deriving it from <see cref="Children"/> cannot tell
    /// "hasn't told us" from "has none", which is exactly the distinction the leave
    /// request form needs in order to say something useful.
    ///
    /// Invariant, enforced in the handlers: it cannot be saved false while
    /// <see cref="Children"/> is non-empty, and adding a child sets it true. The
    /// flag and the list therefore cannot disagree.
    /// </summary>
    public bool? HasChildren { get; set; }

    public ICollection<Child> Children { get; set; } = new List<Child>();
```

In `Domain/AnnualLeave.cs`, after the `LeaveType` navigation:

```csharp
    /// <summary>
    /// The child this leave was taken for, on a leave type with
    /// <see cref="LeaveType.PerChildEntitlement"/> set. Required for those types
    /// and refused for the rest.
    ///
    /// Nullable for one reason: paternity rows that predate the per-child
    /// entitlement have no child and must stay approved and visible. They count
    /// against no child's ledger — an admin can attach one later by editing.
    /// </summary>
    public string? ChildId { get; set; }
    public Child? Child { get; set; }
```

In `Domain/LeaveType.cs`, after `public int MaxCarryoverDays { get; set; }`:

```csharp
    /* Per-child entitlement. Paternity leave is not one budget per employee but one
       per child, bounded by the child's age -- so the three numbers that describe it
       sit here beside the allowance, the same move MoveCarryoverCapToLeaveType made
       for the carryover cap. When the toggle is false all three are ignored and the
       type behaves exactly as it did.

       Note the opposite hazard to EmployeeProfile.AnnualLeaveEntitlement, where a
       stored 0 disables the balance check outright: a 0 here refuses every request
       instead of waving them all through. The validator still refuses 0, but the
       safe direction is the default. */
    public bool PerChildEntitlement { get; set; }
    /// <summary>Lifetime entitlement per eligible child, in weeks. 18 for paternity leave.</summary>
    public int PerChildTotalWeeks { get; set; }
    /// <summary>Cap per leave year per eligible child, in weeks. 5 for paternity leave.</summary>
    public int PerChildWeeksPerYear { get; set; }
    /// <summary>The age at which a child stops being eligible. 15 for paternity leave.</summary>
    public int ChildEligibleUntilAge { get; set; }
```

- [ ] **Step 5: Configure persistence**

In `Persistence/AppDbContext.cs`, add the set beside the others:

```csharp
    public DbSet<Child> Children { get; set; }
```

Add a new configuration block in `OnModelCreating`, immediately before `builder.Entity<AuditLog>`:

```csharp
        builder.Entity<Child>(entity =>
        {
            entity.Property(c => c.Id).HasMaxLength(450).IsRequired();
            entity.Property(c => c.EmployeeProfileId).HasMaxLength(450).IsRequired();
            entity.Property(c => c.Name).HasMaxLength(100).IsRequired();
            entity.Property(c => c.DateOfBirth).IsRequired();

            entity.HasIndex(c => c.EmployeeProfileId);

            entity.HasOne(c => c.EmployeeProfile)
                .WithMany(ep => ep.Children)
                .HasForeignKey(c => c.EmployeeProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

Inside the existing `builder.Entity<AnnualLeave>` block, after the `Delegate` relationship:

```csharp
            // Restrict, not Cascade: a child's row carries the ledger proving how
            // much per-child leave was taken for them, so deleting the child must
            // not erase it. DeleteChild refuses while any leave references the row.
            entity.HasOne(al => al.Child)
                .WithMany(c => c.LeaveRequests)
                .HasForeignKey(al => al.ChildId)
                .OnDelete(DeleteBehavior.Restrict);

            // The per-child ledger totals approved leave for one child, all time and
            // per leave year, on every create, edit and approval — the same access
            // pattern the EmployeeId index above serves for the pooled balance.
            entity.HasIndex(al => new { al.ChildId, al.Status, al.StartDate, al.EndDate })
                .HasDatabaseName("IX_AnnualLeaves_ChildId_Status_StartDate_EndDate");
```

Inside the existing `builder.Entity<LeaveType>` block, after the `EligibilityScope` line:

```csharp
            entity.Property(lt => lt.PerChildEntitlement).HasDefaultValue(false);
            entity.Property(lt => lt.PerChildTotalWeeks).HasDefaultValue(0);
            entity.Property(lt => lt.PerChildWeeksPerYear).HasDefaultValue(0);
            entity.Property(lt => lt.ChildEligibleUntilAge).HasDefaultValue(0);
```

- [ ] **Step 6: Generate the migration**

```bash
export UseArtifactsOutput=true ArtifactsPath=C:/temp/wt-ef
dotnet restore Annualleave.sln
dotnet ef migrations add AddChildLeaveEntitlement --project Persistence --startup-project API
```

Open the generated file and confirm it contains: `CreateTable(name: "Children", ...)` with the `EmployeeProfileId` FK `onDelete: ReferentialAction.Cascade`; `AddColumn<bool>("HasChildren", "EmployeeProfiles", nullable: true)`; `AddColumn<string>("ChildId", "AnnualLeaves", maxLength: 450, nullable: true)` with FK `onDelete: ReferentialAction.Restrict`; four `AddColumn` calls on `LeaveTypes` with `defaultValue: false`/`0`; and the two indexes. If `HasChildren` came out non-nullable, the `bool?` on the entity was missed — fix and regenerate.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
Expected: the two `ChildCrudTests` pass, and the whole existing suite still passes (~713 tests). A failure in `MediatRHandlerRegistrationTests` or `ApiRouteTableFixture` here means something other than schema changed — investigate rather than editing those tests.

- [ ] **Step 8: Commit**

```bash
git add Domain/Child.cs Domain/EmployeeProfile.cs Domain/AnnualLeave.cs Domain/LeaveType.cs Persistence/AppDbContext.cs Persistence/Migrations Tests/WorkTrack.Tests/ChildCrudTests.cs
git commit -m "Add Child entity and per-child entitlement columns

A child stores a date of birth and nothing derived from it. AnnualLeave.ChildId
is nullable so paternity rows that predate this keep working, counting against
no child; the FK is Restrict so deleting a child cannot erase the ledger that
proves leave was taken for them."
```

---

### Task 3: Expose and validate the four leave-type columns

The columns exist; now an admin can read and set them. Note `GetLeaveTypeList` is a **hand-written projection** — a field missing there is silently absent from every client response.

**Files:**
- Modify: `Application/LeaveTypes/DTOs/UpsertLeaveTypeRequest.cs`, `Application/LeaveTypes/DTOs/LeaveTypeDto.cs`, `Application/LeaveTypes/Validators/UpsertLeaveTypeRequestValidator.cs`, `Application/LeaveTypes/Queries/GetLeaveTypeList.cs`
- Test: `Tests/WorkTrack.Tests/PaternityLeaveTypeConfigTests.cs`

**Interfaces:**
- Consumes: `LeaveType`'s four columns from Task 2.
- Produces: `UpsertLeaveTypeRequest` and `LeaveTypeDto` both carrying `PerChildEntitlement`, `PerChildTotalWeeks`, `PerChildWeeksPerYear`, `ChildEligibleUntilAge`. AutoMapper needs no change — `CreateMap<UpsertLeaveTypeRequest, LeaveType>` and `CreateMap<LeaveType, LeaveTypeDto>` map these by convention.

- [ ] **Step 1: Write the failing tests**

Create `Tests/WorkTrack.Tests/PaternityLeaveTypeConfigTests.cs`:

```csharp
using Application.LeaveTypes.DTOs;
using Application.LeaveTypes.Validators;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The three numbers describing a per-child entitlement live on the leave type,
/// beside the allowance -- the same reasoning that moved MaxCarryoverDays there.
///
/// Two rules are worth stating out loud. A 0 here is refused, but unlike
/// EmployeeProfile.AnnualLeaveEntitlement (where a stored 0 disables the balance
/// check outright) a 0 that slipped through would refuse every request rather than
/// permit every request. And a per-child type must not also affect the pooled
/// balance: it keeps its own ledger, and being counted in both would charge one
/// day of leave twice.
/// </summary>
public class PaternityLeaveTypeConfigTests
{
    private static UpsertLeaveTypeRequest Paternity(
        int totalWeeks = 18,
        int weeksPerYear = 5,
        int untilAge = 15,
        bool affectsBalance = false) => new()
    {
        Name = "Paternity Leave",
        RequiresApproval = true,
        IsActive = true,
        AffectsBalance = affectsBalance,
        PerChildEntitlement = true,
        PerChildTotalWeeks = totalWeeks,
        PerChildWeeksPerYear = weeksPerYear,
        ChildEligibleUntilAge = untilAge,
    };

    [Fact]
    public void The_configured_policy_is_valid()
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(261)]
    public void A_total_outside_the_allowed_range_is_rejected(int totalWeeks)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity(totalWeeks: totalWeeks));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.PerChildTotalWeeks));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public void A_yearly_cap_outside_the_allowed_range_is_rejected(int weeksPerYear)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity(weeksPerYear: weeksPerYear));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.PerChildWeeksPerYear));
    }

    [Fact]
    public void A_yearly_cap_above_the_total_is_rejected()
    {
        var result = new UpsertLeaveTypeRequestValidator()
            .Validate(Paternity(totalWeeks: 4, weeksPerYear: 5));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.PerChildWeeksPerYear));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void An_eligibility_age_outside_the_allowed_range_is_rejected(int untilAge)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity(untilAge: untilAge));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.ChildEligibleUntilAge));
    }

    [Fact]
    public void A_per_child_type_may_not_also_affect_the_pooled_balance()
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity(affectsBalance: true));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.AffectsBalance));
    }

    /// <summary>
    /// With the toggle off the three numbers describe nothing, so they must not be
    /// validated -- every existing leave type in the database has them at 0.
    /// </summary>
    [Fact]
    public void The_numbers_are_ignored_when_the_toggle_is_off()
    {
        var request = Paternity(totalWeeks: 0, weeksPerYear: 0, untilAge: 0);
        request.Name = "Annual Leave";
        request.PerChildEntitlement = false;
        request.AffectsBalance = true;
        request.DefaultAllowance = 25;

        var result = new UpsertLeaveTypeRequestValidator().Validate(request);

        Assert.True(result.IsValid);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PaternityLeaveTypeConfigTests
```
Expected: build failure — `UpsertLeaveTypeRequest` has no `PerChildEntitlement`.

- [ ] **Step 3: Add the DTO fields**

In `Application/LeaveTypes/DTOs/UpsertLeaveTypeRequest.cs`, after `MaxCarryoverDays`:

```csharp
    /// <summary>
    /// Turns the per-child entitlement engine on. The three numbers below are
    /// validated only when this is true — every other leave type leaves them at 0.
    /// </summary>
    public bool PerChildEntitlement { get; set; }

    [Range(0, 260)]
    public int PerChildTotalWeeks { get; set; }

    [Range(0, 52)]
    public int PerChildWeeksPerYear { get; set; }

    [Range(0, 30)]
    public int ChildEligibleUntilAge { get; set; }
```

In `Application/LeaveTypes/DTOs/LeaveTypeDto.cs`, after `MaxCarryoverDays`:

```csharp
    public bool PerChildEntitlement { get; set; }
    public int PerChildTotalWeeks { get; set; }
    public int PerChildWeeksPerYear { get; set; }
    public int ChildEligibleUntilAge { get; set; }
```

- [ ] **Step 4: Add the validation**

In `Application/LeaveTypes/Validators/UpsertLeaveTypeRequestValidator.cs`, at the end of the constructor:

```csharp
        /* Only when the toggle is on: with it off these three describe nothing, and
           every leave type already in the database has them at 0. */
        When(x => x.PerChildEntitlement, () =>
        {
            RuleFor(x => x.PerChildTotalWeeks)
                .InclusiveBetween(1, 260)
                .WithMessage("Total per child must be between 1 and 260 weeks.");

            RuleFor(x => x.PerChildWeeksPerYear)
                .InclusiveBetween(1, 52)
                .WithMessage("The yearly cap must be between 1 and 52 weeks.")
                .LessThanOrEqualTo(x => x.PerChildTotalWeeks)
                .WithMessage("The yearly cap cannot exceed the total per child.");

            RuleFor(x => x.ChildEligibleUntilAge)
                .InclusiveBetween(1, 30)
                .WithMessage("Children must stop being eligible between ages 1 and 30.");

            // A per-child type keeps its own ledger. Counted in the pooled balance
            // as well, one day of leave would be charged twice.
            RuleFor(x => x.AffectsBalance)
                .Equal(false)
                .WithMessage("A per-child leave type keeps its own ledger and must not also affect the pooled balance.");
        });
```

- [ ] **Step 5: Add the fields to the hand-written projection**

In `Application/LeaveTypes/Queries/GetLeaveTypeList.cs`, inside the `Select(lt => new LeaveTypeDto { ... })`, after `MaxCarryoverDays = lt.MaxCarryoverDays,`:

```csharp
                    PerChildEntitlement = lt.PerChildEntitlement,
                    PerChildTotalWeeks = lt.PerChildTotalWeeks,
                    PerChildWeeksPerYear = lt.PerChildWeeksPerYear,
                    ChildEligibleUntilAge = lt.ChildEligibleUntilAge,
```

This projection is written by hand, so a field left out here is absent from every client response even though the DTO declares it.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PaternityLeaveTypeConfigTests
```
Expected: PASS, 8 test methods.

- [ ] **Step 7: Commit**

```bash
git add Application/LeaveTypes Tests/WorkTrack.Tests/PaternityLeaveTypeConfigTests.cs
git commit -m "Expose and validate the per-child entitlement columns

Validated only when the toggle is on, since every existing leave type has the
three numbers at 0. A per-child type may not also affect the pooled balance --
counted in both, one day of leave would be charged twice."
```

---

### Task 4: Configure Paternity Leave as a data migration

The seeded row still says 14 days/event. It must be corrected by a **migration**, not by `DbInitializer`: `Seed:Enabled` is `false` in Production, so the seeder never runs on the deployed host and a seeder-only fix would silently skip it.

**Files:**
- Create: `Persistence/Migrations/<timestamp>_ConfigurePaternityPerChildEntitlement.cs` (generated, then hand-written body)
- Modify: `Persistence/DbInitializer.cs` (the seed list around line 774 and the `BackfillLeaveTypeDesignFields` map around line 822)
- Test: `Tests/WorkTrack.Tests/PaternityLeaveTypeConfigTests.cs` (add one case)

**Interfaces:**
- Consumes: the columns from Task 2.
- Produces: a `Paternity Leave` row with `PerChildEntitlement = 1`, `PerChildTotalWeeks = 18`, `PerChildWeeksPerYear = 5`, `ChildEligibleUntilAge = 15`, `AllowanceUnit = 'weeks/child'`, `DefaultAllowance = 0`, `MaxConsecutiveDays = 25`.

- [ ] **Step 1: Write the failing test**

Append to `Tests/WorkTrack.Tests/PaternityLeaveTypeConfigTests.cs` (add `using Domain;`, `using Persistence;`, `using Microsoft.EntityFrameworkCore;`):

```csharp
    /// <summary>
    /// A fresh database and a migrated one must agree. The migration corrects the
    /// deployed row; this holds the seeder's copy of the same figures, so a fresh
    /// clone does not come up with the old flat 14 days.
    /// </summary>
    [Fact]
    public async Task The_seeded_paternity_type_carries_the_per_child_policy()
    {
        await using var db = TestDb.Create();
        await DbInitializer.SeedLeaveTypesAsync(db);

        var paternity = await db.LeaveTypes.SingleAsync(lt => lt.Name == "Paternity Leave");

        Assert.True(paternity.PerChildEntitlement);
        Assert.Equal(18, paternity.PerChildTotalWeeks);
        Assert.Equal(5, paternity.PerChildWeeksPerYear);
        Assert.Equal(15, paternity.ChildEligibleUntilAge);
        Assert.Equal("weeks/child", paternity.AllowanceUnit);
        // 0 so nothing quotes the dead flat allowance; the per-child figures are
        // the only ones that describe this type now.
        Assert.Equal(0, paternity.DefaultAllowance);
        // Display-only, but it contradicted the 5-week annual cap at 14.
        Assert.Equal(25, paternity.MaxConsecutiveDays);
        Assert.False(paternity.AffectsBalance);
    }
```

If the leave-type seeding method in `DbInitializer` is private or named differently, make the smallest change that lets the test call it: mark it `internal static` and keep its name, adding `[assembly: InternalsVisibleTo("WorkTrack.Tests")]` only if the project does not already expose internals (check `Persistence/Persistence.csproj` and existing tests such as `SeedPolicyTests` for how they reach it — follow whatever they do rather than inventing a second mechanism).

- [ ] **Step 2: Run the test to verify it fails**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~The_seeded_paternity_type
```
Expected: FAIL — `Assert.True(paternity.PerChildEntitlement)` is false, the row still says `DefaultAllowance = 14`.

- [ ] **Step 3: Update the seeder in both places**

In `Persistence/DbInitializer.cs`, the seed list entry for Paternity Leave (around line 774) becomes:

```csharp
            new LeaveType
            {
                Name = "Paternity Leave", Icon = "👨‍👶", ColorKey = "paternity",
                Description = "Time off for a father around the birth of a child, and while that child is young.",
                RequiresApproval = true, IsActive = true, AffectsBalance = false, Paid = true,
                AttachmentPolicy = AttachmentPolicy.Required,
                // The flat allowance is dead: this type's budget is per child, not
                // per employee, so DefaultAllowance is 0 and the per-child figures
                // below are the ones every surface quotes.
                DefaultAllowance = 0, AllowanceUnit = "weeks/child",
                PerChildEntitlement = true,
                PerChildTotalWeeks = 18, PerChildWeeksPerYear = 5, ChildEligibleUntilAge = 15,
                AccrualNotes = "18 weeks per child · Max 5 weeks per child per year · Until the child turns 15",
                MinNoticeDays = 30, MaxConsecutiveDays = 25, HalfDayAllowed = false,
                EligibilityNotes = "Employees with children under 15", EligibilityScope = EligibilityScope.Limited
            },
```

And in the `BackfillLeaveTypeDesignFields` map (around line 822), replace the `["Paternity Leave"]` entry with:

```csharp
            ["Paternity Leave"] = new() { Icon = "👨‍👶", ColorKey = "paternity", Description = "Time off for a father around the birth of a child, and while that child is young.", Paid = true, AttachmentPolicy = AttachmentPolicy.Required, DefaultAllowance = 0, AllowanceUnit = "weeks/child", PerChildEntitlement = true, PerChildTotalWeeks = 18, PerChildWeeksPerYear = 5, ChildEligibleUntilAge = 15, AccrualNotes = "18 weeks per child · Max 5 weeks per child per year · Until the child turns 15", MinNoticeDays = 30, MaxConsecutiveDays = 25, HalfDayAllowed = false, EligibilityNotes = "Employees with children under 15", EligibilityScope = EligibilityScope.Limited },
```

Then extend the copy loop below it (the `foreach (var row in rows)` block) with the four new fields, beside the existing `row.DefaultAllowance = preset.DefaultAllowance;` line:

```csharp
            row.PerChildEntitlement = preset.PerChildEntitlement;
            row.PerChildTotalWeeks = preset.PerChildTotalWeeks;
            row.PerChildWeeksPerYear = preset.PerChildWeeksPerYear;
            row.ChildEligibleUntilAge = preset.ChildEligibleUntilAge;
```

Note the loop's guard is `looksEmpty = string.IsNullOrEmpty(row.Description) && row.DefaultAllowance == 0` — it only fills rows still on schema defaults, so it will not touch a configured Paternity row. That is why the migration in Step 4 exists and is not optional.

- [ ] **Step 4: Write the data migration**

```bash
export UseArtifactsOutput=true ArtifactsPath=C:/temp/wt-ef
dotnet restore Annualleave.sln
dotnet ef migrations add ConfigurePaternityPerChildEntitlement --project Persistence --startup-project API
```

The generated `Up`/`Body` will be empty (no schema change). Replace the class body with:

```csharp
        /// <summary>
        /// Paternity Leave was seeded as a flat 14 days/event, which cannot express
        /// an entitlement that is per child and bounded by the child's age. This
        /// moves the deployed row onto the per-child policy.
        ///
        /// A migration rather than a DbInitializer change on purpose: Seed:Enabled
        /// is false in Production, so the seeder never runs on the deployed host
        /// and a seeder-only fix would silently skip it.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [LeaveTypes]
                SET [PerChildEntitlement]   = 1,
                    [PerChildTotalWeeks]    = 18,
                    [PerChildWeeksPerYear]  = 5,
                    [ChildEligibleUntilAge] = 15,
                    [AllowanceUnit]         = 'weeks/child',
                    -- 0 so no surface quotes the dead flat allowance.
                    [DefaultAllowance]      = 0,
                    -- Display-only, but 14 contradicted the 5-week annual cap.
                    [MaxConsecutiveDays]    = 25,
                    [AccrualNotes]          = '18 weeks per child · Max 5 weeks per child per year · Until the child turns 15',
                    [EligibilityNotes]      = 'Employees with children under 15'
                WHERE [Name] = 'Paternity Leave';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [LeaveTypes]
                SET [PerChildEntitlement]   = 0,
                    [PerChildTotalWeeks]    = 0,
                    [PerChildWeeksPerYear]  = 0,
                    [ChildEligibleUntilAge] = 0,
                    [AllowanceUnit]         = 'days/event',
                    [DefaultAllowance]      = 14,
                    [MaxConsecutiveDays]    = 14,
                    [AccrualNotes]          = 'Granted per event · Once per child',
                    [EligibilityNotes]      = 'Male employees'
                WHERE [Name] = 'Paternity Leave';
                """);
        }
```

Leave the generated `Designer.cs` alone — the model snapshot is unchanged because this migration touches no schema.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
Expected: the new case passes and the full suite still passes. Watch `SeedDemoDataTeardownTests` and `SeedPolicyTests` in particular — both read the seeder.

- [ ] **Step 6: Commit**

```bash
git add Persistence/Migrations Persistence/DbInitializer.cs Tests/WorkTrack.Tests/PaternityLeaveTypeConfigTests.cs
git commit -m "Configure Paternity Leave as a per-child entitlement

18 weeks per child, 5 per child per year, until the child turns 15. Written as
a data migration because Seed:Enabled is false in Production -- the seeder never
runs on the deployed host. The seeder's copy is updated to match so a fresh
clone agrees with a migrated database."
```

---

### Task 5: Extract the shared leave-year DB helpers

A pure refactor, taken before the second calculator is written so there is only ever one copy of the holiday query. No behaviour changes; the existing suite is the test.

**Files:**
- Create: `Application/AnnualLeaves/Commands/LeaveYearQueries.cs`
- Modify: `Application/AnnualLeaves/Commands/AnnualLeaveBalanceCalculator.cs` (delete two private methods, call the new class)

**Interfaces:**
- Consumes: `AppDbContext`, `LeaveCalculationService`.
- Produces: `internal static class LeaveYearQueries` with `Task<HashSet<DateTime>> GetHolidaySetAsync(AppDbContext context, DateTime rangeStart, DateTime rangeEnd, CancellationToken cancellationToken)` and `Task<int> GetLeaveYearStartMonthAsync(AppDbContext context, CancellationToken cancellationToken)`.

- [ ] **Step 1: Create the shared helper**

Create `Application/AnnualLeaves/Commands/LeaveYearQueries.cs` by moving the two private methods out of `AnnualLeaveBalanceCalculator` verbatim:

```csharp
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// The database reads both leave-balance calculators need: the configured leave-year
/// start month, and the public holidays covering a date range.
///
/// Extracted rather than copied. Two calculators enforce leave budgets now — the
/// pooled annual-leave balance and the per-child entitlement — and a second copy of
/// the holiday query would diverge the first time the country-code handling changes,
/// with the two answers differing by exactly the days that matter.
/// </summary>
internal static class LeaveYearQueries
{
    /// <summary>
    /// Public holidays for the configured country falling inside the range, as
    /// dates. Empty when no country is configured, which makes every weekday a
    /// business day.
    /// </summary>
    public static async Task<HashSet<DateTime>> GetHolidaySetAsync(
        AppDbContext context, DateTime rangeStart, DateTime rangeEnd, CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var code = settings?.HolidayCountryCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            return [];

        var startDate = rangeStart.Date;
        var endDate = rangeEnd.Date;

        var dates = await context.PublicHolidays
            .AsNoTracking()
            .Where(h => h.CountryCode == code && h.Date >= startDate && h.Date <= endDate)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);

        return dates.Select(d => d.Date).ToHashSet();
    }

    /// <summary>The month a leave year starts in. 1 (January) when unset.</summary>
    public static async Task<int> GetLeaveYearStartMonthAsync(
        AppDbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        return settings?.LeaveYearStartMonth ?? 1;
    }
}
```

- [ ] **Step 2: Point the existing calculator at it**

In `Application/AnnualLeaves/Commands/AnnualLeaveBalanceCalculator.cs`, delete the private `GetHolidaySetAsync` and `GetLeaveYearStartMonthAsync` methods, and replace their four call sites with `LeaveYearQueries.GetHolidaySetAsync(...)` and `LeaveYearQueries.GetLeaveYearStartMonthAsync(...)`. The call sites are in `CheckSufficientBalanceAsync` (two), `SyncCurrentYearBalanceAsync` (one) and `GetApprovedDaysForLeaveYearAsync` (one). Leave the `// ── DB helpers ─────` comment banner with whatever remains under it, and keep `AffectsBalanceAsync` and `GetApprovedDaysForLeaveYearAsync` where they are — they are specific to the pooled balance.

- [ ] **Step 3: Run the full suite to verify nothing moved**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
Expected: PASS, the same count as before this task. `LeaveBalanceTests`, `LeaveBalanceAtomicityTests`, `LeaveTypeAutoApprovalBalanceTests` and `AllowanceGovernsEveryoneTests` all exercise these paths — a failure in any of them means the move was not verbatim.

- [ ] **Step 4: Commit**

```bash
git add Application/AnnualLeaves/Commands/LeaveYearQueries.cs Application/AnnualLeaves/Commands/AnnualLeaveBalanceCalculator.cs
git commit -m "Extract the shared leave-year database helpers

Two calculators are about to need the holiday set and the leave-year start
month. Copying the holiday query would diverge the first time the country-code
handling changes, and the two answers would differ by exactly the days that
matter."
```

---

### Task 6: The calculator — type, ownership and eligibility

The first four checks. Caps arrive in Task 7, so this task's calculator returns `null` once it has established that the child exists, belongs to the employee, and is eligible for the whole request.

**Files:**
- Create: `Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs`
- Create: `Tests/WorkTrack.Tests/PerChildLeaveWorld.cs` (shared test fixture, used by Tasks 6, 7 and 8)
- Create: `Tests/WorkTrack.Tests/PerChildLeaveEntitlementTests.cs`

**Interfaces:**
- Consumes: `PerChildLeaveCalculationService` (Task 1), `LeaveYearQueries` (Task 5), `Child`/`AnnualLeave.ChildId`/`LeaveType.PerChild*` (Task 2).
- Produces: `internal static class PerChildLeaveBalanceCalculator` with
  `Task<string?> CheckPerChildEntitlementAsync(AppDbContext context, AnnualLeave annualLeave, EmployeeProfile employeeProfile, string? excludeLeaveId, CancellationToken cancellationToken)` — returns a human-readable refusal, or `null` when the request is allowed. Never throws for a business rule.
- Produces: `internal static class PerChildLeaveWorld` with `Task<AppDbContext> CreateAsync(int leaveYearStartMonth = 1)`, `Task<Child> AddChildAsync(AppDbContext db, string name, DateOnly dateOfBirth)`, `Task ApproveLeaveAsync(AppDbContext db, string childId, DateTime start, DateTime end)`, `AnnualLeave Request(string? childId, DateTime start, DateTime end, int? leaveTypeId = null)`, `Task<string?> CheckAsync(AppDbContext db, AnnualLeave leave, string? excludeLeaveId = null)`, and the constants `UserId`, `ProfileId`, `PaternityTypeId`, `AnnualLeaveTypeId`.

- [ ] **Step 1: Write the shared test fixture**

Create `Tests/WorkTrack.Tests/PerChildLeaveWorld.cs`:

```csharp
using Application.AnnualLeaves.Commands;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace WorkTrack.Tests;

/// <summary>
/// One employee, one paternity leave type configured 18/5/15, and one ordinary
/// annual-leave type to prove the per-child rules stay off for everything else.
///
/// TestDb (EF in-memory) is the right provider here: these tests turn on the
/// calculator's arithmetic, not on constraints or transactions. ChildCrudTests uses
/// TransactionalTestDb for the parts that do.
/// </summary>
internal static class PerChildLeaveWorld
{
    public const string UserId = "employee-1";
    public const string ProfileId = "profile-1";
    public const int PaternityTypeId = 1;
    public const int AnnualLeaveTypeId = 2;

    public static async Task<AppDbContext> CreateAsync(int leaveYearStartMonth = 1)
    {
        var db = TestDb.Create();

        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = leaveYearStartMonth });

        db.Users.Add(new User
        {
            Id = UserId,
            UserName = "employee-1@example.com",
            Email = "employee-1@example.com",
            DisplayName = "Andreas Georgiou",
        });

        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ProfileId,
            UserId = UserId,
            HasChildren = true,
            // Set so the pooled annual-leave check is live too: a per-child type
            // must not be charged against it, and a 0 here would hide that by
            // switching the pooled check off entirely.
            AnnualLeaveEntitlement = 25,
            LeaveBalance = 25,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = PaternityTypeId,
            Name = "Paternity Leave",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = false,
            DefaultAllowance = 0,
            AllowanceUnit = "weeks/child",
            PerChildEntitlement = true,
            PerChildTotalWeeks = 18,
            PerChildWeeksPerYear = 5,
            ChildEligibleUntilAge = 15,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = AnnualLeaveTypeId,
            Name = "Annual Leave",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = true,
            DefaultAllowance = 25,
        });

        await db.SaveChangesAsync();
        return db;
    }

    public static async Task<Child> AddChildAsync(AppDbContext db, string name, DateOnly dateOfBirth)
    {
        var child = new Child { EmployeeProfileId = ProfileId, Name = name, DateOfBirth = dateOfBirth };
        db.Children.Add(child);
        await db.SaveChangesAsync();
        return child;
    }

    /// <summary>Approved paternity leave already on the record for this child.</summary>
    public static async Task ApproveLeaveAsync(AppDbContext db, string childId, DateTime start, DateTime end)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = UserId,
            EmployeeProfileId = ProfileId,
            ChildId = childId,
            LeaveTypeId = PaternityTypeId,
            StartDate = start,
            EndDate = end,
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>An unsaved request, as a handler holds it before writing.</summary>
    public static AnnualLeave Request(string? childId, DateTime start, DateTime end, int? leaveTypeId = null) => new()
    {
        EmployeeId = UserId,
        EmployeeProfileId = ProfileId,
        ChildId = childId,
        LeaveTypeId = leaveTypeId ?? PaternityTypeId,
        StartDate = start,
        EndDate = end,
        Reason = "Paternity",
        Status = AnnualLeaveStatus.Pending,
    };

    public static async Task<string?> CheckAsync(AppDbContext db, AnnualLeave leave, string? excludeLeaveId = null)
    {
        var profile = await db.EmployeeProfiles.SingleAsync(ep => ep.Id == ProfileId);
        return await PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync(
            db, leave, profile, excludeLeaveId, CancellationToken.None);
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `Tests/WorkTrack.Tests/PerChildLeaveEntitlementTests.cs`:

```csharp
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Who a paternity request may be for. The caps are in PerChildLeaveCapTests; this
/// covers the four questions asked before them — is this even a per-child type, was
/// a child named, is that child this employee's, and is the child still eligible for
/// every day of the request.
///
/// The eligibility question is asked of the request's END date, so a request is
/// never part-eligible: an employee cannot start leave the day before the 15th
/// birthday and run five weeks past it.
/// </summary>
public class PerChildLeaveEntitlementTests
{
    // Andreas turns 15 on 04 Mar 2026.
    private static readonly DateOnly AndreasDob = new(2011, 3, 4);

    [Fact]
    public async Task An_ordinary_leave_type_is_left_alone()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        var request = PerChildLeaveWorld.Request(
            childId: null,
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 5),
            leaveTypeId: PerChildLeaveWorld.AnnualLeaveTypeId);

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }

    [Fact]
    public async Task A_per_child_type_requires_a_child()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        var request = PerChildLeaveWorld.Request(childId: null, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));

        var error = await PerChildLeaveWorld.CheckAsync(db, request);

        Assert.Equal("Select the child this Paternity Leave is for.", error);
    }

    [Fact]
    public async Task An_unknown_child_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        var request = PerChildLeaveWorld.Request("no-such-child", new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));

        Assert.Equal("The selected child is not on your profile.", await PerChildLeaveWorld.CheckAsync(db, request));
    }

    /// <summary>
    /// The child id arrives from the client — including on an admin's on-behalf
    /// request, where the employee and the child are both chosen in the browser.
    /// Without this check one employee could spend another's entitlement.
    /// </summary>
    [Fact]
    public async Task Another_employees_child_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "profile-2", UserId = "employee-2" });
        var otherChild = new Child { EmployeeProfileId = "profile-2", Name = "Someone else's", DateOfBirth = AndreasDob };
        db.Children.Add(otherChild);
        await db.SaveChangesAsync();

        var request = PerChildLeaveWorld.Request(otherChild.Id, new DateTime(2026, 1, 5), new DateTime(2026, 1, 9));

        Assert.Equal("The selected child is not on your profile.", await PerChildLeaveWorld.CheckAsync(db, request));
    }

    [Fact]
    public async Task An_eligible_child_passes()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", AndreasDob);

        // Mon 02 Mar - Tue 03 Mar 2026, both before the birthday on the 4th.
        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 3, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }

    [Fact]
    public async Task A_request_that_straddles_the_fifteenth_birthday_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", AndreasDob);

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 3, 10));

        var error = await PerChildLeaveWorld.CheckAsync(db, request);

        Assert.Equal(
            "Andreas turns 15 on 04 Mar 2026 — Paternity Leave for this child must end on or before 03 Mar 2026.",
            error);
    }

    [Fact]
    public async Task A_request_after_the_fifteenth_birthday_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", AndreasDob);

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));

        var error = await PerChildLeaveWorld.CheckAsync(db, request);

        Assert.NotNull(error);
        Assert.Contains("must end on or before 03 Mar 2026", error);
    }

    /// <summary>
    /// The last eligible day is bookable. An off-by-one here silently costs an
    /// employee a day of entitlement on the boundary that matters most.
    /// </summary>
    [Fact]
    public async Task The_day_before_the_birthday_is_still_bookable()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", AndreasDob);

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 3), new DateTime(2026, 3, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PerChildLeaveEntitlementTests
```
Expected: build failure — `PerChildLeaveBalanceCalculator` does not exist.

- [ ] **Step 4: Write the calculator**

Create `Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs`:

```csharp
using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// Enforces a per-child leave entitlement — paternity leave in practice: so many
/// weeks per child, only while that child is under a given age, and only so many of
/// them in any one leave year.
///
/// A sibling of <see cref="AnnualLeaveBalanceCalculator"/> and deliberately the same
/// shape: it loads its inputs from the DbContext, delegates every calculation to the
/// domain services, and returns a human-readable message rather than throwing, so a
/// handler can map it straight to <c>Result&lt;T&gt;.Failure</c>.
///
/// This is a second ledger, distinct from the pooled <c>EmployeeProfile.LeaveBalance</c>
/// the other calculator keeps. The two must never both apply to one leave type —
/// <c>UpsertLeaveTypeRequestValidator</c> refuses a type that sets both
/// <c>PerChildEntitlement</c> and <c>AffectsBalance</c> — or one day of leave would
/// be charged twice.
///
/// Nothing is stored: the ledger is a projection over approved leave rows, which is
/// why a child aging out needs no recalculation step. Their usage stays in history
/// and their remaining entitlement is simply gone.
/// </summary>
internal static class PerChildLeaveBalanceCalculator
{
    /// <summary>
    /// Returns a human-readable error when the request breaks the per-child rules,
    /// or <c>null</c> when it is allowed — including when the leave type has no
    /// per-child entitlement, in which case there is nothing to enforce.
    /// </summary>
    public static async Task<string?> CheckPerChildEntitlementAsync(
        AppDbContext context,
        AnnualLeave annualLeave,
        EmployeeProfile employeeProfile,
        string? excludeLeaveId,
        CancellationToken cancellationToken)
    {
        var leaveType = annualLeave.LeaveTypeId.HasValue
            ? await context.LeaveTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(lt => lt.Id == annualLeave.LeaveTypeId.Value, cancellationToken)
            : null;

        if (leaveType is null || !leaveType.PerChildEntitlement)
            return null;

        if (string.IsNullOrWhiteSpace(annualLeave.ChildId))
            return $"Select the child this {leaveType.Name} is for.";

        var childId = annualLeave.ChildId;
        var child = await context.Children
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == childId, cancellationToken);

        // One message for "no such child" and "not yours" on purpose: telling the
        // caller which of the two it was would confirm the existence of another
        // employee's record.
        if (child is null || child.EmployeeProfileId != employeeProfile.Id)
            return "The selected child is not on your profile.";

        var lastEligibleDate = PerChildLeaveCalculationService.LastEligibleDate(
            child.DateOfBirth, leaveType.ChildEligibleUntilAge);

        // Asked of the END date, so no request is ever part-eligible. This single
        // check covers a request that starts after the birthday and one that
        // straddles it.
        if (DateOnly.FromDateTime(annualLeave.EndDate.Date) > lastEligibleDate)
        {
            var birthday = lastEligibleDate.AddDays(1);
            return $"{child.Name} turns {leaveType.ChildEligibleUntilAge} on {birthday:dd MMM yyyy} — " +
                $"{leaveType.Name} for this child must end on or before {lastEligibleDate:dd MMM yyyy}.";
        }

        return null;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PerChildLeaveEntitlementTests
```
Expected: PASS, 8 tests. If the two message assertions fail on the dash character, the message must use an em dash (—), matching the code above — copy it rather than retyping.

- [ ] **Step 6: Commit**

```bash
git add Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs Tests/WorkTrack.Tests/PerChildLeaveWorld.cs Tests/WorkTrack.Tests/PerChildLeaveEntitlementTests.cs
git commit -m "Enforce child ownership and eligibility on per-child leave

Eligibility is asked of the request's end date, so a request is never
part-eligible. A child id that is not on the caller's profile is refused with
the same message as an unknown one -- distinguishing them would confirm another
employee's record exists."
```

---

### Task 7: The caps — 18 weeks per child, 5 weeks per child per year

**Files:**
- Modify: `Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs`
- Create: `Tests/WorkTrack.Tests/PerChildLeaveCapTests.cs`

**Interfaces:**
- Consumes: everything from Task 6, plus `LeaveCalculationService.GetCoveredLeaveYears`, `.CalculateBusinessDaysInLeaveYear`, `.GetLeaveYearBounds`, `.CalculateBusinessDays`.
- Produces: no new public members — `CheckPerChildEntitlementAsync` gains checks 5 and 6.

- [ ] **Step 1: Write the failing tests**

Create `Tests/WorkTrack.Tests/PerChildLeaveCapTests.cs`. The first two tests are the worked examples from the requirements, verbatim.

```csharp
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The two caps: 18 weeks (90 business days) per child for their whole eligible
/// life, and 5 weeks (25 business days) per child per leave year.
///
/// A week is five business days, so weekends and public holidays inside a request
/// do not consume entitlement. Only Approved leave counts as used — the same rule
/// the pooled annual-leave balance already applies.
/// </summary>
public class PerChildLeaveCapTests
{
    // Born 2019: under 15 for every date in these tests.
    private static readonly DateOnly YoungChild = new(2019, 3, 4);

    /// <summary>
    /// Requirement example 1. Five weeks in one leave year, two in the next, three
    /// in the one after: 10 of 18 weeks used, 8 (40 business days) left.
    /// </summary>
    [Fact]
    public async Task Example_one_tracks_what_is_left_across_leave_years()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // Year 1: 5 weeks (25 business days), Mon 05 Jan - Fri 06 Feb 2026.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        // Year 2: 2 weeks (10 business days), Mon 04 Jan - Fri 15 Jan 2027.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2027, 1, 4), new DateTime(2027, 1, 15));
        // Year 3: 3 weeks (15 business days), Mon 03 Jan - Fri 21 Jan 2028.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2028, 1, 3), new DateTime(2028, 1, 21));

        // Year 4: 8 weeks would be 40 days -- exactly what is left, but more than
        // the 5-week yearly cap allows, so it must fail on the yearly cap.
        var tooMuchForOneYear = PerChildLeaveWorld.Request(child.Id, new DateTime(2029, 1, 1), new DateTime(2029, 2, 23));
        var yearlyError = await PerChildLeaveWorld.CheckAsync(db, tooMuchForOneYear);
        Assert.NotNull(yearlyError);
        Assert.Contains("left for the leave year", yearlyError);

        // 5 weeks in year 4 is within both caps: 40 days remain in total.
        var withinBoth = PerChildLeaveWorld.Request(child.Id, new DateTime(2029, 1, 1), new DateTime(2029, 2, 2));
        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, withinBoth));
    }

    /// <summary>
    /// The lifetime cap, reached without ever breaking the yearly one: 18 weeks
    /// spread over four leave years, then one more day refused.
    /// </summary>
    [Fact]
    public async Task The_eighteen_week_total_is_the_hard_limit()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 25 + 25 + 25 + 15 = 90 business days = 18 weeks.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2027, 1, 4), new DateTime(2027, 2, 5));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2028, 1, 3), new DateTime(2028, 2, 4));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2029, 1, 1), new DateTime(2029, 1, 19));

        var oneMoreDay = PerChildLeaveWorld.Request(child.Id, new DateTime(2030, 1, 7), new DateTime(2030, 1, 7));

        var error = await PerChildLeaveWorld.CheckAsync(db, oneMoreDay);

        Assert.NotNull(error);
        Assert.Contains("remaining in total", error);
        Assert.Contains("0 day(s)", error);
    }

    /// <summary>
    /// Requirement example 2. Three children are three separate ledgers: 5 weeks
    /// each in the same leave year is fine (15 weeks in total), a 6th week for any
    /// one of them is not. The cap is per child, never pooled.
    /// </summary>
    [Fact]
    public async Task Example_two_gives_each_child_their_own_ledger()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var first = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));
        var second = await PerChildLeaveWorld.AddChildAsync(db, "Maria", new DateOnly(2021, 6, 15));
        var third = await PerChildLeaveWorld.AddChildAsync(db, "Petros", new DateOnly(2023, 9, 1));

        // 5 weeks for each child in the same leave year: 15 weeks for the employee.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, first.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, second.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, third.Id, new DateTime(2026, 5, 4), new DateTime(2026, 6, 5));

        // A 6th week for the first child in the same year: refused.
        var sixthWeek = PerChildLeaveWorld.Request(first.Id, new DateTime(2026, 9, 7), new DateTime(2026, 9, 11));
        Assert.NotNull(await PerChildLeaveWorld.CheckAsync(db, sixthWeek));

        // The next leave year resets all three.
        var nextYear = PerChildLeaveWorld.Request(first.Id, new DateTime(2027, 1, 4), new DateTime(2027, 1, 8));
        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, nextYear));
    }

    /// <summary>
    /// One child's usage must not touch another's. Without a per-child predicate on
    /// the usage query, three children's leave would total against whichever child
    /// was asked about.
    /// </summary>
    [Fact]
    public async Task One_childs_leave_does_not_charge_another()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var first = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));
        var second = await PerChildLeaveWorld.AddChildAsync(db, "Maria", new DateOnly(2021, 6, 15));

        await PerChildLeaveWorld.ApproveLeaveAsync(db, first.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        var forSecond = PerChildLeaveWorld.Request(second.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, forSecond));
    }

    /// <summary>
    /// The yearly cap is checked against every leave year the request touches, which
    /// is what stops 10 weeks arriving as one request split over new year.
    /// </summary>
    [Fact]
    public async Task A_request_across_the_year_boundary_is_capped_in_both_years()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 30 Nov 2026 - 05 Feb 2027: about 5 weeks in 2026 and 5 in 2027, which is
        // 10 weeks and inside the 18-week total, but the yearly cap applies to each
        // year separately and 2026's share alone exceeds it.
        var straddling = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 11, 30), new DateTime(2027, 2, 5));

        var error = await PerChildLeaveWorld.CheckAsync(db, straddling);

        Assert.NotNull(error);
        Assert.Contains("left for the leave year", error);
    }

    [Fact]
    public async Task Five_weeks_either_side_of_the_boundary_is_allowed()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 25 business days ending 31 Dec 2026, then 25 starting 04 Jan 2027.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 11, 26), new DateTime(2026, 12, 31));

        var newYear = PerChildLeaveWorld.Request(child.Id, new DateTime(2027, 1, 4), new DateTime(2027, 2, 5));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, newYear));
    }

    /// <summary>
    /// A leave year that runs April-March moves the reset with it: the same two
    /// requests that were in different years above now fall inside one.
    /// </summary>
    [Fact]
    public async Task The_year_follows_the_configured_leave_year()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync(leaveYearStartMonth: 4);
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 11, 26), new DateTime(2026, 12, 31));

        // Both sit inside the Apr 2026 - Mar 2027 leave year, so the second breaks
        // the 5-week cap where under a calendar year it would not.
        var sameLeaveYear = PerChildLeaveWorld.Request(child.Id, new DateTime(2027, 1, 4), new DateTime(2027, 2, 5));

        Assert.NotNull(await PerChildLeaveWorld.CheckAsync(db, sameLeaveYear));
    }

    /// <summary>
    /// Weekends are free. A five-week calendar span is 25 business days, not 35 —
    /// the whole reason a "week" is defined as five business days.
    /// </summary>
    [Fact]
    public async Task Weekends_do_not_consume_entitlement()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // Mon 05 Jan - Fri 06 Feb 2026 is 33 calendar days but exactly 25 business
        // days, so it fits the 5-week yearly cap precisely.
        var exactlyFiveWeeks = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, exactlyFiveWeeks));
    }

    [Fact]
    public async Task Public_holidays_do_not_consume_entitlement()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var settings = await db.AppSettings.FirstAsync();
        settings.HolidayCountryCode = "CY";
        db.PublicHolidays.Add(new PublicHoliday
        {
            CountryCode = "CY",
            Year = 2026,
            Date = new DateTime(2026, 1, 6),
            LocalName = "Epiphany",
            EnglishName = "Epiphany",
        });
        await db.SaveChangesAsync();

        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 25 business days already approved minus the holiday leaves one day spare.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        var oneMoreDay = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 3, 2));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, oneMoreDay));
    }

    [Theory]
    [InlineData(AnnualLeaveStatus.Pending)]
    [InlineData(AnnualLeaveStatus.Rejected)]
    [InlineData(AnnualLeaveStatus.Cancelled)]
    public async Task Only_approved_leave_counts_as_used(AnnualLeaveStatus status)
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = PerChildLeaveWorld.UserId,
            EmployeeProfileId = PerChildLeaveWorld.ProfileId,
            ChildId = child.Id,
            LeaveTypeId = PerChildLeaveWorld.PaternityTypeId,
            StartDate = new DateTime(2026, 1, 5),
            EndDate = new DateTime(2026, 2, 6),
            Reason = "Paternity",
            Status = status,
        });
        await db.SaveChangesAsync();

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }

    /// <summary>
    /// Editing a request must not count that request against itself, or nudging a
    /// date on an approved five-week request would refuse it as a sixth week.
    /// </summary>
    [Fact]
    public async Task An_edited_request_does_not_count_against_itself()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        var existing = await db.AnnualLeaves.FirstAsync();

        var edited = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 5));
        edited.Id = existing.Id;

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, edited, excludeLeaveId: existing.Id));
    }

    /// <summary>
    /// Paternity rows that predate the per-child entitlement have no child, so they
    /// belong to no ledger. They stay approved and visible, and an admin can attach
    /// a child later by editing them.
    /// </summary>
    [Fact]
    public async Task Legacy_leave_with_no_child_charges_nobody()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = PerChildLeaveWorld.UserId,
            EmployeeProfileId = PerChildLeaveWorld.ProfileId,
            ChildId = null,
            LeaveTypeId = PerChildLeaveWorld.PaternityTypeId,
            StartDate = new DateTime(2025, 1, 6),
            EndDate = new DateTime(2025, 2, 7),
            Reason = "Paternity (before children were declared)",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PerChildLeaveCapTests
```
Expected: FAIL — every test that asserts a refusal returns `null`, because no cap is enforced yet. The tests asserting `null` already pass; that is fine.

- [ ] **Step 3: Add the two caps**

In `Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs`, replace the closing `return null;` of `CheckPerChildEntitlementAsync` with:

```csharp
        var startMonth = await LeaveYearQueries.GetLeaveYearStartMonthAsync(context, cancellationToken);
        var requestHolidays = await LeaveYearQueries.GetHolidaySetAsync(
            context, annualLeave.StartDate, annualLeave.EndDate, cancellationToken);

        var requestedDays = LeaveCalculationService.CalculateBusinessDays(
            annualLeave.StartDate, annualLeave.EndDate, requestHolidays);

        // A range made entirely of weekends and holidays charges nothing, so there
        // is no cap left to break.
        if (requestedDays <= 0)
            return null;

        var approved = await ApprovedLeaveForChildAsync(context, childId, excludeLeaveId, cancellationToken);

        // ── Lifetime cap ───────────────────────────────────────────────────────
        var totalDays = PerChildLeaveCalculationService.WeeksToBusinessDays(leaveType.PerChildTotalWeeks);
        var usedDays = await UsedBusinessDaysAsync(context, approved, cancellationToken);
        var remainingDays = PerChildLeaveCalculationService.RemainingDays(totalDays, usedDays);

        if (remainingDays < requestedDays)
        {
            return $"{child.Name} has {remainingDays} day(s) " +
                $"({PerChildLeaveCalculationService.BusinessDaysToWeeks(remainingDays)} week(s)) of " +
                $"{leaveType.Name} remaining in total. This request is {requestedDays} day(s).";
        }

        // ── Yearly cap, per leave year the request touches ─────────────────────
        // Checked year by year rather than in total: that is what stops ten weeks
        // arriving as one request straddling new year.
        var yearCapDays = PerChildLeaveCalculationService.WeeksToBusinessDays(leaveType.PerChildWeeksPerYear);

        foreach (var leaveYearKey in LeaveCalculationService.GetCoveredLeaveYears(
                     annualLeave.StartDate, annualLeave.EndDate, startMonth))
        {
            var requestedInYear = LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
                annualLeave.StartDate, annualLeave.EndDate, leaveYearKey, startMonth, requestHolidays);
            if (requestedInYear <= 0)
                continue;

            var (lyStart, lyEnd) = LeaveCalculationService.GetLeaveYearBounds(leaveYearKey, startMonth);
            var yearHolidays = await LeaveYearQueries.GetHolidaySetAsync(context, lyStart, lyEnd, cancellationToken);

            var usedInYear = approved.Sum(leave => LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
                leave.StartDate, leave.EndDate, leaveYearKey, startMonth, yearHolidays));

            var remainingInYear = PerChildLeaveCalculationService.RemainingDays(yearCapDays, usedInYear);
            if (remainingInYear < requestedInYear)
            {
                return $"{child.Name} has {remainingInYear} day(s) " +
                    $"({PerChildLeaveCalculationService.BusinessDaysToWeeks(remainingInYear)} week(s)) of " +
                    $"{leaveType.Name} left for the leave year {lyStart:dd MMM yyyy} – {lyEnd:dd MMM yyyy}. " +
                    $"This request uses {requestedInYear} day(s) in that year.";
            }
        }

        return null;
    }

    // ── DB helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Approved leave for one child. Only Approved counts — the same rule the pooled
    /// balance applies — and rows with a null ChildId are excluded by construction,
    /// since this is queried by child.
    /// </summary>
    private static Task<List<AnnualLeave>> ApprovedLeaveForChildAsync(
        AppDbContext context,
        string childId,
        string? excludeLeaveId,
        CancellationToken cancellationToken)
        => context.AnnualLeaves
            .AsNoTracking()
            .Where(leave =>
                leave.ChildId == childId
                && leave.Status == AnnualLeaveStatus.Approved
                && (excludeLeaveId == null || leave.Id != excludeLeaveId))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Business days across every approved leave, holiday-aware. One holiday query
    /// spanning the whole set rather than one per row: the set is small and the
    /// dates are sparse, so the range read costs less than N round trips.
    /// </summary>
    private static async Task<int> UsedBusinessDaysAsync(
        AppDbContext context,
        List<AnnualLeave> approved,
        CancellationToken cancellationToken)
    {
        if (approved.Count == 0)
            return 0;

        var rangeStart = approved.Min(leave => leave.StartDate);
        var rangeEnd = approved.Max(leave => leave.EndDate);
        var holidays = await LeaveYearQueries.GetHolidaySetAsync(context, rangeStart, rangeEnd, cancellationToken);

        return approved.Sum(leave => LeaveCalculationService.CalculateBusinessDays(
            leave.StartDate, leave.EndDate, holidays));
    }
```

Note the closing brace placement: the `return null;` and `}` above end `CheckPerChildEntitlementAsync`, and the two helpers are new members of the class — make sure the class's own closing brace stays last.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PerChildLeave
```
Expected: PASS — `PerChildLeaveCapTests` (14 cases), `PerChildLeaveEntitlementTests` (8) and `PerChildLeaveCalculationServiceTests` all green.

If `Example_one_tracks_what_is_left_across_leave_years` fails on the business-day count, verify the dates by hand: Mon 05 Jan 2026 to Fri 06 Feb 2026 is 25 business days. Adjust the fixture dates, not the cap.

- [ ] **Step 5: Commit**

```bash
git add Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs Tests/WorkTrack.Tests/PerChildLeaveCapTests.cs
git commit -m "Enforce the 18-week and 5-week-per-year per-child caps

The yearly cap is checked against each leave year the request touches, not in
total, which is what stops ten weeks arriving as one request straddling new
year. Each child is a separate ledger; only approved leave counts as used, and
rows with no child charge nobody."
```

---

### Task 8: Wire the calculator into the leave handlers

The calculator is unreachable until a request can carry a child id and the handlers call it.

**Files:**
- Modify: `Application/AnnualLeaves/DTOs/BaseAnnualLeaveDto.cs`, `Application/AnnualLeaves/DTOs/AnnualLeaveDto.cs`, `Application/Core/MappingProfiles.cs`, `Application/AnnualLeaves/Queries/GetAnnualLeaveList.cs`, `Application/AnnualLeaves/Queries/GetAnnualLeaveDetails.cs`, `Application/AnnualLeaves/Commands/CreateAnnualLeave.cs`, `Application/AnnualLeaves/Commands/EditAnnualLeave.cs`, `Application/AnnualLeaves/Commands/UpdateLeaveStatus.cs`
- Test: `Tests/WorkTrack.Tests/PerChildLeaveHandlerTests.cs` (create)

**Interfaces:**
- Consumes: `PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync` (Tasks 6–7).
- Produces: `BaseAnnualLeaveDto.ChildId` (`string?`), `AnnualLeaveDto.ChildId` (`string?`) and `AnnualLeaveDto.ChildName` (`string`). No new handler signatures.

- [ ] **Step 1: Write the failing tests**

Create `Tests/WorkTrack.Tests/PerChildLeaveHandlerTests.cs`:

```csharp
using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The per-child rules only matter if a handler enforces them. One deliberate
/// difference from the pooled annual-leave balance, which is checked at creation
/// only when the type auto-approves: a per-child type is checked at creation even
/// when approval is required, so the employee learns immediately rather than waiting
/// days for a manager to hit an error the manager cannot fix.
/// </summary>
public class PerChildLeaveHandlerTests
{
    private static IMapper BuildMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static Task<Result<string>> Create(AppDbContext db, CreateAnnualLeaveRequest request) =>
        new CreateAnnualLeave.Handler(db, BuildMapper(), new FakeEmailService())
            .Handle(new CreateAnnualLeave.Command { AnnualLeave = request }, CancellationToken.None);

    private static CreateAnnualLeaveRequest Request(string? childId, DateTime start, DateTime end) => new()
    {
        EmployeeId = PerChildLeaveWorld.UserId,
        ChildId = childId,
        LeaveTypeId = PerChildLeaveWorld.PaternityTypeId,
        StartDate = start,
        EndDate = end,
        Reason = "Paternity",
    };

    [Fact]
    public async Task Creating_paternity_leave_without_a_child_is_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();

        var result = await Create(db, Request(childId: null, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)));

        Assert.False(result.IsSuccess);
        Assert.Equal("Select the child this Paternity Leave is for.", result.Error);
        Assert.Empty(await db.AnnualLeaves.ToListAsync());
    }

    [Fact]
    public async Task Creating_paternity_leave_stores_the_child()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));

        var result = await Create(db, Request(child.Id, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)));

        Assert.True(result.IsSuccess);
        var stored = await db.AnnualLeaves.SingleAsync();
        Assert.Equal(child.Id, stored.ChildId);
    }

    /// <summary>
    /// The cap is enforced at creation even though Paternity Leave requires
    /// approval — the employee finds out now, not after a manager tries.
    /// </summary>
    [Fact]
    public async Task The_yearly_cap_is_enforced_at_creation_despite_needing_approval()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        var result = await Create(db, Request(child.Id, new DateTime(2026, 9, 7), new DateTime(2026, 9, 11)));

        Assert.False(result.IsSuccess);
        Assert.Contains("left for the leave year", result.Error);
    }

    /// <summary>
    /// Switching a request off a per-child type must not leave a stale child
    /// attached, or the ledger would keep charging a child for annual leave.
    /// </summary>
    [Fact]
    public async Task A_child_is_not_kept_on_a_type_that_has_no_per_child_entitlement()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));

        var request = Request(child.Id, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5));
        request.LeaveTypeId = PerChildLeaveWorld.AnnualLeaveTypeId;

        var result = await Create(db, request);

        Assert.True(result.IsSuccess);
        var stored = await db.AnnualLeaves.SingleAsync();
        Assert.Null(stored.ChildId);
    }

    [Fact]
    public async Task Approving_paternity_leave_re_checks_the_cap()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));

        // Two pending five-week requests each passed the creation check, because
        // nothing pending counts as used. The second approval is where it breaks.
        var first = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        var second = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));
        db.AnnualLeaves.AddRange(first, second);
        db.Users.Add(new User { Id = "admin-1", UserName = "admin@example.com", Email = "admin@example.com", DisplayName = "Admin" });
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveStatus.Handler(db, new FakeEmailService());

        var firstResult = await handler.Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = first.Id,
            Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
            ChangedByUserId = "admin-1",
            IsAdmin = true,
        }, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondResult = await handler.Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = second.Id,
            Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
            ChangedByUserId = "admin-1",
            IsAdmin = true,
        }, CancellationToken.None);

        Assert.False(secondResult.IsSuccess);
        Assert.Contains("left for the leave year", secondResult.Error);
    }
}
```

Check `UpdateLeaveStatus.Handler`'s constructor arity against the current file before running — if it takes more than `(AppDbContext, IEmailService)`, pass what it needs rather than changing the handler.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~PerChildLeaveHandlerTests
```
Expected: build failure — `CreateAnnualLeaveRequest` has no `ChildId`.

- [ ] **Step 3: Carry the child id on the DTOs**

In `Application/AnnualLeaves/DTOs/BaseAnnualLeaveDto.cs`, after `LeaveTypeId`:

```csharp
    /// <summary>
    /// The child this request is for. Required when the selected leave type has
    /// <c>PerChildEntitlement</c> set, ignored otherwise — the handler clears it
    /// rather than trusting the client, so switching type cannot leave a stale
    /// child attached.
    /// </summary>
    [StringLength(450)]
    public string? ChildId { get; set; }
```

In `Application/AnnualLeaves/DTOs/AnnualLeaveDto.cs`, after `LeaveTypeId`:

```csharp
    public string? ChildId { get; set; }

    /// <summary>Empty when the request is not for a child.</summary>
    public string ChildName { get; set; } = string.Empty;
```

- [ ] **Step 4: Map and load the navigation**

In `Application/Core/MappingProfiles.cs`, add to the `CreateMap<AnnualLeave, AnnualLeaveDto>()` chain, beside the existing `DelegateName` line:

```csharp
            .ForMember(d => d.ChildName, opt => opt.MapFrom(s => s.Child != null ? s.Child.Name : string.Empty))
```

`ChildId` maps by convention on all three `AnnualLeave` maps, so nothing else changes there.

In `Application/AnnualLeaves/Queries/GetAnnualLeaveList.cs`, add to the `Include` chain after `.Include(al => al.Delegate)`:

```csharp
                .Include(al => al.Child)
```

Do the same in `Application/AnnualLeaves/Queries/GetAnnualLeaveDetails.cs` after its `.Include(al => al.Delegate)`. Without these the child's name comes back empty even though the id is present.

- [ ] **Step 5: Enforce it in `CreateAnnualLeave`**

In `Application/AnnualLeaves/Commands/CreateAnnualLeave.cs`, immediately after the `if (leaveType is null) return ...` guard and before the `if (leaveType.RequiresApproval)` branch:

```csharp
            /* The child comes from the client, so trust the leave type instead: a
               request on a type with no per-child entitlement carries no child, no
               matter what was posted. Otherwise switching a request from Paternity
               to Annual Leave would leave the ledger charging a child for it. */
            annualLeave.ChildId = leaveType.PerChildEntitlement
                ? request.AnnualLeave.ChildId
                : null;

            /* Checked here even when the type requires approval — unlike the pooled
               balance, which is only checked at creation when the type auto-approves.
               A per-child refusal is something the employee can act on (pick another
               child, shorten the request); waiting for a manager to hit it days later
               helps nobody. It is re-checked on approval in UpdateLeaveStatus. */
            var perChildError = await PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync(
                context,
                annualLeave,
                employeeProfile,
                excludeLeaveId: annualLeave.Id,
                cancellationToken);
            if (perChildError is not null)
                return Result<string>.Failure(perChildError);
```

- [ ] **Step 6: Enforce it in `EditAnnualLeave`**

In `Application/AnnualLeaves/Commands/EditAnnualLeave.cs`, the field-assignment block currently sets `LeaveTypeId`, `Reason`, `EvidenceUrl`, `DelegateId`. Replace the `annualLeave.LeaveTypeId = request.AnnualLeave.LeaveTypeId;` line with:

```csharp
            annualLeave.LeaveTypeId = request.AnnualLeave.LeaveTypeId;

            var editedLeaveType = await context.LeaveTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(type => type.Id == request.AnnualLeave.LeaveTypeId, cancellationToken);

            // Same rule as on create: the type decides whether a child is carried.
            annualLeave.ChildId = editedLeaveType?.PerChildEntitlement == true
                ? request.AnnualLeave.ChildId
                : null;
```

Then, after the existing `var employeeProfile = await context.EmployeeProfiles...` line in the same handler, add:

```csharp
            if (employeeProfile is not null)
            {
                var perChildError = await PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync(
                    context,
                    annualLeave,
                    employeeProfile,
                    excludeLeaveId: annualLeave.Id,
                    cancellationToken);
                if (perChildError is not null)
                    return Result<Unit>.Failure(perChildError);
            }
```

- [ ] **Step 7: Enforce it in `UpdateLeaveStatus`**

In `Application/AnnualLeaves/Commands/UpdateLeaveStatus.cs`, inside the existing block that runs on the transition into `Approved` — immediately after the pooled `balanceError` check and its `return`:

```csharp
                var perChildError = await PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync(
                    context,
                    annualLeave,
                    employeeProfile,
                    excludeLeaveId: annualLeave.Id,
                    cancellationToken);
                if (perChildError is not null)
                    return Result<Unit>.Failure(perChildError);
```

Both checks are needed: a per-child type never affects the pooled balance (the validator refuses that combination), so the two are mutually exclusive in practice and each returns `null` for the other's types.

- [ ] **Step 8: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
Expected: the five new tests pass and the whole suite still passes. `LeaveOverlapTests`, `LeaveDelegateTests` and `LeaveBalanceAtomicityTests` all drive these handlers — a failure there means a guard was inserted in the wrong place, most likely before the `employeeProfile` null check.

- [ ] **Step 9: Commit**

```bash
git add Application/AnnualLeaves Application/Core/MappingProfiles.cs Tests/WorkTrack.Tests/PerChildLeaveHandlerTests.cs
git commit -m "Enforce per-child entitlement on create, edit and approval

Checked at creation even for a type that requires approval, unlike the pooled
balance: a per-child refusal is something the employee can act on, and waiting
for a manager to hit it days later helps nobody. The leave type decides whether
a child id is carried, so switching type cannot leave a stale child attached."
```

---

### Task 9: Children CRUD

**Files:**
- Create: `Application/Children/DTOs/ChildDto.cs`, `Application/Children/DTOs/UpsertChildRequest.cs`, `Application/Children/Validators/UpsertChildRequestValidator.cs`, `Application/Children/Support/ChildAccessResolver.cs`, `Application/Children/Support/ChildProjection.cs`, `Application/Children/Commands/CreateChild.cs`, `Application/Children/Commands/UpdateChild.cs`, `Application/Children/Commands/DeleteChild.cs`, `Application/Children/Queries/GetChildList.cs`, `API/Controllers/ChildrenController.cs`
- Modify: `Application/Core/Result.cs` (add `FailureFrom`), `Tests/WorkTrack.Tests/ChildCrudTests.cs` (add the handler tests)

**Interfaces:**
- Consumes: `Child` (Task 2), `PerChildLeaveCalculationService.AgeOn` / `.IsEligibleOn` / `.LastEligibleDate` (Task 1), `ManagerAccessScopeResolver.ResolveAsync` (existing, `Application/Core/ManagerAccessScope.cs`).
- Produces:
  - `ChildDto { string Id; string Name; DateOnly DateOfBirth; int AgeYears; bool IsEligible; DateOnly LastEligibleDate; }`
  - `UpsertChildRequest { string Name; DateOnly DateOfBirth; }`
  - `ChildAccessResolver.ResolveAsync(AppDbContext, string callerUserId, string? requestedEmployeeUserId, bool isAdmin, bool isManager, bool forWrite, CancellationToken)` returning `Task<Result<EmployeeProfile>>`
  - `CreateChild.Command { string? EmployeeId; required UpsertChildRequest Child; string CallerUserId; bool IsAdmin; bool IsManager; }` → `Result<ChildDto>`
  - `UpdateChild.Command { required string Id; required UpsertChildRequest Child; string CallerUserId; bool IsAdmin; bool IsManager; }` → `Result<ChildDto>`
  - `DeleteChild.Command { required string Id; string CallerUserId; bool IsAdmin; bool IsManager; }` → `Result<Unit>`
  - `GetChildList.Query { string? EmployeeId; string CallerUserId; bool IsAdmin; bool IsManager; }` → `Result<List<ChildDto>>`

- [ ] **Step 1: Write the failing tests**

Append to `Tests/WorkTrack.Tests/ChildCrudTests.cs` (add `using Application.Children.Commands;`, `using Application.Children.DTOs;`, `using Application.Core;`, `using Persistence;`):

```csharp
    private static Task<Result<ChildDto>> Create(AppDbContext db, string callerUserId, UpsertChildRequest child, string? employeeId = null, bool isAdmin = false) =>
        new CreateChild.Handler(db).Handle(new CreateChild.Command
        {
            EmployeeId = employeeId,
            Child = child,
            CallerUserId = callerUserId,
            IsAdmin = isAdmin,
        }, CancellationToken.None);

    private static UpsertChildRequest Andreas() => new() { Name = "Andreas", DateOfBirth = new DateOnly(2019, 3, 4) };

    [Fact]
    public async Task Adding_a_child_declares_that_the_employee_has_children()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        Assert.Null(profile.HasChildren);

        var result = await Create(db, profile.UserId, Andreas());

        Assert.True(result.IsSuccess);
        Assert.Equal("Andreas", result.Value!.Name);
        // Computed, never stored: the age moves on its own as the date passes.
        Assert.Equal(new DateOnly(2034, 3, 3), result.Value.LastEligibleDate);
        Assert.True(result.Value.IsEligible);

        var reloaded = await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id);
        Assert.True(reloaded.HasChildren);
    }

    /// <summary>
    /// The child list is personal data. One employee may not read or write another's,
    /// whatever employee id they put on the request.
    /// </summary>
    [Fact]
    public async Task An_employee_cannot_add_a_child_to_someone_else()
    {
        await using var db = TestDb.Create();
        await SeedProfileAsync(db, "user-1");
        await SeedProfileAsync(db, "user-2");

        var result = await Create(db, "user-1", Andreas(), employeeId: "user-2");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }

    [Fact]
    public async Task An_admin_can_add_a_child_for_anyone()
    {
        await using var db = TestDb.Create();
        await SeedProfileAsync(db, "admin-1");
        var employee = await SeedProfileAsync(db, "user-2");

        var result = await Create(db, "admin-1", Andreas(), employeeId: "user-2", isAdmin: true);

        Assert.True(result.IsSuccess);
        var stored = await db.Children.SingleAsync();
        Assert.Equal(employee.Id, stored.EmployeeProfileId);
    }

    [Fact]
    public async Task Deleting_a_child_with_leave_against_it_is_refused()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = profile.UserId,
            EmployeeProfileId = profile.Id,
            ChildId = created.Value!.Id,
            StartDate = new DateTime(2026, 3, 2),
            EndDate = new DateTime(2026, 3, 6),
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        var result = await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value.Id,
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Single(await db.Children.ToListAsync());
    }

    [Fact]
    public async Task A_child_with_no_leave_can_be_removed()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        var result = await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value!.Id,
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.Children.ToListAsync());
    }

    /// <summary>
    /// A child who has passed the eligibility age is still listed — the UI has to
    /// explain why they are ineligible rather than silently dropping them, and their
    /// approved leave is still history.
    /// </summary>
    [Fact]
    public async Task An_aged_out_child_is_listed_as_ineligible()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var bornLongAgo = new UpsertChildRequest { Name = "Petros", DateOfBirth = new DateOnly(2005, 1, 20) };

        var result = await Create(db, profile.UserId, bornLongAgo);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsEligible);
        Assert.Equal(21, result.Value.AgeYears);
    }
```

`AgeYears = 21` assumes a run date in 2026 — the repository's current year. If the suite is run in a later year this assertion has to move; prefer asserting `IsEligible` is false and leave the exact age to the domain tests if that becomes a nuisance.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~ChildCrudTests
```
Expected: build failure — `Application.Children` does not exist.

- [ ] **Step 3: Write the DTOs and validator**

`Application/Children/DTOs/ChildDto.cs`:

```csharp
namespace Application.Children.DTOs;

/// <summary>
/// One child as a client sees them. Age and eligibility are computed on every read
/// from <see cref="DateOfBirth"/> — nothing about them is stored, which is what makes
/// a child aging out of eligibility need no job and no recalculation step.
/// </summary>
public class ChildDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public int AgeYears { get; set; }
    public bool IsEligible { get; set; }

    /// <summary>The last date leave for this child may end on.</summary>
    public DateOnly LastEligibleDate { get; set; }
}
```

`Application/Children/DTOs/UpsertChildRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Application.Children.DTOs;

public class UpsertChildRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public DateOnly DateOfBirth { get; set; }
}
```

`Application/Children/Validators/UpsertChildRequestValidator.cs`:

```csharp
using Application.Children.DTOs;
using FluentValidation;

namespace Application.Children.Validators;

public class UpsertChildRequestValidator : AbstractValidator<UpsertChildRequest>
{
    public UpsertChildRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("The child's name is required.")
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("The child's name is required.")
            .MaximumLength(100)
            .WithMessage("The child's name must not exceed 100 characters.");

        RuleFor(x => x.DateOfBirth)
            .NotEqual(default(DateOnly))
            .WithMessage("The child's date of birth is required.")
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("The child's date of birth cannot be in the future.")
            // Bounds the eligibility arithmetic and catches a mistyped year, which
            // would otherwise read as a child who is 1900 years old.
            .GreaterThan(new DateOnly(1900, 1, 1))
            .WithMessage("Check the child's date of birth.");
    }
}
```

- [ ] **Step 4: Write the access resolver**

`Application/Children/Support/ChildAccessResolver.cs`:

```csharp
using Application.Core;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Support;

/// <summary>
/// Whose children a caller may see or change. Children are personal data, so the
/// answer is narrow:
///
/// <list type="bullet">
///   <item>Self — always, read and write.</item>
///   <item>Admin — any employee, read and write.</item>
///   <item>Manager — read only, and only inside their existing department scope. A
///     manager approving a paternity request has to be able to see the ledger it is
///     measured against, but the employee owns the family record.</item>
/// </list>
///
/// Reuses <see cref="ManagerAccessScopeResolver"/> rather than inventing a second
/// notion of a manager's reach.
/// </summary>
public static class ChildAccessResolver
{
    public static async Task<Result<EmployeeProfile>> ResolveAsync(
        AppDbContext context,
        string callerUserId,
        string? requestedEmployeeUserId,
        bool isAdmin,
        bool isManager,
        bool forWrite,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(callerUserId))
            return Result<EmployeeProfile>.Invalid("User context is required.");

        var targetUserId = string.IsNullOrWhiteSpace(requestedEmployeeUserId)
            ? callerUserId
            : requestedEmployeeUserId;

        var profile = await context.EmployeeProfiles
            .FirstOrDefaultAsync(ep => ep.UserId == targetUserId, cancellationToken);

        if (profile is null)
            return Result<EmployeeProfile>.Failure("Employee profile not found.");

        if (targetUserId == callerUserId || isAdmin)
            return Result<EmployeeProfile>.Success(profile);

        if (isManager && !forWrite)
        {
            var scope = await ManagerAccessScopeResolver.ResolveAsync(context, callerUserId, cancellationToken);
            var inScope = (profile.DepartmentId.HasValue && scope.ManagedDepartmentIds.Contains(profile.DepartmentId.Value))
                || scope.DirectReportUserIds.Contains(profile.UserId);

            if (inScope)
                return Result<EmployeeProfile>.Success(profile);
        }

        return Result<EmployeeProfile>.Forbidden(
            "You can only view or change children on your own profile.");
    }
}
```

Check `Application/Core/Result.cs` for the exact factory names — the file defines `Success`, `Failure`, `Conflict` and (per `BaseApiController`) `Forbidden` and `Invalid` kinds. If a factory for a kind is missing, add it in the same style as `Conflict`, with a doc comment saying why.

- [ ] **Step 5a: Add `FailureFrom` to `Result<T>`**

`Application/Core/Result.cs` has `Success`, `Failure`, `Conflict`, `Forbidden`, `Invalid` and `ValidationFailure`, but nothing that forwards an existing failure. Every handler in this task calls `ChildAccessResolver` and has to pass its refusal through with the reason intact — without this, forwarding a `Forbidden` through `Result<ChildDto>.Failure(access.Error)` silently demotes a 403 to a 404. Add it beside `Conflict`:

```csharp
    /// <summary>
    /// Re-wraps a failure from a nested call — typically an authorization refusal
    /// resolved by a helper — keeping the reason so the API layer still picks the
    /// right status code. Without this, forwarding a Forbidden through
    /// <see cref="Failure"/> silently demotes it to a 404, which tells a legitimate
    /// caller who mistyped an id the same thing it tells someone reaching for a
    /// record that isn't theirs.
    /// </summary>
    public static Result<T> FailureFrom<TOther>(Result<TOther> other) => new()
    {
        IsSuccess = false,
        Error = other.Error,
        ErrorKind = other.ErrorKind,
        ValidationErrors = other.ValidationErrors,
    };
```

Use `Result<X>.FailureFrom(access)` in all four handlers in this task and in Task 10's query — one form, consistently.

- [ ] **Step 5b: Write the commands and query**

`Application/Children/Commands/CreateChild.cs`:

```csharp
using Application.Children.DTOs;
using Application.Children.Support;
using Application.Core;
using Domain;
using Domain.Services;
using MediatR;
using Persistence;

namespace Application.Children.Commands;

public class CreateChild
{
    public class Command : IRequest<Result<ChildDto>>
    {
        /// <summary>The employee's user id. Null means the caller themselves.</summary>
        public string? EmployeeId { get; set; }
        public required UpsertChildRequest Child { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<ChildDto>>
    {
        public async Task<Result<ChildDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, request.EmployeeId,
                request.IsAdmin, request.IsManager, forWrite: true, cancellationToken);

            if (!access.IsSuccess)
                return Result<ChildDto>.FailureFrom(access);

            var profile = access.Value!;

            var child = new Child
            {
                EmployeeProfileId = profile.Id,
                Name = request.Child.Name.Trim(),
                DateOfBirth = request.Child.DateOfBirth,
            };

            context.Children.Add(child);

            // The declaration and the list cannot be allowed to disagree: adding a
            // child answers "do you have children" whatever the flag said before.
            profile.HasChildren = true;

            await context.SaveChangesAsync(cancellationToken);

            return Result<ChildDto>.Success(ChildProjection.ToDto(child, ChildProjection.DefaultEligibilityAge));
        }
    }
}
```

`Application/Children/Support/ChildProjection.cs` — the shared mapping, so four callers cannot compute eligibility three different ways:

```csharp
using Application.Children.DTOs;
using Domain;
using Domain.Services;

namespace Application.Children.Support;

/// <summary>
/// Child → <see cref="ChildDto"/>, including the computed age and eligibility.
/// Shared so the list, the create/update responses and the entitlement ledger cannot
/// answer the eligibility question three slightly different ways.
/// </summary>
internal static class ChildProjection
{
    /// <summary>
    /// Used when no per-child leave type is in play (a plain child list). The real
    /// figure comes from <c>LeaveType.ChildEligibleUntilAge</c>; this is only the
    /// fallback for rendering a list when no type has been chosen yet.
    /// </summary>
    public const int DefaultEligibilityAge = 15;

    public static ChildDto ToDto(Child child, int eligibleUntilAge)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new ChildDto
        {
            Id = child.Id,
            Name = child.Name,
            DateOfBirth = child.DateOfBirth,
            AgeYears = PerChildLeaveCalculationService.AgeOn(child.DateOfBirth, today),
            IsEligible = PerChildLeaveCalculationService.IsEligibleOn(child.DateOfBirth, today, eligibleUntilAge),
            LastEligibleDate = PerChildLeaveCalculationService.LastEligibleDate(child.DateOfBirth, eligibleUntilAge),
        };
    }
}
```

`Application/Children/Commands/UpdateChild.cs` — same shape as `CreateChild`, resolving access from the child's owning profile:

```csharp
using Application.Children.DTOs;
using Application.Children.Support;
using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Commands;

public class UpdateChild
{
    public class Command : IRequest<Result<ChildDto>>
    {
        public required string Id { get; set; }
        public required UpsertChildRequest Child { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<ChildDto>>
    {
        public async Task<Result<ChildDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var child = await context.Children
                .Include(c => c.EmployeeProfile)
                .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

            if (child is null)
                return Result<ChildDto>.Failure("Cannot find the child.");

            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, child.EmployeeProfile?.UserId,
                request.IsAdmin, request.IsManager, forWrite: true, cancellationToken);

            if (!access.IsSuccess)
                return Result<ChildDto>.FailureFrom(access);

            child.Name = request.Child.Name.Trim();
            child.DateOfBirth = request.Child.DateOfBirth;

            await context.SaveChangesAsync(cancellationToken);

            return Result<ChildDto>.Success(ChildProjection.ToDto(child, ChildProjection.DefaultEligibilityAge));
        }
    }
}
```

`Application/Children/Commands/DeleteChild.cs`:

```csharp
using Application.Children.Support;
using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Commands;

public class DeleteChild
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var child = await context.Children
                .Include(c => c.EmployeeProfile)
                .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

            if (child is null)
                return Result<Unit>.Failure("Cannot find the child.");

            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, child.EmployeeProfile?.UserId,
                request.IsAdmin, request.IsManager, forWrite: true, cancellationToken);

            if (!access.IsSuccess)
                return Result<Unit>.FailureFrom(access);

            /* Refused rather than cascaded: the child's row is what the per-child
               ledger is queried by, so deleting it would erase the record of leave
               that was actually taken. The database says the same thing (the FK is
               Restrict) — this is the version with an explanation. A child who has
               aged out is kept, not deleted; they read as ineligible. */
            var leaveCount = await context.AnnualLeaves
                .CountAsync(leave => leave.ChildId == child.Id, cancellationToken);

            if (leaveCount > 0)
            {
                return Result<Unit>.Conflict(
                    $"{child.Name} has {leaveCount} leave request(s) recorded against them and cannot be removed. " +
                    "A child who is no longer eligible is kept on the profile rather than deleted.");
            }

            context.Children.Remove(child);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
```

`Application/Children/Queries/GetChildList.cs`:

```csharp
using Application.Children.DTOs;
using Application.Children.Support;
using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Queries;

public class GetChildList
{
    public class Query : IRequest<Result<List<ChildDto>>>
    {
        /// <summary>The employee's user id. Null means the caller themselves.</summary>
        public string? EmployeeId { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<ChildDto>>>
    {
        public async Task<Result<List<ChildDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, request.EmployeeId,
                request.IsAdmin, request.IsManager, forWrite: false, cancellationToken);

            if (!access.IsSuccess)
                return Result<List<ChildDto>>.FailureFrom(access);

            var profileId = access.Value!.Id;

            var children = await context.Children
                .AsNoTracking()
                .Where(c => c.EmployeeProfileId == profileId)
                .OrderBy(c => c.DateOfBirth)
                .ToListAsync(cancellationToken);

            // Oldest first: it is the order a parent lists their children in, and it
            // puts the child closest to aging out at the top.
            var dtos = children
                .Select(c => ChildProjection.ToDto(c, ChildProjection.DefaultEligibilityAge))
                .ToList();

            return Result<List<ChildDto>>.Success(dtos);
        }
    }
}
```

- [ ] **Step 6: Write the controller**

`API/Controllers/ChildrenController.cs`:

```csharp
using Application.Children.Commands;
using Application.Children.DTOs;
using Application.Children.Queries;
using Asp.Versioning;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// An employee's declared children, which is what a per-child leave entitlement is
/// measured against. Every action is authorized inside the handler as well, against
/// the caller's own profile — the role attributes here only say who may reach the
/// endpoint at all, not whose children they may touch.
/// </summary>
[ApiVersion("1.0")]
[Authorize]
public class ChildrenController : BaseApiController
{
    private string CallerUserId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

    private bool IsAdmin => User.IsInRole(AppRoles.Admin);
    private bool IsManager => User.IsInRole(AppRoles.Manager);

    [HttpGet]
    public async Task<ActionResult<List<ChildDto>>> GetChildren([FromQuery] string? employeeId)
    {
        var result = await Mediator.Send(new GetChildList.Query
        {
            EmployeeId = employeeId,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<ChildDto>> CreateChild(
        UpsertChildRequest request, [FromQuery] string? employeeId)
    {
        var result = await Mediator.Send(new CreateChild.Command
        {
            EmployeeId = employeeId,
            Child = request,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ChildDto>> UpdateChild(string id, UpsertChildRequest request)
    {
        var result = await Mediator.Send(new UpdateChild.Command
        {
            Id = id,
            Child = request,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteChild(string id)
    {
        var result = await Mediator.Send(new DeleteChild.Command
        {
            Id = id,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }
}
```

Check how a sibling controller reads the current user — if the codebase resolves it through `ICurrentUserAccessor` in the handler rather than from `User` in the controller, follow that instead and drop the three helper members. Grep `ICurrentUserAccessor` for the established pattern before writing this.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
Expected: the six new `ChildCrudTests` cases pass along with the two schema ones, and the full suite passes. `MediatRHandlerRegistrationTests` covers every handler in the assembly, so it will pick up the four new ones automatically — if it fails, a handler is not a public nested type implementing `IRequestHandler<,>`.

- [ ] **Step 8: Commit**

```bash
git add Application/Children API/Controllers/ChildrenController.cs Tests/WorkTrack.Tests/ChildCrudTests.cs
git commit -m "Add children CRUD with per-caller access rules

Self and Admin may write, a Manager may only read inside their existing
department scope -- a manager approving a paternity request needs to see the
ledger it is measured against, but the employee owns the family record.
Deleting a child with leave against them is refused: their row is what the
ledger is queried by."
```

---

### Task 10: The per-child ledger query

Requirements 3–8 in one call, plus requirement 10 for free: the totals are a projection over eligible children, so they fall the moment a child ages out, with no recalculation step and regardless of what that child had used.

**Files:**
- Create: `Application/Children/DTOs/ChildLeaveEntitlementDto.cs`, `Application/Children/Queries/GetChildLeaveEntitlements.cs`
- Modify: `API/Controllers/ChildrenController.cs` (one endpoint), `Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs` (expose the usage helpers to the query)
- Test: `Tests/WorkTrack.Tests/ChildLeaveEntitlementQueryTests.cs` (create)

**Interfaces:**
- Consumes: `PerChildLeaveCalculationService` (Task 1), `LeaveYearQueries` (Task 5), `ChildProjection` (Task 9), `ChildAccessResolver` (Task 9).
- Produces:
  - `ChildLeaveEntitlementDto { string ChildId; string Name; DateOnly DateOfBirth; int AgeYears; bool IsEligible; DateOnly LastEligibleDate; int TotalDays; decimal TotalWeeks; int UsedDays; int RemainingDays; int ThisYearCapDays; int ThisYearUsedDays; int ThisYearRemainingDays; DateTime LeaveYearStart; DateTime LeaveYearEnd; }`
  - `ChildLeaveEntitlementSummaryDto { int? LeaveTypeId; string LeaveTypeName; int EligibleChildCount; int TotalRemainingDays; int ThisYearCapDays; int ThisYearRemainingDays; List<ChildLeaveEntitlementDto> Children; }`
  - `GetChildLeaveEntitlements.Query { string? EmployeeId; string CallerUserId; bool IsAdmin; bool IsManager; }` → `Result<ChildLeaveEntitlementSummaryDto>`
  - `PerChildLeaveBalanceCalculator.ApprovedLeaveForChildAsync` and `.UsedBusinessDaysAsync` change from `private` to `internal` so the query reuses exactly the arithmetic the enforcement uses.

- [ ] **Step 1: Write the failing tests**

Create `Tests/WorkTrack.Tests/ChildLeaveEntitlementQueryTests.cs`:

```csharp
using Application.Children.Queries;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The ledger the UI quotes. It must agree with the calculator that enforces the
/// caps to the day — a screen promising 13 weeks while the API refuses at 8 is worse
/// than no screen — so both read usage through the same helper.
///
/// The employee's totals cover eligible children only. That is requirement 10 with
/// no code: a child aging out changes the projection, so the figures fall on the next
/// read whether or not that child had used leave.
/// </summary>
public class ChildLeaveEntitlementQueryTests
{
    private static readonly DateOnly YoungChild = new(2019, 3, 4);

    private static Task<Application.Core.Result<Application.Children.DTOs.ChildLeaveEntitlementSummaryDto>> Query(
        Persistence.AppDbContext db) =>
        new GetChildLeaveEntitlements.Handler(db).Handle(new GetChildLeaveEntitlements.Query
        {
            CallerUserId = PerChildLeaveWorld.UserId,
        }, CancellationToken.None);

    [Fact]
    public async Task An_untouched_child_has_the_full_eighteen_weeks()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        var result = await Query(db);

        Assert.True(result.IsSuccess);
        var child = Assert.Single(result.Value!.Children);
        Assert.Equal(90, child.TotalDays);
        Assert.Equal(18.0m, child.TotalWeeks);
        Assert.Equal(0, child.UsedDays);
        Assert.Equal(90, child.RemainingDays);
        Assert.Equal(25, child.ThisYearCapDays);
        Assert.Equal(25, child.ThisYearRemainingDays);
    }

    [Fact]
    public async Task Used_days_come_off_both_the_total_and_the_year()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 5 business days in the current leave year. DateTime.UtcNow decides which
        // year that is, so anchor to it rather than to a literal 2026.
        var monday = ThisYearsMonday();
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, monday, monday.AddDays(4));

        var result = await Query(db);

        var row = Assert.Single(result.Value!.Children);
        Assert.Equal(5, row.UsedDays);
        Assert.Equal(85, row.RemainingDays);
        Assert.Equal(5, row.ThisYearUsedDays);
        Assert.Equal(20, row.ThisYearRemainingDays);
    }

    /// <summary>
    /// Requirement 10. Three children, one of them 15: the totals describe two.
    /// The aged-out child is still listed, with isEligible false, so the UI can say
    /// why rather than silently dropping them.
    /// </summary>
    [Fact]
    public async Task Totals_cover_eligible_children_only()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);
        await PerChildLeaveWorld.AddChildAsync(db, "Maria", new DateOnly(2021, 6, 15));
        // Born well over 15 years ago whenever this runs.
        var agedOut = await PerChildLeaveWorld.AddChildAsync(
            db, "Petros", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-16));

        // The aged-out child used leave when they were eligible. It stays in
        // history and must not resurrect their entitlement.
        var monday = ThisYearsMonday();
        await PerChildLeaveWorld.ApproveLeaveAsync(db, agedOut.Id, monday, monday.AddDays(4));

        var result = await Query(db);
        var summary = result.Value!;

        Assert.Equal(3, summary.Children.Count);
        Assert.Equal(2, summary.EligibleChildCount);
        // Two eligible children, untouched: 2 x 90 days.
        Assert.Equal(180, summary.TotalRemainingDays);
        // 2 x 25 days this leave year.
        Assert.Equal(50, summary.ThisYearCapDays);

        var petros = summary.Children.Single(c => c.Name == "Petros");
        Assert.False(petros.IsEligible);
        Assert.Equal(5, petros.UsedDays);
        Assert.Equal(0, petros.RemainingDays);
        Assert.Equal(0, petros.ThisYearRemainingDays);
    }

    [Fact]
    public async Task Without_a_per_child_leave_type_the_ledger_is_empty()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var paternity = await db.LeaveTypes.FindAsync(PerChildLeaveWorld.PaternityTypeId);
        paternity!.PerChildEntitlement = false;
        await db.SaveChangesAsync();

        await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        var result = await Query(db);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.LeaveTypeId);
        Assert.Empty(result.Value.Children);
    }

    /// <summary>The Monday of the first full week of the current calendar year.</summary>
    private static DateTime ThisYearsMonday()
    {
        var date = new DateTime(DateTime.UtcNow.Year, 1, 1);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~ChildLeaveEntitlementQueryTests
```
Expected: build failure — `GetChildLeaveEntitlements` does not exist.

- [ ] **Step 3: Write the DTOs**

`Application/Children/DTOs/ChildLeaveEntitlementDto.cs`:

```csharp
namespace Application.Children.DTOs;

/// <summary>
/// One child's per-child leave ledger. Every figure is derived — from the child's
/// date of birth, the leave type's configuration, and that child's approved leave —
/// so nothing here can go stale and nothing needs recalculating when a child ages
/// out.
/// </summary>
public class ChildLeaveEntitlementDto
{
    public string ChildId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public int AgeYears { get; set; }
    public bool IsEligible { get; set; }
    public DateOnly LastEligibleDate { get; set; }

    /// <summary>Lifetime entitlement in business days. 90 for 18 weeks.</summary>
    public int TotalDays { get; set; }
    public decimal TotalWeeks { get; set; }
    public int UsedDays { get; set; }
    public int RemainingDays { get; set; }

    /// <summary>The per-year cap in business days. 25 for 5 weeks.</summary>
    public int ThisYearCapDays { get; set; }
    public int ThisYearUsedDays { get; set; }
    public int ThisYearRemainingDays { get; set; }

    /// <summary>The leave-year window the "this year" figures describe.</summary>
    public DateTime LeaveYearStart { get; set; }
    public DateTime LeaveYearEnd { get; set; }
}

/// <summary>
/// The employee's per-child ledger. The totals cover <b>eligible</b> children only,
/// which is what makes a child turning 15 reduce them with no recalculation step.
/// </summary>
public class ChildLeaveEntitlementSummaryDto
{
    /// <summary>Null when no active leave type has a per-child entitlement.</summary>
    public int? LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;

    public int EligibleChildCount { get; set; }
    public int TotalRemainingDays { get; set; }
    public int ThisYearCapDays { get; set; }
    public int ThisYearRemainingDays { get; set; }

    /// <summary>
    /// Every declared child, ineligible ones included with <c>IsEligible</c> false,
    /// so the UI can explain why rather than silently omitting them.
    /// </summary>
    public List<ChildLeaveEntitlementDto> Children { get; set; } = [];
}
```

- [ ] **Step 4: Open up the two usage helpers**

In `Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs`, change `private static Task<List<AnnualLeave>> ApprovedLeaveForChildAsync` to `internal static`, and `private static async Task<int> UsedBusinessDaysAsync` to `internal static`. Add above the first:

```csharp
    /// <summary>
    /// Internal rather than private so <c>GetChildLeaveEntitlements</c> reads usage
    /// through exactly the arithmetic this class enforces. A screen promising more
    /// than the API will approve is worse than no screen.
    /// </summary>
```

- [ ] **Step 5: Write the query**

`Application/Children/Queries/GetChildLeaveEntitlements.cs`:

```csharp
using Application.AnnualLeaves.Commands;
using Application.Children.DTOs;
using Application.Children.Support;
using Application.Core;
using Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Queries;

public class GetChildLeaveEntitlements
{
    public class Query : IRequest<Result<ChildLeaveEntitlementSummaryDto>>
    {
        /// <summary>The employee's user id. Null means the caller themselves.</summary>
        public string? EmployeeId { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<ChildLeaveEntitlementSummaryDto>>
    {
        public async Task<Result<ChildLeaveEntitlementSummaryDto>> Handle(
            Query request, CancellationToken cancellationToken)
        {
            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, request.EmployeeId,
                request.IsAdmin, request.IsManager, forWrite: false, cancellationToken);

            if (!access.IsSuccess)
                return Result<ChildLeaveEntitlementSummaryDto>.FailureFrom(access);

            var profile = access.Value!;

            // Whichever active leave type carries a per-child entitlement — paternity
            // in practice. No type, no ledger: there is nothing to be entitled to.
            var leaveType = await context.LeaveTypes
                .AsNoTracking()
                .Where(lt => lt.PerChildEntitlement && lt.IsActive)
                .OrderBy(lt => lt.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var summary = new ChildLeaveEntitlementSummaryDto();

            if (leaveType is null)
                return Result<ChildLeaveEntitlementSummaryDto>.Success(summary);

            summary.LeaveTypeId = leaveType.Id;
            summary.LeaveTypeName = leaveType.Name;

            var children = await context.Children
                .AsNoTracking()
                .Where(c => c.EmployeeProfileId == profile.Id)
                .OrderBy(c => c.DateOfBirth)
                .ToListAsync(cancellationToken);

            var totalDays = PerChildLeaveCalculationService.WeeksToBusinessDays(leaveType.PerChildTotalWeeks);
            var yearCapDays = PerChildLeaveCalculationService.WeeksToBusinessDays(leaveType.PerChildWeeksPerYear);

            var startMonth = await LeaveYearQueries.GetLeaveYearStartMonthAsync(context, cancellationToken);
            var leaveYearKey = LeaveCalculationService.GetLeaveYearKey(DateTime.UtcNow, startMonth);
            var (leaveYearStart, leaveYearEnd) = LeaveCalculationService.GetLeaveYearBounds(leaveYearKey, startMonth);
            var yearHolidays = await LeaveYearQueries.GetHolidaySetAsync(
                context, leaveYearStart, leaveYearEnd, cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            foreach (var child in children)
            {
                var approved = await PerChildLeaveBalanceCalculator.ApprovedLeaveForChildAsync(
                    context, child.Id, excludeLeaveId: null, cancellationToken);

                var usedDays = await PerChildLeaveBalanceCalculator.UsedBusinessDaysAsync(
                    context, approved, cancellationToken);

                var usedThisYear = approved.Sum(leave => LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
                    leave.StartDate, leave.EndDate, leaveYearKey, startMonth, yearHolidays));

                var isEligible = PerChildLeaveCalculationService.IsEligibleOn(
                    child.DateOfBirth, today, leaveType.ChildEligibleUntilAge);

                /* An ineligible child has no remaining entitlement, whatever the
                   arithmetic would say — the rule applies regardless of how much
                   they had used. Their used figure stays, because it happened. */
                var remainingDays = isEligible
                    ? PerChildLeaveCalculationService.RemainingDays(totalDays, usedDays)
                    : 0;
                var remainingThisYear = isEligible
                    ? Math.Min(
                        PerChildLeaveCalculationService.RemainingDays(yearCapDays, usedThisYear),
                        remainingDays)
                    : 0;

                summary.Children.Add(new ChildLeaveEntitlementDto
                {
                    ChildId = child.Id,
                    Name = child.Name,
                    DateOfBirth = child.DateOfBirth,
                    AgeYears = PerChildLeaveCalculationService.AgeOn(child.DateOfBirth, today),
                    IsEligible = isEligible,
                    LastEligibleDate = PerChildLeaveCalculationService.LastEligibleDate(
                        child.DateOfBirth, leaveType.ChildEligibleUntilAge),
                    TotalDays = isEligible ? totalDays : 0,
                    TotalWeeks = isEligible
                        ? PerChildLeaveCalculationService.BusinessDaysToWeeks(totalDays)
                        : 0m,
                    UsedDays = usedDays,
                    RemainingDays = remainingDays,
                    ThisYearCapDays = isEligible ? yearCapDays : 0,
                    ThisYearUsedDays = usedThisYear,
                    ThisYearRemainingDays = remainingThisYear,
                    LeaveYearStart = leaveYearStart,
                    LeaveYearEnd = leaveYearEnd,
                });
            }

            // Requirement 10: the employee's totals are a projection over eligible
            // children, so they fall the moment one ages out — no recalculation.
            var eligible = summary.Children.Where(c => c.IsEligible).ToList();
            summary.EligibleChildCount = eligible.Count;
            summary.TotalRemainingDays = eligible.Sum(c => c.RemainingDays);
            summary.ThisYearCapDays = eligible.Count * yearCapDays;
            summary.ThisYearRemainingDays = eligible.Sum(c => c.ThisYearRemainingDays);

            return Result<ChildLeaveEntitlementSummaryDto>.Success(summary);
        }
    }
}
```

Note `ThisYearRemainingDays` per child is the **lesser** of the yearly remainder and the lifetime remainder: with 3 days of total entitlement left, 25 days this year is not true.

- [ ] **Step 6: Add the endpoint**

In `API/Controllers/ChildrenController.cs`, before the closing brace:

```csharp
    /// <summary>
    /// The per-child leave ledger: entitlement, used and remaining, per child and in
    /// total. Read-only and derived — there is nothing here to write.
    /// </summary>
    [HttpGet("entitlements")]
    public async Task<ActionResult<ChildLeaveEntitlementSummaryDto>> GetEntitlements(
        [FromQuery] string? employeeId)
    {
        var result = await Mediator.Send(new GetChildLeaveEntitlements.Query
        {
            EmployeeId = employeeId,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }
```

The literal route `entitlements` must not collide with `{id}` — `[HttpGet]` on the list and `[HttpGet("entitlements")]` are distinct, and there is no `[HttpGet("{id}")]` on this controller, so no ambiguity arises. If one is added later it needs a route constraint.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
Expected: the four new tests pass and the full suite passes. `AttendanceRouteSurfaceTests` / `ApiRouteTableFixture` enumerate the live endpoint table — if one of those asserts an exact route count it will need the new routes added; read the assertion before touching it, and add the routes rather than loosening the check.

- [ ] **Step 8: Commit**

```bash
git add Application/Children API/Controllers/ChildrenController.cs Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs Tests/WorkTrack.Tests/ChildLeaveEntitlementQueryTests.cs
git commit -m "Add the per-child leave entitlement ledger

Reads usage through the same helpers the calculator enforces with, so the screen
cannot promise more than the API will approve. The employee's totals cover
eligible children only, which is what makes a child turning 15 reduce them with
no recalculation step -- their used days stay in history, their remaining
entitlement is gone."
```

---

### Task 11: Declaring whether you have children

Requirement 1. Rides on the profile update the Edit profile dialog already calls, rather than becoming a fifth endpoint.

**Files:**
- Create: `Application/Children/Support/HasChildrenDeclaration.cs`
- Modify: `Application/Accounts/DTOs/UpdateProfileDto.cs`, `API/Controllers/AccountController.cs` (the `PUT profile` action and the `user-info` response)
- Test: `Tests/WorkTrack.Tests/ChildCrudTests.cs` (add five cases)

**Interfaces:**
- Consumes: `EmployeeProfile.HasChildren` (Task 2), `Child` (Task 2).
- Produces: `HasChildrenDeclaration.ValidateAsync(AppDbContext, EmployeeProfile, bool? requested, CancellationToken)` returning `Task<string?>`; `UpdateProfileDto.HasChildren` (`bool?`); the `user-info` and `PUT profile` responses both gain a `hasChildren` field.

- [ ] **Step 1: Write the failing tests**

Append to `Tests/WorkTrack.Tests/ChildCrudTests.cs`:

```csharp
    /// <summary>
    /// The declaration and the list must not be able to disagree. Saying "no
    /// children" while children are on the profile is refused rather than silently
    /// ignored — a flag that contradicts the rows beside it is the failure mode
    /// CLAUDE.md documents for every duplicated leave figure.
    /// </summary>
    [Fact]
    public async Task Declaring_no_children_while_children_exist_is_refused()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        await Create(db, profile.UserId, Andreas());

        var error = await HasChildrenDeclaration.ValidateAsync(
            db, profile, requested: false, CancellationToken.None);

        Assert.Equal("Remove the 1 child(ren) on your profile before saying you have none.", error);
    }

    [Fact]
    public async Task Declaring_no_children_is_fine_when_there_are_none()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);

        Assert.Null(await HasChildrenDeclaration.ValidateAsync(
            db, profile, requested: false, CancellationToken.None));
    }

    /// <summary>
    /// A client that does not send the field must not silently retract a
    /// declaration, so null means "leave it alone" rather than "false".
    /// </summary>
    [Fact]
    public async Task An_absent_declaration_changes_nothing()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        await Create(db, profile.UserId, Andreas());

        Assert.Null(await HasChildrenDeclaration.ValidateAsync(
            db, profile, requested: null, CancellationToken.None));
    }

    [Fact]
    public async Task Removing_the_last_child_leaves_the_declaration_alone()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value!.Id,
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        // Still true: they told us they have children, and removing a row is not a
        // retraction. They can uncheck the box themselves.
        Assert.True((await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id)).HasChildren);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter FullyQualifiedName~ChildCrudTests
```
Expected: build failure — `HasChildrenDeclaration` does not exist. `Removing_the_last_child_leaves_the_declaration_alone` pins existing Task 9 behaviour and will pass once the file compiles; if it fails, fix Task 9's `DeleteChild` handler rather than the test.

Add `using Application.Children.Support;` to the test file.

- [ ] **Step 3: Add the field to the profile update**

In `Application/Accounts/DTOs/UpdateProfileDto.cs`, after `DateOfBirth`:

```csharp
    /// <summary>
    /// Whether this employee has children — requirement 1 of the per-child leave
    /// entitlement. Null leaves the stored answer alone (an older client that does
    /// not send the field must not silently retract a declaration).
    ///
    /// Refused as false while children are on the profile: the flag and the list
    /// cannot be allowed to disagree.
    /// </summary>
    public bool? HasChildren { get; set; }
```

Create `Application/Children/Support/HasChildrenDeclaration.cs` — the invariant lives here rather than inline in the controller so it is testable without spinning up the web host:

```csharp
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Support;

/// <summary>
/// Guards the one invariant on <see cref="EmployeeProfile.HasChildren"/>: it cannot
/// say "no children" while children are on the profile. A flag free to contradict
/// the rows beside it is the failure mode CLAUDE.md documents for every duplicated
/// leave figure — so the contradiction is refused rather than quietly resolved in
/// either direction.
/// </summary>
public static class HasChildrenDeclaration
{
    /// <summary>
    /// An error message when the requested declaration contradicts the profile's
    /// children, or <c>null</c> when it may be saved. A <c>null</c> request leaves
    /// the stored answer alone — a client that does not send the field must not
    /// silently retract a declaration.
    /// </summary>
    public static async Task<string?> ValidateAsync(
        AppDbContext context,
        EmployeeProfile profile,
        bool? requested,
        CancellationToken cancellationToken)
    {
        if (requested is not false)
            return null;

        var childCount = await context.Children
            .CountAsync(child => child.EmployeeProfileId == profile.Id, cancellationToken);

        return childCount == 0
            ? null
            : $"Remove the {childCount} child(ren) on your profile before saying you have none.";
    }
}
```

In `API/Controllers/AccountController.cs`, inside the `UpdateProfile` action after the `employeeProfile is null` guard and before the save:

```csharp
        var declarationError = await HasChildrenDeclaration.ValidateAsync(
            context, employeeProfile, request.HasChildren, HttpContext.RequestAborted);

        if (declarationError is not null)
            return BadRequest(new { message = declarationError });

        if (request.HasChildren.HasValue)
            employeeProfile.HasChildren = request.HasChildren.Value;
```

Then add `hasChildren = employeeProfile.HasChildren,` to the anonymous response object this action returns (beside `departmentId` / `departmentName`), and the same to the `user-info` response object built from `employeeProfile` around line 209. Without the second, the dialog cannot render the checkbox's current state on first open.

- [ ] **Step 4: Run the whole suite**

```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
Expected: PASS. `AdminUserInviteTests` and `UserDeactivationTests` touch `AccountController` — a failure there points at a mis-placed brace in the action.

- [ ] **Step 5: Commit**

```bash
git add Application/Accounts/DTOs/UpdateProfileDto.cs API/Controllers/AccountController.cs Tests/WorkTrack.Tests/ChildCrudTests.cs
git commit -m "Let an employee declare whether they have children

A tri-state on the profile: null means never asked, which is what the leave
request form needs in order to say 'add your children' rather than 'you have
none'. Refused as false while children exist, so the flag and the list cannot
disagree. Rides on the existing profile update rather than a fifth endpoint."
```

---

### Task 12: Client types, API module and allowance helper

The typed surface every later frontend task consumes. No UI yet.

**Files:**
- Create: `client/src/lib/types/child.ts`, `client/src/lib/api/children.ts`
- Modify: `client/src/lib/types/index.ts`, `client/src/lib/types/leave-type.ts`, `client/src/lib/types/annual-leave.ts`, `client/src/lib/types/auth.ts`, `client/src/lib/api/index.ts`, `client/src/lib/leave-allowance.ts`

**Interfaces:**
- Consumes: the API from Tasks 9–11.
- Produces:
  - `Child { id, name, dateOfBirth, ageYears, isEligible, lastEligibleDate }`
  - `ChildLeaveEntitlement { childId, name, dateOfBirth, ageYears, isEligible, lastEligibleDate, totalDays, totalWeeks, usedDays, remainingDays, thisYearCapDays, thisYearUsedDays, thisYearRemainingDays, leaveYearStart, leaveYearEnd }`
  - `ChildLeaveEntitlementSummary { leaveTypeId, leaveTypeName, eligibleChildCount, totalRemainingDays, thisYearCapDays, thisYearRemainingDays, children }`
  - `UpsertChildRequest { name, dateOfBirth }`
  - `getChildren(employeeId?)`, `createChild(request, employeeId?)`, `updateChild(id, request)`, `deleteChild(id)`, `getChildLeaveEntitlements(employeeId?)`
  - `LeaveType` gains `perChildEntitlement`, `perChildTotalWeeks`, `perChildWeeksPerYear`, `childEligibleUntilAge`; `AnnualLeaveBase` gains `childId?`; `AnnualLeave` gains `childId`, `childName`; `UpdateProfileRequest` gains `hasChildren?`; `UserInfo` gains `hasChildren?`
  - `client/src/lib/leave-allowance.ts` gains `describeAllowance(type)`

- [ ] **Step 1: Write the types**

Create `client/src/lib/types/child.ts`:

```ts
/**
 * A declared child. `ageYears`, `isEligible` and `lastEligibleDate` are computed by
 * the server from `dateOfBirth` on every read — nothing about them is stored, which
 * is how a child ages out of paternity-leave eligibility with no job to run.
 */
export interface Child {
    id: string
    name: string
    /** ISO date "yyyy-MM-dd". */
    dateOfBirth: string
    ageYears: number
    isEligible: boolean
    /** The last date leave for this child may end on. ISO date. */
    lastEligibleDate: string
}

export interface UpsertChildRequest {
    name: string
    dateOfBirth: string
}

/**
 * One child's per-child leave ledger, in business days with a weeks figure for
 * display. An ineligible child is present with `isEligible: false` and zeroed
 * entitlement — `usedDays` still shows what they used while eligible.
 */
export interface ChildLeaveEntitlement {
    childId: string
    name: string
    dateOfBirth: string
    ageYears: number
    isEligible: boolean
    lastEligibleDate: string
    totalDays: number
    totalWeeks: number
    usedDays: number
    remainingDays: number
    thisYearCapDays: number
    thisYearUsedDays: number
    thisYearRemainingDays: number
    leaveYearStart: string
    leaveYearEnd: string
}

/**
 * The employee's ledger. The totals cover eligible children only, so they fall on
 * their own when a child turns 15.
 */
export interface ChildLeaveEntitlementSummary {
    /** Null when no active leave type carries a per-child entitlement. */
    leaveTypeId: number | null
    leaveTypeName: string
    eligibleChildCount: number
    totalRemainingDays: number
    thisYearCapDays: number
    thisYearRemainingDays: number
    children: ChildLeaveEntitlement[]
}
```

Add `export * from './child'` to `client/src/lib/types/index.ts`.

In `client/src/lib/types/leave-type.ts`, add to the `LeaveType` interface after `maxCarryoverDays`:

```ts
    /** When true this type's budget is per child, not per employee — see perChild* below. */
    perChildEntitlement: boolean
    /** Lifetime weeks per eligible child. 18 for paternity leave. */
    perChildTotalWeeks: number
    /** Weeks per eligible child per leave year. 5 for paternity leave. */
    perChildWeeksPerYear: number
    /** The age at which a child stops being eligible. 15 for paternity leave. */
    childEligibleUntilAge: number
```

In `client/src/lib/types/annual-leave.ts`, add `childId?: string | null` to `AnnualLeaveBase`, and `childId: string | null` plus `childName: string` to `AnnualLeave`.

In `client/src/lib/types/auth.ts`, add `hasChildren?: boolean | null` to `UpdateProfileRequest`, and the same to `UserInfo` (find the interface in that file — the dialog reads the current state from it).

- [ ] **Step 2: Write the API module**

Create `client/src/lib/api/children.ts`:

```ts
import apiClient from './client'
import type { Child, ChildLeaveEntitlementSummary, UpsertChildRequest } from '../types'

/**
 * `employeeId` is for an admin reading or editing someone else's children, and for
 * the on-behalf leave form. Omitted, every call is about the signed-in user. The
 * server authorizes it either way — passing an id is not permission to use it.
 */
export async function getChildren(employeeId?: string) {
    const response = await apiClient.get<Child[]>('/children', {
        params: employeeId ? { employeeId } : undefined,
    })
    return response.data
}

export async function createChild(request: UpsertChildRequest, employeeId?: string) {
    const response = await apiClient.post<Child>('/children', request, {
        params: employeeId ? { employeeId } : undefined,
    })
    return response.data
}

export async function updateChild(id: string, request: UpsertChildRequest) {
    const response = await apiClient.put<Child>(`/children/${id}`, request)
    return response.data
}

export async function deleteChild(id: string) {
    await apiClient.delete(`/children/${id}`)
}

export async function getChildLeaveEntitlements(employeeId?: string) {
    const response = await apiClient.get<ChildLeaveEntitlementSummary>('/children/entitlements', {
        params: employeeId ? { employeeId } : undefined,
    })
    return response.data
}
```

Add `export * from './children'` to `client/src/lib/api/index.ts`.

- [ ] **Step 3: Teach the allowance helper about per-child types**

In `client/src/lib/leave-allowance.ts`, append:

```ts
/**
 * How a leave type's budget reads on a card. A per-child type has no meaningful
 * `defaultAllowance` — it is 0 by migration — so quoting the usual "N days/year"
 * would render "— days/year" beside a type that grants 18 weeks per child.
 */
export function describeAllowance(type: LeaveType | undefined) {
    if (!type) return '—'

    if (type.perChildEntitlement) {
        return `${type.perChildTotalWeeks} weeks per child · max ${type.perChildWeeksPerYear} weeks/year`
    }

    const allowance = allowanceForLeaveType(type)
    return allowance > 0 ? `${allowance} ${type.allowanceUnit}` : '—'
}
```

Also update the module's header comment: the numbered list of "where an allowance comes from" should note that a type with `perChildEntitlement` has no per-employee allowance at all — its budget is per child and lives in `perChildTotalWeeks` / `perChildWeeksPerYear`, read through `describeAllowance`.

- [ ] **Step 4: Verify the client still builds**

```bash
cd /c/Practice/Own/2026/WorkTrack/client && npm run build && npm run lint
```
Expected: both clean. Any test fixture that constructs a `LeaveType` literal now fails to typecheck — `AllLeaveAdminPage.test.tsx:32` and `settingsPlacement.test.tsx:70` both build full `LeaveType` objects. Add the four new fields to those literals (`perChildEntitlement: false, perChildTotalWeeks: 0, perChildWeeksPerYear: 0, childEligibleUntilAge: 0`).

- [ ] **Step 5: Run the client tests**

```bash
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client
```
Expected: PASS, banner `RUN v3.2.7`. If the banner says `RUN v5.0.0` the wrong binary was resolved — use the absolute path above.

- [ ] **Step 6: Commit**

```bash
git add client/src/lib
git commit -m "Add child types, API module and per-child allowance helper

describeAllowance exists because a per-child type has no per-employee allowance
-- defaultAllowance is 0 by migration -- so the usual 'N days/year' would render
an em dash beside a type that grants 18 weeks per child."
```

---

### Task 13: Children in the Edit profile dialog

Requirements 1 and 2, on the surface chosen in design: the sidebar's existing "Edit profile" dialog, widened, with children as their own section.

**Files:**
- Create: `client/src/components/layout/ChildrenSection.tsx`
- Create: `client/src/components/layout/ChildrenSection.test.tsx`
- Modify: `client/src/components/layout/Sidebar.tsx` (dialog `maxWidth`, the new section, `hasChildren` in the form), `client/src/components/layout/index.ts`

**Interfaces:**
- Consumes: `getChildren`, `createChild`, `updateChild`, `deleteChild` (Task 12).
- Produces: `ChildrenSection` with props `{ hasChildren: boolean | null | undefined; onHasChildrenChange: (value: boolean) => void; disabled?: boolean }`. It owns its own queries and mutations — the parent only owns the checkbox value, because that saves with the profile.

- [ ] **Step 1: Write the failing test**

Create `client/src/components/layout/ChildrenSection.test.tsx`. Follow the setup in an existing component test (`client/src/components/admin/ComponentsPanel.test.tsx` is the closest shape — a panel with a list and mutations); copy its `QueryClientProvider` wrapper and its `vi.mock` of the API module rather than inventing a new harness.

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ChildrenSection from './ChildrenSection'
import type { Child } from '../../lib/types'

const children: Child[] = [
    { id: 'c1', name: 'Andreas', dateOfBirth: '2019-03-04', ageYears: 7, isEligible: true, lastEligibleDate: '2034-03-03' },
    { id: 'c2', name: 'Petros', dateOfBirth: '2005-01-20', ageYears: 21, isEligible: false, lastEligibleDate: '2020-01-19' },
]

const getChildren = vi.fn()
const createChild = vi.fn()
const deleteChild = vi.fn()

vi.mock('../../lib/api', () => ({
    getChildren: (...args: unknown[]) => getChildren(...args),
    createChild: (...args: unknown[]) => createChild(...args),
    updateChild: vi.fn(),
    deleteChild: (...args: unknown[]) => deleteChild(...args),
}))

function renderSection(hasChildren: boolean | null = true) {
    const onHasChildrenChange = vi.fn()
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={client}>
            <ChildrenSection hasChildren={hasChildren} onHasChildrenChange={onHasChildrenChange} />
        </QueryClientProvider>,
    )
    return { onHasChildrenChange }
}

describe('ChildrenSection', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        getChildren.mockResolvedValue(children)
        createChild.mockResolvedValue(children[0])
        deleteChild.mockResolvedValue(undefined)
    })

    it('lists each child with their computed age', async () => {
        renderSection()

        expect(await screen.findByText('Andreas')).toBeInTheDocument()
        expect(screen.getByText(/7/)).toBeInTheDocument()
        expect(screen.getByText('Petros')).toBeInTheDocument()
    })

    /**
     * An aged-out child is shown, not hidden: their leave is still history, and a
     * disappearing row reads as data loss.
     */
    it('marks a child over the age limit as no longer eligible', async () => {
        renderSection()

        await screen.findByText('Petros')
        expect(screen.getByText(/no longer eligible/i)).toBeInTheDocument()
    })

    it('hides the list when the employee says they have no children', async () => {
        renderSection(false)

        await waitFor(() => expect(screen.queryByText('Andreas')).not.toBeInTheDocument())
    })

    it('adds a child', async () => {
        const user = userEvent.setup()
        renderSection()
        await screen.findByText('Andreas')

        await user.click(screen.getByRole('button', { name: /add child/i }))
        await user.type(screen.getByLabelText(/child's name/i), 'Maria')
        await user.type(screen.getByLabelText(/date of birth/i), '2022-09-12')
        await user.click(screen.getByRole('button', { name: /^save child$/i }))

        await waitFor(() =>
            expect(createChild).toHaveBeenCalledWith({ name: 'Maria', dateOfBirth: '2022-09-12' }),
        )
    })

    /**
     * Ticking the box is the parent's business — it saves with the profile — so the
     * section only reports the change upward.
     */
    it('reports a change to the declaration rather than saving it', async () => {
        const user = userEvent.setup()
        const { onHasChildrenChange } = renderSection(false)

        await user.click(screen.getByRole('checkbox', { name: /i have children/i }))

        expect(onHasChildrenChange).toHaveBeenCalledWith(true)
    })
})
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/layout/ChildrenSection.test.tsx
```
Expected: FAIL — cannot resolve `./ChildrenSection`.

- [ ] **Step 3: Write the component**

Create `client/src/components/layout/ChildrenSection.tsx`. Use MUI 7 components already used elsewhere in the codebase (`Stack`, `TextField`, `Checkbox`, `FormControlLabel`, `IconButton`, `Chip`, `Typography`, `Button`, `Alert`, `CircularProgress`) and the shared `softBg` token helper from `../../lib/theme-tokens` for the chip backgrounds. Structure:

```tsx
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Chip from '@mui/material/Chip'
import CircularProgress from '@mui/material/CircularProgress'
import FormControlLabel from '@mui/material/FormControlLabel'
import IconButton from '@mui/material/IconButton'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import { createChild, deleteChild, getChildren } from '../../lib/api'
import { getApiErrorMessage } from '../../lib/api/error-utils'
import { softBg } from '../../lib/theme-tokens'

interface ChildrenSectionProps {
    /** null means the employee has never answered. */
    hasChildren: boolean | null | undefined
    onHasChildrenChange: (value: boolean) => void
    disabled?: boolean
}

/**
 * Children on the employee's own profile. What paternity leave is measured
 * against: 18 weeks per child, until that child turns 15.
 *
 * The rows commit immediately, while the "I have children" checkbox saves with the
 * rest of the profile. That split is deliberate — each child is its own resource,
 * and batching them into the dialog's Save would mean building a diff-and-sync
 * command for very little gain. Rows therefore show their own pending state.
 */
export default function ChildrenSection({ hasChildren, onHasChildrenChange, disabled }: ChildrenSectionProps) {
    const queryClient = useQueryClient()
    const [isAdding, setIsAdding] = useState(false)
    const [name, setName] = useState('')
    const [dateOfBirth, setDateOfBirth] = useState('')

    const declared = hasChildren === true

    const { data: children, isLoading } = useQuery({
        queryKey: ['children'],
        queryFn: () => getChildren(),
        enabled: declared,
    })

    const invalidate = () => {
        void queryClient.invalidateQueries({ queryKey: ['children'] })
        // The picker on the leave form reads the ledger, not the list.
        void queryClient.invalidateQueries({ queryKey: ['childLeaveEntitlements'] })
    }

    const addMutation = useMutation({
        mutationFn: () => createChild({ name: name.trim(), dateOfBirth }),
        onSuccess: () => {
            invalidate()
            setIsAdding(false)
            setName('')
            setDateOfBirth('')
        },
    })

    const removeMutation = useMutation({
        mutationFn: (id: string) => deleteChild(id),
        onSuccess: invalidate,
    })

    const today = new Date().toISOString().slice(0, 10)
    const error = addMutation.error ?? removeMutation.error
    const canSaveNew = name.trim().length > 0 && dateOfBirth.length > 0

    return (
        <Stack spacing={1}>
            <FormControlLabel
                control={
                    <Checkbox
                        checked={declared}
                        onChange={(event) => onHasChildrenChange(event.target.checked)}
                        disabled={disabled}
                    />
                }
                label="I have children"
            />

            {declared && (
                <Box sx={{ pl: 1 }}>
                    {isLoading && <CircularProgress size={18} />}

                    {children?.map((child) => (
                        <Stack
                            key={child.id}
                            direction="row"
                            spacing={1}
                            alignItems="center"
                            sx={{ py: 0.5 }}
                        >
                            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                                <Typography variant="body2" fontWeight={600} noWrap>
                                    {child.name}
                                </Typography>
                                <Typography variant="caption" color="text.secondary">
                                    {`${new Date(child.dateOfBirth).toLocaleDateString('en-GB', {
                                        day: '2-digit', month: 'short', year: 'numeric',
                                    })} · Age ${child.ageYears}`}
                                </Typography>
                            </Box>

                            <Chip
                                size="small"
                                label={child.isEligible ? 'Eligible' : 'No longer eligible'}
                                sx={{
                                    bgcolor: child.isEligible ? softBg('success') : softBg('default'),
                                    color: child.isEligible ? 'success.dark' : 'text.secondary',
                                }}
                            />

                            <IconButton
                                size="small"
                                aria-label={`Remove ${child.name}`}
                                onClick={() => removeMutation.mutate(child.id)}
                                disabled={disabled || removeMutation.isPending}
                            >
                                <DeleteOutlineRoundedIcon fontSize="small" />
                            </IconButton>
                        </Stack>
                    ))}

                    {isAdding ? (
                        <Stack spacing={1} sx={{ mt: 1 }}>
                            <TextField
                                label="Child's name"
                                value={name}
                                onChange={(event) => setName(event.target.value)}
                                size="small"
                                fullWidth
                            />
                            <TextField
                                label="Date of birth"
                                type="date"
                                value={dateOfBirth}
                                onChange={(event) => setDateOfBirth(event.target.value)}
                                slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: today } }}
                                size="small"
                                fullWidth
                            />
                            <Stack direction="row" spacing={1}>
                                <Button
                                    variant="contained"
                                    size="small"
                                    onClick={() => addMutation.mutate()}
                                    disabled={!canSaveNew || addMutation.isPending}
                                    sx={{ textTransform: 'none' }}
                                >
                                    Save child
                                </Button>
                                <Button
                                    variant="outlined"
                                    size="small"
                                    onClick={() => setIsAdding(false)}
                                    disabled={addMutation.isPending}
                                    sx={{ textTransform: 'none' }}
                                >
                                    Cancel
                                </Button>
                            </Stack>
                        </Stack>
                    ) : (
                        <Button
                            size="small"
                            onClick={() => setIsAdding(true)}
                            disabled={disabled}
                            sx={{ textTransform: 'none', mt: 0.5 }}
                        >
                            Add child
                        </Button>
                    )}

                    {error && (
                        <Alert severity="error" sx={{ mt: 1 }}>
                            {getApiErrorMessage(error, 'Unable to update children.')}
                        </Alert>
                    )}
                </Box>
            )}
        </Stack>
    )
}
```

Two behaviours the tests pin: the server's delete refusal ("… has N leave request(s) recorded against them and cannot be removed") must surface through that `Alert`, and an ineligible child must render the literal text "No longer eligible".

`max={today}` on the date input keeps a future birth date untypeable. The server refuses one too, but a bounded input is kinder than an error.

Export it from `client/src/components/layout/index.ts` alongside the existing exports.

- [ ] **Step 4: Run the test to verify it passes**

```bash
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/layout/ChildrenSection.test.tsx
```
Expected: PASS, 5 tests.

- [ ] **Step 5: Wire it into the dialog**

In `client/src/components/layout/Sidebar.tsx`:

1. Change the Edit profile dialog's `maxWidth="xs"` to `maxWidth="sm"` (around line 413) — the children rows do not fit in `xs`.
2. Add `hasChildren` to the profile form's state. The dialog uses react-hook-form (`register`, `errors`), but this is a checkbox whose value comes from `authStore.user`, so hold it in local state seeded from `authStore.user?.hasChildren` on open, exactly as the component's other non-text state is handled.
3. Render the section after the Date of birth field and before the Department field:

```tsx
                        <ChildrenSection
                            hasChildren={hasChildren}
                            onHasChildrenChange={setHasChildren}
                            disabled={updateProfileMutation.isPending}
                        />
```

4. Include `hasChildren` in the payload passed to `updateProfileMutation` in `onEditSubmit`.
5. Refresh `authStore.user` after a successful save the same way the existing code does, so reopening the dialog shows the saved declaration.

If unchecking the box while children exist, the server answers 400 with "Remove the N child(ren) on your profile before saying you have none." — that arrives through the existing `updateProfileMutation.isError` `Alert`, so no new error handling is needed.

- [ ] **Step 6: Verify the build and the whole client suite**

```bash
cd /c/Practice/Own/2026/WorkTrack/client && npm run build && npm run lint
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client
```
Expected: build and lint clean, all client tests pass.

- [ ] **Step 7: Commit**

```bash
git add client/src/components/layout
git commit -m "Add children to the Edit profile dialog

Rows commit immediately while the 'I have children' checkbox saves with the
profile: each child is its own resource, and batching them into the dialog's
Save would mean a diff-and-sync command for very little gain. An aged-out child
is shown as no longer eligible rather than hidden -- their leave is still
history, and a disappearing row reads as data loss."
```

---

### Task 14: The child picker on the leave request form

Where an employee actually spends the entitlement. Its own component because `AnnualLeaveForm.tsx` is already 502 lines.

**Files:**
- Create: `client/src/components/annual-leave/ChildLeavePicker.tsx`, `client/src/components/annual-leave/ChildLeavePicker.test.tsx`
- Modify: `client/src/components/annual-leave/AnnualLeaveForm.tsx`, `client/src/lib/validation/leave.ts`, `client/src/components/annual-leave/index.ts`

**Interfaces:**
- Consumes: `getChildLeaveEntitlements` (Task 12), `ChildLeaveEntitlementSummary` (Task 12).
- Produces: `ChildLeavePicker` with props
  `{ value: string; onChange: (childId: string) => void; employeeId?: string; requestedDays: number | null; error?: string; disabled?: boolean }`,
  and `buildAnnualLeaveSchema(requireEmployee: boolean, requireChild: boolean)` — note the **second parameter**, and `AnnualLeaveFormValues` gains `childId: string`.

- [ ] **Step 1: Write the failing test**

Create `client/src/components/annual-leave/ChildLeavePicker.test.tsx`:

```tsx
import { fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ChildLeavePicker from './ChildLeavePicker'
import type { ChildLeaveEntitlementSummary } from '../../lib/types'

const getChildLeaveEntitlements = vi.fn()

vi.mock('../../lib/api', () => ({
    getChildLeaveEntitlements: (...args: unknown[]) => getChildLeaveEntitlements(...args),
}))

const summary: ChildLeaveEntitlementSummary = {
    leaveTypeId: 1,
    leaveTypeName: 'Paternity Leave',
    eligibleChildCount: 1,
    totalRemainingDays: 65,
    thisYearCapDays: 25,
    thisYearRemainingDays: 25,
    children: [
        {
            childId: 'c1', name: 'Andreas', dateOfBirth: '2019-03-04', ageYears: 7,
            isEligible: true, lastEligibleDate: '2034-03-03',
            totalDays: 90, totalWeeks: 18, usedDays: 25, remainingDays: 65,
            thisYearCapDays: 25, thisYearUsedDays: 0, thisYearRemainingDays: 25,
            leaveYearStart: '2026-01-01T00:00:00', leaveYearEnd: '2026-12-31T00:00:00',
        },
        {
            childId: 'c2', name: 'Petros', dateOfBirth: '2005-01-20', ageYears: 21,
            isEligible: false, lastEligibleDate: '2020-01-19',
            totalDays: 0, totalWeeks: 0, usedDays: 40, remainingDays: 0,
            thisYearCapDays: 0, thisYearUsedDays: 0, thisYearRemainingDays: 0,
            leaveYearStart: '2026-01-01T00:00:00', leaveYearEnd: '2026-12-31T00:00:00',
        },
    ],
}

function renderPicker(overrides: Partial<React.ComponentProps<typeof ChildLeavePicker>> = {}) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
        <QueryClientProvider client={client}>
            <ChildLeavePicker value="" onChange={vi.fn()} requestedDays={null} {...overrides} />
        </QueryClientProvider>,
    )
}

describe('ChildLeavePicker', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        getChildLeaveEntitlements.mockResolvedValue(summary)
    })

    /**
     * A MUI Select opens on mouseDown, not click, and renders its options into a
     * portal — screen queries still reach them, but only once it is open.
     */
    async function openPicker() {
        fireEvent.mouseDown(await screen.findByRole('combobox', { name: /child/i }))
        return screen.findByRole('listbox')
    }

    it('quotes each eligible child their own remaining entitlement', async () => {
        renderPicker()
        await openPicker()

        expect(screen.getByText(/Andreas/)).toBeInTheDocument()
        expect(screen.getByText(/65 of 90 days left/)).toBeInTheDocument()
        expect(screen.getByText(/25 left this year/)).toBeInTheDocument()
    })

    /**
     * Shown disabled with the reason rather than hidden: an employee who cannot
     * find their child assumes the data is missing, not that the child aged out.
     */
    it('shows an aged-out child disabled, with the reason', async () => {
        renderPicker()
        await openPicker()

        const agedOut = screen.getByText(/Petros/)
        expect(agedOut).toBeInTheDocument()
        expect(screen.getByText(/no longer eligible/i)).toBeInTheDocument()
        // aria-disabled rather than the disabled attribute: MUI marks a disabled
        // MenuItem for assistive tech and keeps it in the list.
        expect(agedOut.closest('li')).toHaveAttribute('aria-disabled', 'true')
    })

    it('says where to add children when none are declared', async () => {
        getChildLeaveEntitlements.mockResolvedValue({ ...summary, eligibleChildCount: 0, children: [] })

        renderPicker()

        expect(await screen.findByText(/add your children in edit profile/i)).toBeInTheDocument()
    })

    it('explains when children exist but none are eligible', async () => {
        getChildLeaveEntitlements.mockResolvedValue({
            ...summary,
            eligibleChildCount: 0,
            children: [summary.children[1]],
        })

        renderPicker()

        expect(await screen.findByText(/no eligible children/i)).toBeInTheDocument()
    })

    it('shows what the chosen dates would leave', async () => {
        renderPicker({ value: 'c1', requestedDays: 6 })

        expect(await screen.findByText(/6 business days \(1\.2 weeks\)/)).toBeInTheDocument()
        expect(screen.getByText(/59 days left/)).toBeInTheDocument()
    })
})
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/annual-leave/ChildLeavePicker.test.tsx
```
Expected: FAIL — cannot resolve `./ChildLeavePicker`.

- [ ] **Step 3: Write the picker**

Create `client/src/components/annual-leave/ChildLeavePicker.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query'
import Alert from '@mui/material/Alert'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { getChildLeaveEntitlements } from '../../lib/api'
import type { ChildLeaveEntitlement } from '../../lib/types'

interface ChildLeavePickerProps {
    value: string
    onChange: (childId: string) => void
    /** An admin filing on behalf of someone else. Omitted for one's own request. */
    employeeId?: string
    /** Business days the chosen dates come to, or null while they are incomplete. */
    requestedDays: number | null
    error?: string
    disabled?: boolean
}

const BUSINESS_DAYS_PER_WEEK = 5

function formatDate(iso: string) {
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })
}

function weeks(days: number) {
    return (Math.round((days / BUSINESS_DAYS_PER_WEEK) * 10) / 10).toFixed(1)
}

function optionLabel(child: ChildLeaveEntitlement) {
    return child.isEligible
        ? `${child.name} — ${child.remainingDays} of ${child.totalDays} days left · ${child.thisYearRemainingDays} left this year`
        : `${child.name} — turned ${child.ageYears} on ${formatDate(child.lastEligibleDate)} (no longer eligible)`
}

/**
 * The child a per-child leave request is for — paternity leave in practice, where
 * the entitlement is 18 weeks per child until that child turns 15.
 *
 * Ineligible children are listed and disabled rather than filtered out. An employee
 * who cannot find their child in the list assumes the record is missing; one who
 * sees them greyed out with a date understands why.
 *
 * Every figure here is advisory. The server re-checks both caps on create and again
 * on approval, and its message is the one that counts.
 */
export default function ChildLeavePicker({
    value, onChange, employeeId, requestedDays, error, disabled,
}: ChildLeavePickerProps) {
    const { data, isLoading } = useQuery({
        queryKey: ['childLeaveEntitlements', employeeId ?? 'me'],
        queryFn: () => getChildLeaveEntitlements(employeeId),
    })

    const children = data?.children ?? []
    const hasEligible = children.some((child) => child.isEligible)
    const selected = children.find((child) => child.childId === value)

    if (!isLoading && children.length === 0) {
        return (
            <Alert severity="info">
                Add your children in Edit profile to request this leave — the entitlement
                is per child.
            </Alert>
        )
    }

    if (!isLoading && !hasEligible) {
        return (
            <Alert severity="warning">
                No eligible children. This leave is available only while a child is under
                15.
            </Alert>
        )
    }

    return (
        <Stack spacing={0.5}>
            <TextField
                select
                required
                label="Child"
                value={value}
                onChange={(event) => onChange(event.target.value)}
                error={!!error}
                helperText={error}
                disabled={disabled || isLoading}
                fullWidth
            >
                {children.map((child) => (
                    <MenuItem key={child.childId} value={child.childId} disabled={!child.isEligible}>
                        {optionLabel(child)}
                    </MenuItem>
                ))}
            </TextField>

            {selected && requestedDays !== null && requestedDays > 0 && (
                <Typography variant="caption" color="text.secondary">
                    {`This request: ${requestedDays} business days (${weeks(requestedDays)} weeks) · `}
                    {`${selected.name}: ${Math.max(0, selected.remainingDays - requestedDays)} days left`}
                </Typography>
            )}
        </Stack>
    )
}
```

Export it from `client/src/components/annual-leave/index.ts`.

- [ ] **Step 4: Run the test to verify it passes**

```bash
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/annual-leave/ChildLeavePicker.test.tsx
```
Expected: PASS, 5 tests.

- [ ] **Step 5: Add `childId` to the form schema**

In `client/src/lib/validation/leave.ts`, change the signature and add the field:

```ts
export function buildAnnualLeaveSchema(requireEmployee: boolean, requireChild: boolean) {
    return z
        .object({
            employeeId: requireEmployee
                ? z.string().min(1, 'Please select an employee.')
                : z.string(),
            // Required only for a leave type with a per-child entitlement. The
            // server decides for real — it clears the id on any other type — so
            // this is the form-level affordance, not the rule.
            childId: requireChild
                ? z.string().min(1, 'Please select the child this leave is for.')
                : z.string(),
            startDate: z.string().min(1, 'Start date is required.'),
            // ... rest unchanged
```

and add `childId: string` to `AnnualLeaveFormValues`. Update the doc comment's bullet list with the new rule.

- [ ] **Step 6: Wire the picker into the form**

In `client/src/components/annual-leave/AnnualLeaveForm.tsx`:

1. Import `ChildLeavePicker`, `getChildLeaveEntitlements` is not needed here — the picker owns its query.
2. Watch the selected type and derive the flag:

```tsx
    const watchedLeaveTypeId = watch('leaveTypeId')
    const watchedEmployeeId = watch('employeeId')
    const selectedLeaveType = leaveTypes?.find((type) => type.id === watchedLeaveTypeId)
    const requiresChild = selectedLeaveType?.perChildEntitlement === true
```

3. Pass it to the schema — the memo now depends on both flags:

```tsx
    const schema = useMemo(
        () => buildAnnualLeaveSchema(requireEmployee, requiresChild),
        [requireEmployee, requiresChild],
    )
```

4. Add `childId: leave?.childId ?? ''` to `buildDefaults()`.
5. Compute the request's business days for the caption — weekdays only, holidays ignored, because the client has no holiday list and the server is authoritative:

```tsx
    const watchedStart = watch('startDate')
    const watchedEnd = watch('endDate')

    /**
     * Weekday count for the caption under the picker. Public holidays are NOT
     * excluded — the client has no holiday list — so this can read one or two days
     * high near a holiday. The server's figure is the one that counts, and it only
     * ever comes out lower, so the caption never over-promises what is left.
     */
    const requestedDays = useMemo(() => {
        if (!watchedStart || !watchedEnd) return null
        const start = new Date(watchedStart)
        const end = new Date(watchedEnd)
        if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end < start) return null

        let count = 0
        for (const date = new Date(start); date <= end; date.setDate(date.getDate() + 1)) {
            const day = date.getDay()
            if (day !== 0 && day !== 6) count++
        }
        return count
    }, [watchedStart, watchedEnd])
```

6. Render the picker inside the form, immediately after the leave-type field, wrapped in `{requiresChild && (...)}`, using the `Controller` pattern the file already uses for its other fields:

```tsx
                    {requiresChild && (
                        <Controller
                            name="childId"
                            control={control}
                            render={({ field, fieldState }) => (
                                <ChildLeavePicker
                                    value={field.value}
                                    onChange={field.onChange}
                                    employeeId={isAdmin && !isEdit ? watchedEmployeeId || undefined : undefined}
                                    requestedDays={requestedDays}
                                    error={fieldState.error?.message}
                                    disabled={readOnly || isPending}
                                />
                            )}
                        />
                    )}
```

7. Include `childId` in the create and edit payloads — send `undefined` when `!requiresChild` so the server is not handed a stale id it would only discard:

```tsx
        childId: requiresChild ? values.childId : undefined,
```

8. When `readOnly` and the leave has a `childName`, show it as a plain disabled `TextField` labelled "Child" instead of the picker, matching how the other read-only fields render.

- [ ] **Step 7: Verify the build and the full client suite**

```bash
cd /c/Practice/Own/2026/WorkTrack/client && npm run build && npm run lint
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client
```
Expected: clean. `buildAnnualLeaveSchema` gained a required second parameter — any other caller must be updated; grep for it before building.

- [ ] **Step 8: Commit**

```bash
git add client/src/components/annual-leave client/src/lib/validation/leave.ts
git commit -m "Add the child picker to the leave request form

Ineligible children are listed and disabled with the date they aged out rather
than filtered out: an employee who cannot find their child assumes the record is
missing. The caption's day count ignores public holidays, so it reads high near
one and never over-promises what is left -- the server's figure is lower and it
is the one enforced."
```

---

### Task 15: The per-child fields on Leave Types

**Files:**
- Modify: `client/src/components/admin/LeaveTypesPanel.tsx`

**Interfaces:**
- Consumes: the four `LeaveType` fields (Task 12).
- Produces: no new exports.

- [ ] **Step 1: Add the fields to the editor form**

In `client/src/components/admin/LeaveTypesPanel.tsx`, the edit dialog's local state block (around line 747, beside `const [eligibilityScope, setEligibilityScope] = useState(...)`) gains:

```tsx
    const [perChildEntitlement, setPerChildEntitlement] = useState(i?.perChildEntitlement ?? false)
    const [perChildTotalWeeks, setPerChildTotalWeeks] = useState(i?.perChildTotalWeeks ?? 18)
    const [perChildWeeksPerYear, setPerChildWeeksPerYear] = useState(i?.perChildWeeksPerYear ?? 5)
    const [childEligibleUntilAge, setChildEligibleUntilAge] = useState(i?.childEligibleUntilAge ?? 15)
```

The defaults are the paternity policy: a new per-child type starts from the configured one rather than from zeros, which the server would refuse anyway.

Add all four to the payload built around line 768, beside `eligibilityScope`.

- [ ] **Step 2: Render the toggle and its dependents**

Add to the dialog's form, after the allowance and carryover fields (so the three budget numbers sit together):

```tsx
                        <FormControlLabel
                            control={
                                <Switch
                                    checked={perChildEntitlement}
                                    onChange={(e) => {
                                        const on = e.target.checked
                                        setPerChildEntitlement(on)
                                        // A per-child type keeps its own ledger and must not
                                        // also be deducted from the pooled balance -- the
                                        // server refuses that combination outright.
                                        if (on) setAffectsBalance(false)
                                    }}
                                    slotProps={{ input: { role: 'switch', 'aria-label': 'Per-child entitlement' } }}
                                />
                            }
                            label="Per-child entitlement"
                        />

                        {perChildEntitlement && (
                            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                                <TextField
                                    label="Total per child"
                                    type="number"
                                    value={perChildTotalWeeks}
                                    onChange={(e) => setPerChildTotalWeeks(Number(e.target.value))}
                                    slotProps={{ htmlInput: { min: 1, max: 260 }, input: { endAdornment: <InputAdornment position="end">weeks</InputAdornment> } }}
                                    helperText={`${perChildTotalWeeks * 5} business days`}
                                    fullWidth
                                />
                                <TextField
                                    label="Max per year, per child"
                                    type="number"
                                    value={perChildWeeksPerYear}
                                    onChange={(e) => setPerChildWeeksPerYear(Number(e.target.value))}
                                    slotProps={{ htmlInput: { min: 1, max: 52 }, input: { endAdornment: <InputAdornment position="end">weeks</InputAdornment> } }}
                                    helperText={`${perChildWeeksPerYear * 5} business days`}
                                    fullWidth
                                />
                                <TextField
                                    label="Eligible until age"
                                    type="number"
                                    value={childEligibleUntilAge}
                                    onChange={(e) => setChildEligibleUntilAge(Number(e.target.value))}
                                    slotProps={{ htmlInput: { min: 1, max: 30 }, input: { endAdornment: <InputAdornment position="end">years</InputAdornment> } }}
                                    fullWidth
                                />
                            </Stack>
                        )}
```

Note the `slotProps={{ input: { role: 'switch', ... } }}` on the `Switch`: in MUI 7 passing `slotProps.input` **replaces** the default input props, dropping the `role="switch"` MUI would otherwise add — so it has to be restated, or the accessible role disappears and `getByRole('switch')` stops finding it. Follow whatever the file's existing switches do; if they use `inputProps`, note that MUI 7's `Switch` ignores it.

`FormControlLabel`, `Switch`, `InputAdornment` and `Stack` may need importing — check the file's existing imports first.

- [ ] **Step 3: Quote the policy on the type's card**

The list row currently renders the allowance from `defaultAllowance` and `allowanceUnit`. For a per-child type that is `0 weeks/child`, which reads as nothing. Replace that expression with `describeAllowance(t)` from `../../lib/leave-allowance`, which renders "18 weeks per child · max 5 weeks/year" for a per-child type and the unchanged "25 days/year" for everything else.

Also disable the `Affects balance` switch while `perChildEntitlement` is on, with a helper caption explaining that a per-child type keeps its own ledger — the server refuses the combination, and a disabled control is a better answer than a validation error.

- [ ] **Step 4: Verify**

```bash
cd /c/Practice/Own/2026/WorkTrack/client && npm run build && npm run lint
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client
```
Expected: clean, all tests pass. `settingsPlacement.test.tsx` asserts which controls appear on which settings screen — if it fails, read the assertion: the per-child fields belong on Leave Types, never on Leave Settings.

- [ ] **Step 5: Commit**

```bash
git add client/src/components/admin/LeaveTypesPanel.tsx
git commit -m "Add the per-child entitlement fields to Leave Types

The three numbers sit beside the allowance they replace, revealed by their own
toggle. Turning the toggle on clears 'affects balance' and disables it: a
per-child type keeps its own ledger, and a disabled control is a better answer
than a validation error."
```

---

### Task 16: Quote the entitlement where employees look for it

**Files:**
- Modify: `client/src/components/annual-leave/ApplyLeavePage.tsx`, `client/src/components/annual-leave/MyLeavePage.tsx`

**Interfaces:**
- Consumes: `describeAllowance` (Task 12), `getChildLeaveEntitlements` (Task 12).
- Produces: no new exports.

- [ ] **Step 1: Fix the leave-type card on Apply Leave**

In `client/src/components/annual-leave/ApplyLeavePage.tsx`, find where a leave type card quotes its allowance (search for `defaultAllowance` and `allowanceUnit`) and replace the expression with `describeAllowance(type)` from `../../lib/leave-allowance`. Without this, the Paternity card reads "— days/year" or "0 weeks/child" beside a type that grants 18 weeks per child, because the migration set `defaultAllowance` to 0 on purpose.

- [ ] **Step 2: Add the per-child card to My Leave**

In `client/src/components/annual-leave/MyLeavePage.tsx`, add a card beside the existing balance summary, rendered only when the ledger has a per-child leave type and at least one child:

```tsx
    const { data: childEntitlements } = useQuery({
        queryKey: ['childLeaveEntitlements', 'me'],
        queryFn: () => getChildLeaveEntitlements(),
    })
```

Render it inside whatever card wrapper the file already uses for its balance summary (match the surrounding `Paper`/`Card` and `sx` conventions rather than introducing a new one):

```tsx
{childEntitlements?.leaveTypeId && childEntitlements.children.length > 0 && (
    <Stack spacing={1}>
        <Box>
            <Typography variant="subtitle2" fontWeight={700}>
                {childEntitlements.leaveTypeName}
            </Typography>
            <Typography variant="caption" color="text.secondary">
                {`Leave year ${new Date(childEntitlements.children[0].leaveYearStart)
                    .toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })} – ${new Date(
                    childEntitlements.children[0].leaveYearEnd,
                ).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })}`}
            </Typography>
        </Box>

        {childEntitlements.children.map((child) => (
            <Stack
                key={child.childId}
                direction="row"
                justifyContent="space-between"
                alignItems="baseline"
                sx={{ opacity: child.isEligible ? 1 : 0.6 }}
            >
                <Typography variant="body2">
                    {`${child.name} · age ${child.ageYears}`}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                    {child.isEligible
                        ? `${child.remainingDays} of ${child.totalDays} days left · ${child.thisYearRemainingDays} of ${child.thisYearCapDays} this year`
                        : `no longer eligible · ${child.usedDays} days used`}
                </Typography>
            </Stack>
        ))}

        <Typography variant="caption" color="text.secondary">
            {`${childEntitlements.eligibleChildCount} eligible child(ren) · ${childEntitlements.totalRemainingDays} days remaining in total`}
        </Typography>
    </Stack>
)}
```

The guard covers both empty cases: an employee with no children, and a company where no active leave type carries a per-child entitlement (`leaveTypeId` is null). Either way nothing renders, rather than an empty card. The leave-year window is stated once so "this year" is unambiguous, and an ineligible child is dimmed with the days they used rather than dropped.

- [ ] **Step 3: Verify**

```bash
cd /c/Practice/Own/2026/WorkTrack/client && npm run build && npm run lint
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client
```
Expected: clean, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add client/src/components/annual-leave
git commit -m "Quote the per-child entitlement on Apply Leave and My Leave

The Paternity card read '0 weeks/child' because the migration zeroes the flat
allowance on purpose; describeAllowance quotes the per-child policy instead. My
Leave gains a ledger card naming the leave-year window, so 'this year' is
unambiguous."
```

---

### Task 17: Document it and verify the whole thing

CLAUDE.md is the file that tells the next person why the app is shaped this way. A second leave ledger is exactly the kind of thing it exists to explain.

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add `Child` to the domain-model table**

In the entity table, after the `EmployeeProfile` row:

```markdown
| `Child` | One declared child of an `EmployeeProfile`: `Name`, `DateOfBirth`. **Age and eligibility are never stored** — both are computed on every read (`PerChildLeaveCalculationService`), which is what makes a child aging out of paternity leave automatic. Deleting a child with leave against them is refused (`DeleteChild`, and the FK is `Restrict`): the row is what the per-child ledger is queried by, so removing it would erase the record of leave actually taken. An aged-out child is kept and reads as ineligible |
```

Update the `EmployeeProfile` row to mention `HasChildren` as a tri-state (`null` = never asked, `false` = declared none, `true` = has some), refused as `false` while children exist. Update the `AnnualLeave` row to mention `ChildId` (nullable; required on a per-child type, and `null` on rows predating the feature, which count against no ledger). Update the `LeaveType` row to mention `PerChildEntitlement` and the three numbers.

- [ ] **Step 2: Document the second ledger**

Add a subsection after "Leave is configured once, for everyone, on Leave Types":

```markdown
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
  `<= 0`. The validator refuses a 0 either way, but the safe direction is the default.
- **The per-child check runs at creation even when the type requires approval**,
  unlike the pooled check, which only runs at creation for an auto-approving type. A
  per-child refusal is something the employee can act on; waiting days for a manager
  to hit it helps nobody. It is re-checked on the transition into `Approved` — so, as
  with the pooled balance, several *pending* requests can each pass creation and the
  second *approval* is what fails.
```

- [ ] **Step 3: Run the entire verification pass**

Backend:
```bash
DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd
```
Expected: PASS. Roughly 713 pre-existing tests plus about 50 added by this plan.

Frontend:
```bash
cd /c/Practice/Own/2026/WorkTrack/client && npm run build && npm run lint
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client
```
Expected: build clean, lint clean, all tests pass with banner `RUN v3.2.7`.

Migrations apply cleanly:
```bash
export UseArtifactsOutput=true ArtifactsPath=C:/temp/wt-ef
dotnet restore Annualleave.sln
dotnet ef database update --project Persistence --startup-project API
```
Expected: `AddChildLeaveEntitlement` and `ConfigurePaternityPerChildEntitlement` apply with no error. Then confirm the row moved:
```bash
dotnet ef dbcontext info --project Persistence --startup-project API
```
and check Paternity Leave in the running app's Leave Types screen reads "18 weeks per child · max 5 weeks/year".

Do **not** report the work complete until all three command groups have been run and their output seen. If any fails, fix it before claiming completion — an unverified success claim is worse than a known failure.

- [ ] **Step 4: Manual smoke test**

Start both halves (`dotnet run --project API` and `npm run dev` in `client/`) and walk the flow:

1. Sign in as an employee. Edit profile → tick "I have children" → add a child born ~5 years ago and one born ~16 years ago. Save.
2. Request Leave → choose Paternity Leave. The child picker appears; the young child is selectable with "90 of 90 days left", the older one is disabled with the date they aged out.
3. Book 5 weeks of weekdays for the young child. It is accepted.
4. Book one more day in the same leave year for that child. It is refused, naming the leave-year window and 0 days left.
5. My Leave shows the ledger: 25 used, 65 remaining, 0 left this year.
6. Try to remove that child in Edit profile. Refused, saying leave is recorded against them.
7. As an admin, Leave Types → Paternity Leave shows the toggle on with 18 / 5 / 15, and "Affects balance" disabled.

If the API dies moments after launch, check `API/Logs/worktrack-<date>.jsonl` for "Database migration or seeding failed" — that is SQL Server not yet up, and the fix is to start it again.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "Document the per-child leave ledger

A second ledger needed explaining: what it is, why it is a projection rather
than a stored balance, why a 0 refuses everything where the pooled entitlement's
0 disables the check, and why the per-child check runs at creation even for a
type that requires approval."
```

---

## Notes for whoever executes this

**Order matters.** Tasks 1–2 are the foundation; 5 must land before 6–7 (the calculator calls the extracted helper); 8 needs 6–7; 12 must land before 13–16. Within the frontend, 13 and 14 are independent of each other.

**Two things the spec deliberately does not do**, so don't add them:
- No admin UI for another employee's children. The API allows it (an admin can correct a record without a deploy) but no screen is built.
- No gating on `EligibilityScope` / `EligibilityNotes`. Nothing enforces "Male employees" today, and these rules are gender-neutral.

**If a task turns out bigger than it reads**, stop and say so rather than half-finishing it. The likely candidates are Task 14 (`AnnualLeaveForm.tsx` is 502 lines and the picker touches its schema, defaults and payload) and Task 16 (`ApplyLeavePage.tsx` is 1347 lines — find the card, change the one expression, resist tidying the rest).

