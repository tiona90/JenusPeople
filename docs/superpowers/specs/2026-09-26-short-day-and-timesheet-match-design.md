# Short days and timesheet ↔ attendance match — design

Date: 2026-09-26 · Status: approved in chat, awaiting spec review

## Goal

The Working Week settings already define a net working day: working hours minus the
configured break (`WorkingDaySchedule.ScheduledMinutes` — 08:00–17:00 with a 1h
flexible break is 480 minutes, 8h). Nothing uses that figure to say that somebody
worked *less* than it, and nothing checks a timesheet against the attendance it
claims to describe.

1. **Short day** — on a working day, anybody who worked under the net target and was
   not on approved leave is flagged: to themselves (My Attendance), to their manager
   (Team Attendance, manager dashboard), and to HR within their departments (Team
   Attendance, HR dashboard, daily attendance report).
2. **Timesheet mismatch** — for each day of a timesheet, if the hours logged differ
   from the hours attendance recorded, the Manager and HR reviewing it see the
   difference, and the employee sees it before submitting. Flag only; nothing is
   blocked.
3. **Leave is ignored** — a day on approved leave is never short and never a mismatch.

## Decisions (from the conversation)

| Question | Answer |
|---|---|
| Tolerance | 15 minutes grace for both: short means *more than* 15 min under target; mismatch means timesheet and attendance differ by *more than* 15 min on a day. One constant, `DailyHoursRule.GraceMinutes = 15`, not a setting. |
| Mismatch consequence | Flag only. Submit and Approve are unchanged. |
| Short-day surfaces | All four: Team Attendance week grid (Manager + HR in scope), dashboard issue, daily report email section, employee history grade. |
| Approach | Computed on every read by one server rule; nothing stored. |

## Assumptions (stated, not asked)

- **Non-working days are skipped** — the configured working-days preset plus public
  holidays for the holiday country, as `WorkingWeek` reads them for reminders.
- **An open day is not short yet.** A day is judged once it is checked out of, or once
  it is a past date. Today, still checked in, says nothing — the same reading as
  `BreakVariance`, where a shortfall is only news once the day is done.
- **No attendance on a past working day, no leave** → short by the whole target
  ("No attendance"). Today with no check-in is left to the existing "not checked in"
  issue, not called short.
- **Approved leave only.** Pending / AwaitingHrApproval do not excuse a short day —
  the leave may be refused.
- **Half-day leave halves the target** (`ScheduledMinutes / 2`, rounded down) rather
  than skipping the day. Full-day leave removes the target.
- **"Day" is the UTC calendar day** of attendance events, which is how attendance is
  recorded and grouped everywhere today (`AttendanceDay`, `AttendanceHoursCalculator`).
  Working-day / holiday is asked of that same date.
- **Worked minutes** are `AttendanceDayState.WorkedMinutes` (elapsed minus breaks),
  the figure every attendance screen already shows.
- A `ScheduledMinutes` of 0 or less (misconfigured hours) means no target — nothing is
  ever short.

## Architecture

### 1. The rule — `Application/Attendance/Support/DailyHoursRule.cs` (new, pure)

```csharp
public enum DayKind { Working, NonWorking, Leave }

public sealed record DailyHoursVerdict(
    DayKind Kind,
    int? TargetMinutes,     // null unless Working (halved for a half-day leave)
    int? ShortByMinutes);   // null when nothing to say; > GraceMinutes otherwise

public static class DailyHoursRule
{
    public const int GraceMinutes = 15;

    // Pure: every input is passed in.
    public static DailyHoursVerdict Judge(
        DateOnly day,
        bool isWorkingDay,          // WorkingWeek answer for the date
        LeaveOnDay leave,           // None | Full | HalfDay
        int scheduledMinutes,       // WorkingDaySchedule.ScheduledMinutes
        int workedMinutes,          // AttendanceDayState.WorkedMinutes (0 when none)
        bool dayClosed);            // checked out, or day < today

    public static int? MismatchMinutes(          // logged − attended, null within grace
        DayKind kind, decimal loggedHours, int attendedMinutes);
}
```

- `Kind` is `NonWorking` when `!isWorkingDay`, `Leave` when `leave == Full`, else
  `Working`.
- `ShortByMinutes = target − worked` when `Working`, `dayClosed`, and
  `target − worked > GraceMinutes`; otherwise null.
- `MismatchMinutes` is null for a `Leave` day; for any other day, `round(logged×60) −
  attended` when its absolute value exceeds `GraceMinutes`. A non-working day with
  hours on either side is still compared (weekend work logged but not attended is a
  mismatch).

### 2. Loading the inputs for a range — `DailyHoursContext` (new, same folder)

One loader so the queries do not each re-invent the lookups:

```csharp
public sealed class DailyHoursContext
{
    public static Task<DailyHoursContext> LoadAsync(
        AppDbContext context, IReadOnlyCollection<string> employeeProfileIds,
        DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken ct);

    public int ScheduledMinutes { get; }
    public bool IsWorkingDay(DateOnly day);
    public LeaveOnDay LeaveOn(string employeeProfileId, DateOnly day);
    public bool IsClosed(DateOnly day, AttendanceDayState? state);
}
```

- Reads `AppSettings` once, the public holidays for the range in one query, and the
  **Approved** `AnnualLeave` rows overlapping the range for those profiles in one query.
- Adds `WorkingWeek.IsWorkingDay(settings, day, IReadOnlySet<DateOnly> holidays)` — a
  pure overload beside `IsWorkingDayAsync`, which is re-expressed through it, so there
  stays one reading of "working day".
