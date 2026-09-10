# Per-Child Paternity Leave Entitlement — Design

Date: 2026-09-10
Status: Approved, ready for implementation planning

## Problem

`Paternity Leave` exists today as an ordinary `LeaveType` seeded with
`DefaultAllowance = 14`, `AllowanceUnit = "days/event"`, `MaxConsecutiveDays = 14`,
`AffectsBalance = false` (`Persistence/DbInitializer.cs`). That is a single flat number
per employee, and it cannot express the actual policy:

- 18 weeks of leave **per child**.
- Available only while that child is **under 15 years old**.
- At most **5 weeks per year, per eligible child**.
- Usage tracked **separately for each child**, with a remaining figure per child.
- When a child turns 15 they become ineligible **regardless of how much they had used**,
  and the employee's totals fall accordingly.

The enforced pool in the app today is one integer, `EmployeeProfile.LeaveBalance`, kept
by `AnnualLeaveBalanceCalculator`. A per-child entitlement cannot ride on that column;
it needs a second ledger, derived from approved leave rows rather than stored as a
running total.

## Decisions taken

These were settled before design and are not open questions:

| Question | Decision |
|---|---|
| What is one "week" worth? | **5 business days.** 18 weeks = 90 business days per child; 5 weeks/year = 25 business days/year. Weekends and public holidays inside a request do not consume entitlement. |
| Which "year" does the 5-week cap reset on? | The **configured leave year** (`AppSettings.LeaveYearStartMonth`), exactly as the annual-leave check does. A request straddling the boundary is split and checked against both years. |
| A request straddling the 15th birthday? | **Refused.** The child must still be under 15 on the request's **end date**, so no request is ever part-eligible. The message names the last bookable date. |
| Where are children entered? | In the existing **"Edit profile" dialog** in the sidebar (widened `xs` to `sm`). No new page. |
| Where do 18 / 5 / 15 live? | **New columns on `LeaveType`**, gated by a `PerChildEntitlement` toggle — following CLAUDE.md's "leave is configured once, on Leave Types". |
| Pre-existing paternity rows with no child? | **Left unattached.** `ChildId` is nullable; old rows stay approved and visible but count against no child's ledger. An admin can edit one later to attach a child. |

## Non-goals

- Maternity Leave keeps its flat model. The toggle exists for it but stays **off**.
- No admin **UI** for editing *another* employee's children in this pass. The API
  supports it (see Authorization) so that a correction is possible without a deploy, but
  no screen is built for it. An admin assigning leave on behalf of someone still picks
  from that person's declared children.
- `EligibilityScope` / `EligibilityNotes` stay decorative. Nothing enforces
  "Male employees" today, and the rules above are gender-neutral; no gating is added.
- No half-days and no proration for per-child leave types.
- No change to the annual-leave pool. Paternity remains `AffectsBalance = false`.

## Architecture

Follows the existing layering — `Domain` (pure rules), `Persistence`, `Application`
(MediatR CQRS over `AppDbContext`, no repository), `API` (thin controllers), `client`.

```
Domain/Child.cs                                    new entity
Domain/EmployeeProfile.cs                          + HasChildren
Domain/AnnualLeave.cs                              + ChildId / Child
Domain/LeaveType.cs                                + 4 per-child columns
Domain/Services/PerChildLeaveCalculationService.cs pure arithmetic (new)
Domain/Services/LeaveCalculationService.cs         reused unchanged

Persistence/AppDbContext.cs                        DbSet<Child> + configuration
Persistence/Migrations/...AddChildLeaveEntitlement schema
Persistence/Migrations/...ConfigurePaternityPerChildEntitlement  data
Persistence/DbInitializer.cs                       leave-type spec map kept in step

Application/Children/...                           new slice (commands, queries, DTOs, validators)
Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs   new
Application/AnnualLeaves/Commands/LeaveYearQueries.cs                 extracted shared DB helpers
Application/AnnualLeaves/Commands/{CreateAnnualLeave,EditAnnualLeave,UpdateLeaveStatus}.cs  wired
Application/AnnualLeaves/DTOs + Validators         + ChildId
Application/LeaveTypes/...                         + 4 fields, validation
Application/Accounts/...                           + HasChildren on the profile update

API/Controllers/ChildrenController.cs              new

client/src/lib/types/child.ts                      new
client/src/lib/api/children.ts                     new
client/src/components/layout/Sidebar.tsx           children section in Edit profile
client/src/components/annual-leave/AnnualLeaveForm.tsx  child picker
client/src/components/annual-leave/ApplyLeavePage.tsx   per-child card copy
client/src/components/annual-leave/MyLeavePage.tsx      per-child entitlement card
client/src/components/admin/LeaveTypesPanel.tsx         toggle + 3 numbers
client/src/lib/leave-allowance.ts                       per-child unit awareness
```