- `LeaveOn` returns `HalfDay` when the covering leave's `Duration` is a half day,
  `Full` otherwise.

### 3. Attendance surfaces

| Surface | Server change | Client change |
|---|---|---|
| **My Attendance** (`GetMyAttendanceHistory`) | `DayHistoryDto` gains `ShortByMinutes?`, `OnLeave`, `TargetMinutes?`. `HistoryStatus` gains `short`, `leave` and `off`. Precedence: `leave` → `off` (non-working day, no events) → `absent` (no events; `ShortByMinutes` = target) → `in-progress` → `short` → `late` → `complete`. A day both late and short reads `short`; the late check-in time is still shown in its column. | `AttendancePage` history table renders the `short` grade ("1h 30m short") and `Leave`; the strip's bar scales to `targetMinutes` instead of the hard-coded 8h. |
| **Team Attendance week grid** (`GetTeamAttendance.BuildWeekAsync`) — Manager and HR in scope | `WeekDayHoursDto` gains `ShortByMinutes?`, `OnLeave`. | `TeamAttendancePage` grid cell: amber "6h 30m · 1h 30m short"; `Leave` instead of "—". |
| **HR dashboard issue** (`GetCompanyAttendance.BuildIssues`) | New issue "N short days yesterday" (detail lists names) judged for the previous working day in scope. Also: the overtime issue's hard-coded `OvertimeMinutes = 600` becomes `ScheduledMinutes`, and people on leave are excluded from it. | `TodaysIssuesCard` renders it like the existing issues — no change beyond the data. |
| **Manager dashboard issue** | `GetTeamAttendance` gains `ShortDaysYesterday` (list of names + minutes). | Manager section of `DashboardHome` shows a line under the team card when non-empty. |
| **Daily attendance report** (`ReminderDispatcher.BuildDailyAttendanceReportAsync`) | New `ShortDay` section: checked-out people under target by more than the grace, leave excluded, beside `Overtime`. Omitted when `ScheduledMinutes <= 0`. | — |

The "yesterday" issue uses the most recent **working** day before today (a Monday
dashboard reports Friday).

### 4. Timesheet ↔ attendance

- `GetTimesheetList` loads the attendance events for each listed timesheet's
  employee and week (one query over all employees × the union of weeks), computes
  `WorkedMinutes` per UTC day, and a `DailyHoursContext` for leave.
- `TimesheetDto` gains:
  - `AttendanceMinutes: List<int>` — Mon–Fri, parallel to `DailyHours`.
  - `DayMismatchMinutes: List<int?>` — Mon–Fri, `DailyHoursRule.MismatchMinutes`.
  - `OnLeaveDays: List<bool>` — Mon–Fri.
  - `MismatchDayCount: int`.
- Client:
  - `TimesheetDailyBreakdown` (shared by All Timesheets' expanded row and Team
    Timesheets' View dialog) marks a mismatched day card: "Logged 8h · attended
    6h 30m", and a leave day "On leave".
  - Team Timesheets and All Timesheets rows get a chip "2 days don't match attendance"
    when `mismatchDayCount > 0`.
  - `MyTimesheetPage` / `NewTimesheetPage` shows the same per-day note to the employee
    before submitting (read from the DTO of the timesheet being edited).
- No status, submit or approve behaviour changes.

### 5. Client mirror — `client/src/lib/daily-hours.ts` (new)

Wording only: `describeShortDay(minutes)`, `describeMismatch(loggedHours,
attendedMinutes)`. The client never re-derives a verdict from raw figures; a missing
field (older API) reads as "nothing to say", so no false flags appear.

## Error handling / edges

- Settings row missing → `WorkingDaySchedule.From(null)` defaults (09:00–18:00, no
  break) apply, as today.
- A leave spanning the range edge is caught by the overlap query
  (`StartDate <= to && EndDate >= from`).
- A soft-deleted profile is already filtered out of every query's population.
- Timesheet with no attendance at all for the week → every logged day beyond the grace
  is a mismatch; that is the intended signal.

## Testing

Server (xUnit, `Tests/WorkTrack.Tests/`):
- `DailyHoursRuleTests` — non-working day, full leave, half-day leave (target 240),
  open day says nothing, grace boundary (15 → null, 16 → 16), no attendance on a past
  day (short by target), `ScheduledMinutes <= 0`; `MismatchMinutes` on leave, within
  grace, both signs, weekend.
- `DailyHoursContextTests` (EF in-memory) — holidays in range, approved vs pending
  leave, half-day duration.
- Query tests: `GetMyAttendanceHistory` short/leave/off grades; `GetTeamAttendance`
  week grid and `ShortDaysYesterday`; `GetCompanyAttendance` short-days issue and the
  overtime-on-schedule fix; `DailyAttendanceReportTests` short-day section;
  `GetTimesheetList` mismatch fields.

Client (vitest, per-directory batches):
- `AttendancePage.test.tsx`, `TeamAttendancePage.test.tsx`,
  `TimesheetDailyBreakdown` (via `AllTimesheetsPage.test.tsx` /
  `TeamTimesheetPage.test.tsx`), `daily-hours.test.ts`.

## Out of scope

- Storing short days or mismatches, notifications/bell entries for them.
- A configurable grace setting.
- Blocking submit/approve on a mismatch.
- Changing how attendance days are bucketed (UTC day stays).

## Docs

CLAUDE.md gains a short "Short days and timesheet match" paragraph under Key
Configuration beside the attendance-lateness one, naming `DailyHoursRule` as the one
place the rule lives.