### Why age is never stored

`Child` stores `DateOfBirth` only. Age and eligibility are computed on every read. This
is what satisfies requirements 3, 4, 9 and 10 with no moving parts: a child ages out at
15 with no nightly job, no `IsEligible` column to go stale, and the employee's totals
recompute on the next read because they are a projection over eligible children rather
than a stored sum. Nothing needs to "recalculate" because nothing was cached.

## Domain model

### `Domain/Child.cs` (new)

```csharp
public class Child
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// Owner. Children hang off the HR record, not the login, because the
    /// entitlement they carry is an HR fact and EmployeeProfile is where leave
    /// entitlement already lives.
    public string EmployeeProfileId { get; set; } = string.Empty;
    public EmployeeProfile? EmployeeProfile { get; set; }

    /// First name is enough to disambiguate one employee's children in a picker.
    public string Name { get; set; } = string.Empty;

    /// Date only, like User.DateOfBirth. Age is NEVER stored — see design note.
    public DateOnly DateOfBirth { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AnnualLeave> LeaveRequests { get; set; } = new List<AnnualLeave>();
}
```

Persistence configuration:

- `Name`: required, max length 100.
- Index on `EmployeeProfileId`.
- `EmployeeProfile` to `Children`: cascade delete (a deleted profile's children go with it).
- `AnnualLeave.Child` to `Child.LeaveRequests`: **`DeleteBehavior.Restrict`**.

**Deleting a child is refused while any leave row references it.** Otherwise removing a
child erases the ledger proving 5 weeks were taken. This matches how a `ProjectType`
that projects still carry cannot be deleted. A child who has aged out is **kept**, not
deleted — their history still matters, and they simply read as ineligible.

### `EmployeeProfile` — `bool? HasChildren`

Requirement 1, as a genuine tri-state:

- `null` — the employee has never answered.
- `false` — the employee declared they have no children.
- `true` — the employee declared they have children.

Deriving this from `Children.Any()` was rejected: it cannot distinguish "hasn't told us"
from "has none", which is exactly the distinction the request form needs in order to
write a useful message ("Add your children in Edit profile" vs "No eligible children").

**Invariant, enforced in the handler:** `HasChildren` cannot be saved as `false` while
the profile has children, and adding a child sets it to `true`. The flag and the list
can therefore never disagree — the failure mode CLAUDE.md documents repeatedly for
duplicated leave figures.

### `AnnualLeave` — `string? ChildId`

Nullable, with a `Child?` navigation. Nullable for exactly one reason: pre-existing
paternity rows have no child and must stay approved and visible. Required by validator
and handler whenever the request's leave type has `PerChildEntitlement` set.

### `LeaveType` — four new columns

Placed beside `DefaultAllowance` and `MaxCarryoverDays`, the columns that already
describe a budget:

```csharp
/// Turns the per-child entitlement engine on for this type. When false the three
/// numbers below are ignored and the type behaves exactly as it does today.
public bool PerChildEntitlement { get; set; }

/// Lifetime entitlement per eligible child, in weeks. 18 for paternity leave.
public int PerChildTotalWeeks { get; set; }

/// Cap per leave year per eligible child, in weeks. 5 for paternity leave.
public int PerChildWeeksPerYear { get; set; }

/// The child's age at which the entitlement ends. 15 for paternity leave.
public int ChildEligibleUntilAge { get; set; }
```

**A 0 here means 0, not "unchecked".** This is the deliberate opposite of
`EmployeeProfile.AnnualLeaveEntitlement`, where `CheckSufficientBalanceAsync` returns
early on `<= 0` and a stored 0 disables the check entirely. A `PerChildTotalWeeks` of 0
refuses every request instead of waving them all through. The validator still refuses 0
on a type with the toggle on, but the safe direction is the default.

Validation on `UpsertLeaveTypeRequest` (only when `PerChildEntitlement` is true):

- `PerChildTotalWeeks`: 1 to 260.
- `PerChildWeeksPerYear`: 1 to 52, and not greater than `PerChildTotalWeeks`.
- `ChildEligibleUntilAge`: 1 to 30.
- `AffectsBalance` must be false — a per-child type has its own ledger and must not also
  be deducted from the pooled annual-leave balance.

## The arithmetic — `Domain/Services/PerChildLeaveCalculationService.cs`

Pure, like `LeaveCalculationService`: no `DbContext`, no clock, no I/O. All calendar
work — business-day counting, holiday exclusion, leave-year windows, boundary
straddling — **reuses `LeaveCalculationService`**. There is no second calendar.

```csharp
public static class PerChildLeaveCalculationService
{
    /// The one place weeks become days. A "week" of per-child leave is a working
    /// week, so weekends and public holidays inside a request do not consume it.
    public const int BusinessDaysPerWeek = 5;

    public static int WeeksToBusinessDays(int weeks);      // 18 -> 90, 5 -> 25
    public static decimal BusinessDaysToWeeks(int days);   // 6 -> 1.2m, for display

    /// Whole years completed on onDate. Birthday-correct: a 29 Feb child has their
    /// birthday on 1 Mar in a common year, so they are not briefly younger.
    public static int AgeOn(DateOnly dateOfBirth, DateOnly onDate);

    /// The last date a request for this child may END on.
    /// dateOfBirth.AddYears(maxAge).AddDays(-1).
    public static DateOnly LastEligibleDate(DateOnly dateOfBirth, int maxAge);

    public static bool IsEligibleOn(DateOnly dateOfBirth, DateOnly onDate, int maxAge)
        => AgeOn(dateOfBirth, onDate) < maxAge;

    /// Floored at zero, mirroring LeaveCalculationService.CalculateRemainingBalance.
    public static int RemainingDays(int totalDays, int usedDays);
}
```

`AgeOn` is defined as `years = onDate.Year - dateOfBirth.Year`, decremented by one when
`onDate` falls before that year's birthday, where a 29 Feb birthday in a common year is
treated as 1 Mar.

## Enforcement — `Application/AnnualLeaves/Commands/PerChildLeaveBalanceCalculator.cs`

A sibling of `AnnualLeaveBalanceCalculator`, same contract: an internal static class
returning a human-readable error string, or `null` when the request is allowed. It
**never throws** for a business rule. Handlers map the string straight to
`Result<T>.Failure`.

```csharp
public static Task<string?> CheckPerChildEntitlementAsync(
    AppDbContext context,
    AnnualLeave annualLeave,
    EmployeeProfile employeeProfile,
    string? excludeLeaveId,
    CancellationToken cancellationToken);
```

Checks, in order, stopping at the first failure:

1. **Not a per-child type** — return `null`. The type's `PerChildEntitlement` is false;
   nothing to enforce.
2. **No child named** — `"Select the child this <type name> is for."`
3. **Child not found, or belongs to another employee** —
   `"The selected child is not on your profile."` This is what closes the hole in an
   admin's on-behalf request, where `ChildId` arrives from the client.
4. **Child ineligible on the request's end date** —
   `"<Name> turns <age> on <dd MMM yyyy> — <type name> for this child must end on or
   before <dd MMM yyyy>."` Derived from `LastEligibleDate`. This single check covers
   both a request that starts after the birthday and one that straddles it.
5. **Lifetime cap.** Business days already approved for this child (excluding
   `excludeLeaveId`, holiday-aware) plus the requested business days must not exceed
   `WeeksToBusinessDays(PerChildTotalWeeks)`. On failure:
   `"<Name> has <n> day(s) (<w> week(s)) of <type name> remaining in total. This
   request is <r> day(s)."`
6. **Annual cap.** For each leave year the request touches
   (`LeaveCalculationService.GetCoveredLeaveYears`), days already approved for this
   child *within that leave year* plus the request's days *within that leave year* must
   not exceed `WeeksToBusinessDays(PerChildWeeksPerYear)`. On failure:
   `"<Name> has <n> day(s) (<w> week(s)) of <type name> left for the leave year
   <dd MMM yyyy> to <dd MMM yyyy>. This request uses <r> day(s) in that year."`

Checking each covered leave year separately is what stops a request from smuggling 10
weeks through a December-January split.

**Only `Approved` rows count as used.** `Pending`, `Rejected` and `Cancelled` do not —
the same rule `GetApprovedDaysForLeaveYearAsync` already applies to the annual pool.
Rows with a `null` `ChildId` are excluded by construction, since usage is queried by
`ChildId`.

A known and accepted consequence, identical to how the annual pool already behaves:
several *pending* requests can each pass the creation check independently, because
nothing pending is counted. The caps are enforced again on the transition into
`Approved`, so the second approval is refused rather than the second request. Reserving
entitlement at request time would be a different feature — pending leave would have to
expire or be released — and is not in scope.

### Shared DB helper extraction

`AnnualLeaveBalanceCalculator`'s private `GetHolidaySetAsync` and
`GetLeaveYearStartMonthAsync` move into a new internal
`Application/AnnualLeaves/Commands/LeaveYearQueries.cs` that both calculators call.
Copying the holiday query would be a real bug source the first time the country-code
handling changes. `AnnualLeaveBalanceCalculator`'s public behaviour is unchanged.

### Wiring

| Handler | When the check runs |
|---|---|
| `CreateAnnualLeave` | **Always** for a per-child type — before the leave is added, whether or not the type requires approval. |
| `EditAnnualLeave` | On save, with `excludeLeaveId` set to the row being edited. |
| `UpdateLeaveStatus` | On the transition into `Approved`, alongside the existing balance check. |

The deliberate difference from the annual-leave pool: that check runs at creation only
when the type auto-approves. For a per-child type it runs at creation **even when
approval is required**, so the employee learns immediately rather than waiting days for
a manager to hit an error the manager cannot fix.

`CreateAnnualLeave` and `EditAnnualLeave` must also set `annualLeave.ChildId` from the
request, and clear it when the selected type is not a per-child type — so switching a
request from Paternity to Annual Leave does not leave a stale child attached.

## API

New `Application/Children/` slice and a thin `API/Controllers/ChildrenController.cs`
dispatching to MediatR and calling `HandleResult<T>()`.

```
GET    /api/children                  my children; ?employeeId= for Admin / in-scope Manager
POST   /api/children                  { name, dateOfBirth }
PUT    /api/children/{id}             { name, dateOfBirth }
DELETE /api/children/{id}             refused while leave references it
GET    /api/children/entitlements     the ledger; ?employeeId= as above
```

`HasChildren` is **not** a new endpoint. It rides on the existing
`PUT /api/account/profile` request, which is what the Edit profile dialog already saves.

### `ChildLeaveEntitlementDto`

One row per child, answering requirements 3 to 8 in a single call:

```csharp
public class ChildLeaveEntitlementDto
{
    public string ChildId { get; set; }
    public string Name { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public int AgeYears { get; set; }              // computed
    public bool IsEligible { get; set; }           // computed
    public DateOnly LastEligibleDate { get; set; } // day before the 15th birthday

    public int TotalDays { get; set; }             // 90
    public decimal TotalWeeks { get; set; }        // 18
    public int UsedDays { get; set; }              // approved, all time, this child
    public int RemainingDays { get; set; }         // floored at 0

    public int ThisYearCapDays { get; set; }       // 25
    public int ThisYearUsedDays { get; set; }
    public int ThisYearRemainingDays { get; set; }
    public DateTime LeaveYearStart { get; set; }   // the window the figures describe
    public DateTime LeaveYearEnd { get; set; }
}
```

The response wraps these with the employee's totals over **eligible** children only —
`totalRemainingDays`, `thisYearCapDays` — which is requirement 10 for free: the totals
are a projection, so they change the moment a child ages out, with no recalculation
step and regardless of what that child had used.

An ineligible child is **returned, not hidden**, with `isEligible = false`, so the UI
can explain why rather than silently omitting them.

### Authorization

No new policy names. Role policies in the `AnnualLeaveRead` / `AnnualLeaveCreate` style,
plus an in-handler ownership check:

- **Self** — always read and write their own children.
- **Admin** — read and write for any employee via `?employeeId=`.
- **Manager** — **read-only**, and only within their existing department scope, resolved
  with the same `ManagerAccessScopeResolver` the leave handlers use. A manager approving
  a request has to be able to see the ledger it is measured against.

Any other combination is refused. `ICurrentUserAccessor` supplies the caller.

## Frontend

### Edit profile dialog — `Sidebar.tsx`

Widened `maxWidth="xs"` to `"sm"`, with a Children section below Date of birth:

- An "I have children" checkbox bound to `hasChildren`, saved with the profile.
- One compact row per child: name, DOB, computed age, and a chip reading `Eligible` or
  `Aged out`, with edit and remove actions.
- An "Add child" row (name + date).

**Child rows commit immediately** on add / edit / remove via their own mutations plus
query invalidation, while the checkbox saves with the profile's Save button. The
alternative — batching children into the profile Save — means building a
diff-and-sync command for very little gain. This mixed model is a deliberate choice and
should be visible in the UI: rows show their own pending state.

Unchecking "I have children" while children exist is refused client-side with a message
pointing at removing them first, matching the server invariant.

### `AnnualLeaveForm.tsx` — the child picker

When the selected leave type has `perChildEntitlement`, a **required** Child select
appears. Each option quotes that child's own numbers; ineligible children are shown
**disabled with the reason**, not hidden:

```
Child *  [ Andreas - 65 of 90 days left, 25 left this year   v ]
           Maria   - 90 of 90 days left, 25 left this year
           Petros  - turned 15 on 20 Jan 2025 (not eligible)

         This request: 6 business days (1.2 weeks). Andreas: 59 days left
```

The live figure under the picker recomputes from the chosen dates. Submission is blocked
with a message that points at the right fix:

- `hasChildren` unset or no children — "Add your children in Edit profile to request
  paternity leave."
- Children exist but none eligible — "No eligible children — paternity leave ends when
  a child turns 15."

The zod schema in `lib/validation/leave.ts` gains a conditional `childId` requirement.
The client's checks are an affordance only; the server is authoritative.

For an admin's on-behalf request, the picker loads the *selected employee's* children,
so it must react to the employee dropdown.

### Other client surfaces

- **`ApplyLeavePage.tsx`** — the Paternity card quotes "18 weeks per child, max 5
  weeks/year" instead of a flat day allowance.
- **`MyLeavePage.tsx`** — a per-child entitlement card: one row per child with used and
  remaining, plus this year's figures.
- **`LeaveTypesPanel.tsx`** — the `PerChildEntitlement` toggle plus the three numbers,
  revealed only when the toggle is on, following the dependent-toggle affordance from
  the recent Leave Settings rework.
- **`lib/leave-allowance.ts`** — learns the per-child unit, so no surface falls back to
  quoting `defaultAllowance` for a per-child type.
- **`lib/types/child.ts`**, **`lib/api/children.ts`** — new; `LeaveType` and
  `AnnualLeave` types gain the new fields.

## Migrations

1. **`AddChildLeaveEntitlement`** — schema only:
   - `Children` table (`Id`, `EmployeeProfileId`, `Name`, `DateOfBirth`, `CreatedAt`),
     index on `EmployeeProfileId`, cascade from `EmployeeProfiles`.
   - `EmployeeProfiles.HasChildren` — `bit NULL`.
   - `AnnualLeaves.ChildId` — `nvarchar(450) NULL`, FK to `Children` with
     `ON DELETE NO ACTION`.
   - `LeaveTypes`: `PerChildEntitlement bit NOT NULL DEFAULT 0`,
     `PerChildTotalWeeks int NOT NULL DEFAULT 0`,
     `PerChildWeeksPerYear int NOT NULL DEFAULT 0`,
     `ChildEligibleUntilAge int NOT NULL DEFAULT 0`.

2. **`ConfigurePaternityPerChildEntitlement`** — data, updating the existing
   `Paternity Leave` row by name:
   - `PerChildEntitlement = 1`, `PerChildTotalWeeks = 18`,
     `PerChildWeeksPerYear = 5`, `ChildEligibleUntilAge = 15`.
   - `AllowanceUnit = 'weeks/child'`.
   - `DefaultAllowance = 0` — so nothing quotes the dead 14.
   - `MaxConsecutiveDays = 25` — the display-only cap now agrees with the annual limit
     instead of contradicting it.
   - `Down` restores the previous values (14, `days/event`, 14, toggle off).

**This must be a migration, not a `DbInitializer` change.** `Seed:Enabled` is `false` in
Production, so `DbInitializer` never runs on the deployed host and a seeder-only change
would silently skip it.

`DbInitializer`'s leave-type spec map is updated to the same values in the same change,
so a fresh clone and the deployed database agree.

## Testing

Handler tests run against a real EF provider per CLAUDE.md: `TransactionalTestDb`
(SQLite in-memory) wherever a constraint or transaction is the subject, `TestDb` (EF
in-memory) otherwise.

### Domain — `PerChildLeaveCalculationService`

- `WeeksToBusinessDays`: 18 to 90, 5 to 25, 0 to 0.
- `AgeOn`: day before, on, and after a birthday; a 29 Feb child on 28 Feb, 1 Mar, and
  29 Feb of a leap year.
- `LastEligibleDate`: the day before the 15th birthday, including a 29 Feb child.
- `RemainingDays` floors at zero.

### The two worked examples from the requirements

- **Example 1** — one eligible child: 5 weeks in leave year 1, 2 in year 2, 3 in year 3,
  leaving 8 weeks (40 days); a 4th-year request for 9 weeks is refused, one for
  8 weeks is accepted.
- **Example 2** — three eligible children: total 54 weeks; 5 weeks per child in one
  leave year (15 weeks) all accepted; a 6th week for any one child refused. Then one
  child turns 15: totals fall to 36 weeks and 10 weeks/year **even though that child
  had used leave** — the aged-out child's usage stays in history and their remaining
  entitlement is gone.

### Enforcement edge cases

- Request straddling the 15th birthday — refused, message names the last bookable date.
- Request starting after the 15th birthday — refused as ineligible.
- Request straddling the leave-year boundary — charged to both years, and refused when
  either year's cap would break.
- Public holidays and weekends inside the range do not consume entitlement.
- A `ChildId` belonging to another employee — refused.
- Missing `ChildId` on a per-child type — refused.
- Legacy rows with `null` `ChildId` — excluded from every ledger figure.
- `Pending`, `Rejected`, `Cancelled` rows — excluded from used days.
- Editing a request excludes its own row from the used total.
- Switching a request's type away from a per-child type clears `ChildId`.
- Deleting a child referenced by leave — refused (`TransactionalTestDb`).
- Saving `HasChildren = false` while children exist — refused.
- A per-child type may not also set `AffectsBalance`.

### Frontend

Vitest (via `client/node_modules/.bin/vitest`) over the child picker's eligible and
ineligible rendering and its blocked-submit messages, and the Edit profile dialog's
children section.

## Risks

- **The dialog is getting crowded.** Children in the Edit profile dialog is the chosen
  surface, but the per-child *entitlement* table does not fit there — it lives on the
  request form's picker and the My Leave card instead. If the dialog proves too tight in
  use, the follow-up is a dedicated page, not a smaller table.
- **`AnnualLeaveForm.tsx` and `ApplyLeavePage.tsx` are already large** (502 and 1347
  lines). The child picker goes in as its own component rather than inline, to avoid
  making that worse.
- **Two ledgers now exist** — the pooled `LeaveBalance` and the per-child projection.
  They must never both apply to one leave type; the `AffectsBalance` validation rule
  above is what keeps them disjoint.
