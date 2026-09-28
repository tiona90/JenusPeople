# Short Days and Timesheet ↔ Attendance Match — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flag working days where someone worked under the net scheduled hours (working hours minus break) and timesheet days whose logged hours differ from attended hours, ignoring approved leave, for employees, Managers and HR.

**Architecture:** One pure rule (`DailyHoursRule`) plus one range loader (`DailyHoursContext`) in `Application/Attendance/Support/`, called on every read by the existing attendance queries, the daily report and the timesheet list. Nothing is stored. The client only words the server's figures (`client/src/lib/daily-hours.ts`).

**Tech Stack:** ASP.NET Core 10, EF Core, MediatR, xUnit (EF in-memory `TestDb`); React 19 + TypeScript, MUI 7, React Query, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-26-short-day-and-timesheet-match-design.md`

## Global Constraints

- Grace: `DailyHoursRule.GraceMinutes = 15`. Short means `target − worked > 15`; mismatch means `|logged − attended| > 15`. Exactly 15 is not flagged.
- Target: `WorkingDaySchedule.ScheduledMinutes` (working hours minus break). Halved (integer division) for a half-day leave. `ScheduledMinutes <= 0` means no target.
- Only `AnnualLeaveStatus.Approved` leave excuses a day. Leave rows are matched by `AnnualLeave.EmployeeProfileId` (not `EmployeeId`).
- Non-working day = configured working-days preset or a public holiday for `AppSettings.HolidayCountryCode`.
- An open day (today, not checked out) is never short and never a mismatch.
- "Day" is the UTC calendar day (`AttendanceDay.UtcDayStart`).
- Flag only: no change to submit, approve or any status transition.
- A missing field from an older API reads as "nothing to say" on the client.
- Never use `string[].Contains` inside an EF lambda (C# 14 binds it to a span overload and throws). Use `List<string>`.
- Run server tests with: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~<Name>"`.
- Run client tests with: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client <paths>` (never `npx vitest`). Run them in per-directory batches, never the whole suite at once.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A past day with a forgotten check-out** reads as a long day, not a short one. The calculator runs an open day up to now. This matches every existing screen and is left alone; the rule must not crash or flag it (pinned in Task 4).
2. **The timesheet comparison window.** Only sheets whose week starts within the last `TimesheetAttendanceComparison.WindowWeeks = 12` weeks are compared, so the list query doesn't load all attendance history. Older sheets send null fields and show nothing (pinned in Task 7).
3. **Two half-day leaves on one date** (AM + PM) count as a full day of leave, not a half (pinned in Task 2).
4. **A weekend with hours** logged but no attendance is a mismatch. A weekend with nothing on either side is not (pinned in Task 1).
5. **Existing tests that run on the real clock.** `AttendanceActionsTests.History_reports_absent_days_and_the_day_in_progress` depends on the weekday once weekends read `off`. Fix it in Task 3, and don't weaken any other test.

---

## File Structure

| File | Responsibility |
|---|---|
| `Application/Settings/Support/WorkingWeek.cs` (modify) | Add `LoadHolidaysAsync` and a pure `IsWorkingDay(settings, day, holidays)`. `IsWorkingDayAsync` is re-expressed through them. |
| `Application/Attendance/Support/DailyHoursRule.cs` (create) | Pure verdicts: `Judge`, `MismatchMinutes`, `CombineLeave`, `Describe`. |
| `Application/Attendance/Support/DailyHoursContext.cs` (create) | Loads settings, holidays and approved leave for a profile set × date range, and answers `IsWorkingDay`, `LeaveOn`, `IsClosed`, `Judge`. |
| `Application/Attendance/Support/ShortDayDigest.cs` (create) | "Who was short on the previous working day" for a set of profiles. Shared by the team and company queries. |
| `Application/Attendance/DTOs/AttendanceDtos.cs` (modify) | New fields on `DayHistoryDto`, `WeekDayHoursDto`, `TeamAttendanceDto`; new `ShortDayDto`. |
| `Application/Attendance/Queries/GetMyAttendanceHistory.cs` (modify) | `short` / `leave` / `off` grades and the target. |
| `Application/Attendance/Queries/GetTeamAttendance.cs` (modify) | Week grid short/leave fields and the previous-day digest. |
| `Application/Attendance/Queries/GetCompanyAttendance.cs` (modify) | Short-days issue; overtime judged on the schedule, leave excluded. |
| `Application/Reminders/ReminderDispatcher.cs` (modify) | "Short day" section of the daily report. |
| `Application/Timesheets/Support/TimesheetAttendanceComparison.cs` (create) | Per-timesheet Mon–Fri attended minutes, mismatches and leave flags. |
| `Application/Timesheets/Queries/GetTimesheetList.cs` (modify) | Fill the new DTO fields; `NowUtc` test seam. |
| `Application/Timesheets/DTOs/TimesheetDto.cs` (modify) | `AttendanceMinutes`, `DayMismatchMinutes`, `OnLeaveDays`, `MismatchDayCount`. |
| `client/src/lib/daily-hours.ts` (create) | `describeShortDay`, `describeMismatch`, `describeMismatchCount`. |
| `client/src/lib/types/attendance.ts`, `client/src/lib/types/timesheet.ts` (modify) | Mirror the DTO fields. |
| `client/src/components/attendance/ShortDaysNote.tsx` (create) | "Short days on Fri 25 Sep: …" line. Used by Team Attendance and the manager dashboard. |
| `client/src/components/attendance/AttendancePage.tsx` (modify) | History grades and the bar target. |
| `client/src/components/attendance/TeamAttendancePage.tsx` (modify) | Grid cells and the note. |
| `client/src/components/annual-leave/DashboardHome.tsx` (modify) | Note inside `TeamStatusNowCard`. |
| `client/src/components/timesheet/MismatchChip.tsx` (create) | "N days don't match attendance" chip. |
| `client/src/components/timesheet/TimesheetDailyBreakdown.tsx` (modify) | Per-day mismatch and leave line. |
| `client/src/components/timesheet/AllTimesheetsPage.tsx`, `TeamTimesheetPage.tsx`, `MyTimesheetPage.tsx` (modify) | Row chips. |
| `CLAUDE.md` (modify) | One paragraph under Key Configuration. |

---

### Task 1: The pure rule and the pure working-day reading

**Files:**
- Modify: `Application/Settings/Support/WorkingWeek.cs:59-78`
- Create: `Application/Attendance/Support/DailyHoursRule.cs`
- Test: `Tests/WorkTrack.Tests/DailyHoursRuleTests.cs`

**Interfaces:**
- Produces:
  - `enum DayKind { Working, NonWorking, Leave }`
  - `enum LeaveOnDay { None, HalfDay, Full }`
  - `record DailyHoursVerdict(DayKind Kind, int? TargetMinutes, int? ShortByMinutes)`
  - `DailyHoursRule.GraceMinutes` (const int 15)
  - `DailyHoursRule.Judge(bool isWorkingDay, LeaveOnDay leave, int scheduledMinutes, int workedMinutes, bool dayClosed) → DailyHoursVerdict`
  - `DailyHoursRule.MismatchMinutes(DayKind kind, decimal loggedHours, int attendedMinutes) → int?`
  - `DailyHoursRule.CombineLeave(IEnumerable<LeaveDuration> durations) → LeaveOnDay`
  - `DailyHoursRule.Describe(int minutes) → string`
  - `WorkingWeek.IsWorkingDay(AppSettings? settings, DateOnly day, IReadOnlySet<DateOnly> holidays) → bool`
  - `WorkingWeek.LoadHolidaysAsync(AppDbContext, AppSettings?, DateOnly from, DateOnly to, CancellationToken) → Task<HashSet<DateOnly>>`

- [ ] **Step 1: Write the failing test**

Create `Tests/WorkTrack.Tests/DailyHoursRuleTests.cs`:

```csharp
using Application.Attendance.Support;
using Application.Settings.Support;
using Domain;
using Domain.Services;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The one rule for "worked less than the day asks for" and "the timesheet says
/// something attendance does not". Pure: every input is passed in.
/// </summary>
public class DailyHoursRuleTests
{
    private const int Eight = 480;

    [Fact]
    public void Full_leave_has_no_target_and_is_never_short()
    {
        var v = DailyHoursRule.Judge(isWorkingDay: true, LeaveOnDay.Full, Eight, workedMinutes: 0, dayClosed: true);
        Assert.Equal(DayKind.Leave, v.Kind);
        Assert.Null(v.TargetMinutes);
        Assert.Null(v.ShortByMinutes);
    }

    [Fact]
    public void Leave_outranks_a_non_working_day()
    {
        var v = DailyHoursRule.Judge(isWorkingDay: false, LeaveOnDay.Full, Eight, 0, true);
        Assert.Equal(DayKind.Leave, v.Kind);
    }

    [Fact]
    public void A_non_working_day_has_no_target()
    {
        var v = DailyHoursRule.Judge(isWorkingDay: false, LeaveOnDay.None, Eight, 0, true);
        Assert.Equal(DayKind.NonWorking, v.Kind);
        Assert.Null(v.ShortByMinutes);
    }

    [Fact]
    public void A_half_day_leave_halves_the_target()
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.HalfDay, Eight, workedMinutes: 200, dayClosed: true);
        Assert.Equal(DayKind.Working, v.Kind);
        Assert.Equal(240, v.TargetMinutes);
        Assert.Equal(40, v.ShortByMinutes);
    }

    [Fact]
    public void An_open_day_says_nothing_yet()
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.None, Eight, workedMinutes: 60, dayClosed: false);
        Assert.Equal(Eight, v.TargetMinutes);
        Assert.Null(v.ShortByMinutes);
    }

    [Theory]
    [InlineData(465, null)]  // exactly 15 short: within the grace
    [InlineData(464, 16)]    // 16 short: flagged
    [InlineData(480, null)]
    [InlineData(600, null)]  // over is not short
    public void The_grace_is_fifteen_minutes(int worked, int? expected)
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.None, Eight, worked, dayClosed: true);
        Assert.Equal(expected, v.ShortByMinutes);
    }

    [Fact]
    public void No_attendance_on_a_closed_working_day_is_short_by_the_whole_target()
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.None, Eight, 0, true);
        Assert.Equal(Eight, v.ShortByMinutes);
    }

    [Fact]
    public void No_schedule_means_nothing_is_ever_short()
    {
        var v = DailyHoursRule.Judge(true, LeaveOnDay.None, scheduledMinutes: 0, 0, true);
        Assert.Null(v.TargetMinutes);
        Assert.Null(v.ShortByMinutes);
    }

    [Theory]
    [InlineData(8.0, 480, null)]
    [InlineData(8.0, 465, null)]    // 15 apart: within the grace
    [InlineData(8.0, 390, 90)]      // logged more than attended
    [InlineData(6.0, 480, -120)]    // attended more than logged
    [InlineData(8.0, 0, 480)]       // logged with no attendance at all
    public void Mismatch_is_logged_minus_attended_beyond_the_grace(double logged, int attended, int? expected)
    {
        Assert.Equal(expected, DailyHoursRule.MismatchMinutes(DayKind.Working, (decimal)logged, attended));
    }

    [Fact]
    public void A_leave_day_is_never_a_mismatch()
    {
        Assert.Null(DailyHoursRule.MismatchMinutes(DayKind.Leave, 8m, 0));
    }

    [Fact]
    public void A_weekend_is_compared_when_either_side_has_hours()
    {
        Assert.Equal(240, DailyHoursRule.MismatchMinutes(DayKind.NonWorking, 4m, 0));
        Assert.Null(DailyHoursRule.MismatchMinutes(DayKind.NonWorking, 0m, 0));
    }

    [Fact]
    public void Two_half_days_on_one_date_are_a_full_day()
    {
        Assert.Equal(LeaveOnDay.None, DailyHoursRule.CombineLeave([]));
        Assert.Equal(LeaveOnDay.HalfDay, DailyHoursRule.CombineLeave([LeaveDuration.HalfDayMorning]));
        Assert.Equal(LeaveOnDay.Full, DailyHoursRule.CombineLeave([LeaveDuration.HalfDayMorning, LeaveDuration.HalfDayAfternoon]));
        Assert.Equal(LeaveOnDay.Full, DailyHoursRule.CombineLeave([LeaveDuration.Full]));
    }

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    public void Describe_words_minutes(int minutes, string expected)
    {
        Assert.Equal(expected, DailyHoursRule.Describe(minutes));
    }

    [Fact]
    public void Working_week_reads_holidays_from_the_set()
    {
        var settings = new AppSettings { WorkingDays = "mon-fri" };
        var holiday = new DateOnly(2026, 9, 24); // Thursday
        var holidays = new HashSet<DateOnly> { holiday };

        Assert.False(WorkingWeek.IsWorkingDay(settings, holiday, holidays));
        Assert.True(WorkingWeek.IsWorkingDay(settings, new DateOnly(2026, 9, 23), holidays));
        Assert.False(WorkingWeek.IsWorkingDay(settings, new DateOnly(2026, 9, 26), holidays)); // Saturday
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~DailyHoursRuleTests"`
Expected: build FAIL — `DailyHoursRule`, `DayKind`, `LeaveOnDay` and `WorkingWeek.IsWorkingDay` do not exist.

- [ ] **Step 3: Add the pure working-day reading to `WorkingWeek`**

In `Application/Settings/Support/WorkingWeek.cs`, replace the whole `IsWorkingDayAsync` method (lines 59-78, with its doc comment) with:

```csharp
    /// <summary>
    /// Whether <paramref name="day"/> is a working day for the org: a configured
    /// weekday that is not in <paramref name="holidays"/>. The pure reading every
    /// range query uses, with the holidays loaded once by <see cref="LoadHolidaysAsync"/>.
    /// </summary>
    public static bool IsWorkingDay(AppSettings? settings, DateOnly day, IReadOnlySet<DateOnly> holidays) =>
        IsConfiguredWorkingDay(settings, day.DayOfWeek) && !holidays.Contains(day);

    /// <summary>
    /// Public holidays for the configured holiday country between
    /// <paramref name="from"/> and <paramref name="to"/>, inclusive. No holiday
    /// country means no holidays.
    /// </summary>
    public static async Task<HashSet<DateOnly>> LoadHolidaysAsync(
        AppDbContext context, AppSettings? settings, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var country = settings?.HolidayCountryCode;
        if (string.IsNullOrWhiteSpace(country)) return [];

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
        var dates = await context.PublicHolidays
            .Where(h => h.CountryCode == country && h.Date >= start && h.Date < end)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);

        return [.. dates.Select(DateOnly.FromDateTime)];
    }

    /// <summary>
    /// Whether <paramref name="day"/> is a working day for the org: a configured
    /// weekday that is not a public holiday for the configured holiday country.
    /// No holiday country means no holidays.
    /// </summary>
    public static async Task<bool> IsWorkingDayAsync(AppDbContext context, AppSettings? settings, DateOnly day, CancellationToken cancellationToken) =>
        IsConfiguredWorkingDay(settings, day.DayOfWeek)
        && IsWorkingDay(settings, day, await LoadHolidaysAsync(context, settings, day, day, cancellationToken));
```

- [ ] **Step 4: Write `DailyHoursRule`**

Create `Application/Attendance/Support/DailyHoursRule.cs`:

```csharp
using Domain.Services;

namespace Application.Attendance.Support;

/// <summary>What a calendar day is, for the purpose of asking how long it should have been.</summary>
public enum DayKind { Working, NonWorking, Leave }

/// <summary>How much of a day approved leave covers.</summary>
public enum LeaveOnDay { None, HalfDay, Full }

/// <param name="TargetMinutes">The minutes the day asks for; null unless <see cref="DayKind.Working"/> with a schedule.</param>
/// <param name="ShortByMinutes">Minutes under the target, beyond the grace; null when there is nothing to say.</param>
public sealed record DailyHoursVerdict(DayKind Kind, int? TargetMinutes, int? ShortByMinutes);

/// <summary>
/// The one rule for a short day and for a timesheet that disagrees with
/// attendance. The target is the working day net of the configured break
/// (<see cref="WorkingDaySchedule.ScheduledMinutes"/>); approved leave removes it,
/// or halves it for a half day; a non-working day has none. A day still open says
/// nothing, the same reading as <see cref="WorkingDaySchedule.BreakVariance"/>:
/// the afternoon may still come. Pure, so every caller loads its own inputs
/// (usually through <see cref="DailyHoursContext"/>) and they all agree.
/// </summary>
public static class DailyHoursRule
{
    /// <summary>
    /// Leaving two minutes early is not news. Shared by the short-day and the
    /// mismatch checks, so a day within the grace of its target is also within the
    /// grace of a timesheet that logs the full target.
    /// </summary>
    public const int GraceMinutes = 15;

    public static DailyHoursVerdict Judge(
        bool isWorkingDay, LeaveOnDay leave, int scheduledMinutes, int workedMinutes, bool dayClosed)
    {
        if (leave == LeaveOnDay.Full) return new(DayKind.Leave, null, null);
        if (!isWorkingDay) return new(DayKind.NonWorking, null, null);
        if (scheduledMinutes <= 0) return new(DayKind.Working, null, null);

        var target = leave == LeaveOnDay.HalfDay ? scheduledMinutes / 2 : scheduledMinutes;
        var shortBy = target - workedMinutes;
        return new(DayKind.Working, target, dayClosed && shortBy > GraceMinutes ? shortBy : null);
    }

    /// <summary>
    /// Logged minus attended, in minutes, when the two differ by more than the
    /// grace; null otherwise and always null on a leave day. A non-working day is
    /// still compared: weekend work logged but never clocked is exactly the kind
    /// of disagreement a reviewer wants to see.
    /// </summary>
    public static int? MismatchMinutes(DayKind kind, decimal loggedHours, int attendedMinutes)
    {
        if (kind == DayKind.Leave) return null;
        var logged = (int)Math.Round(loggedHours * 60m, MidpointRounding.AwayFromZero);
        var diff = logged - attendedMinutes;
        return Math.Abs(diff) > GraceMinutes ? diff : null;
    }

    /// <summary>The approved leave rows covering one date, folded into one reading. Two halves are a whole.</summary>
    public static LeaveOnDay CombineLeave(IEnumerable<LeaveDuration> durations)
    {
        var halves = 0;
        foreach (var duration in durations)
        {
            if (duration == LeaveDuration.Full) return LeaveOnDay.Full;
            halves++;
        }
        return halves switch { 0 => LeaveOnDay.None, 1 => LeaveOnDay.HalfDay, _ => LeaveOnDay.Full };
    }

    /// <summary>"45 min", "1h", "1h 30m" — the wording the client's formatBreakMinutes uses.</summary>
    public static string Describe(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0) return $"{rest} min";
        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass, including the reminder schedule**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~DailyHoursRuleTests|FullyQualifiedName~ReminderSchedule|FullyQualifiedName~DailyAttendanceReport"`
Expected: all PASS. `IsWorkingDayAsync` changed shape but not meaning.

- [ ] **Step 6: Commit**

```bash
git add Application/Settings/Support/WorkingWeek.cs Application/Attendance/Support/DailyHoursRule.cs Tests/WorkTrack.Tests/DailyHoursRuleTests.cs
git commit -m "Add the rule for a short day and a timesheet that disagrees with attendance

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The range loader — `DailyHoursContext`

**Files:**
- Create: `Application/Attendance/Support/DailyHoursContext.cs`
- Test: `Tests/WorkTrack.Tests/DailyHoursContextTests.cs`

**Interfaces:**
- Consumes: Task 1 (`DailyHoursRule`, `LeaveOnDay`, `WorkingWeek.IsWorkingDay`, `WorkingWeek.LoadHolidaysAsync`).
- Produces:
  - `DailyHoursContext.LoadAsync(AppDbContext context, IReadOnlyCollection<string> employeeProfileIds, DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken ct) → Task<DailyHoursContext>`
  - Properties: `WorkingDaySchedule Schedule`, `int ScheduledMinutes`, `DateOnly TodayUtc`
  - Methods: `bool IsWorkingDay(DateOnly)`, `LeaveOnDay LeaveOn(string employeeProfileId, DateOnly)`, `bool IsClosed(DateOnly, AttendanceDayState?)`, `DailyHoursVerdict Judge(string employeeProfileId, DateOnly, AttendanceDayState?)`

- [ ] **Step 1: Write the failing test**

Create `Tests/WorkTrack.Tests/DailyHoursContextTests.cs`:

```csharp
using Application.Attendance.Support;
using Domain;
using Domain.Services;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The loader every short-day and mismatch surface shares: settings, holidays and
/// approved leave for a set of people over a range, in three queries.
/// </summary>
public class DailyHoursContextTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc); // Thursday
    private static readonly DateOnly Mon = new(2026, 9, 21);

    private static AppDbContext Seed()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC", WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00",
            BreakMode = "flexible", BreakMinutes = 60, WorkingDays = "mon-fri", HolidayCountryCode = "CY",
        });
        db.PublicHolidays.Add(new PublicHoliday { CountryCode = "CY", Year = 2026, Date = new DateTime(2026, 9, 23), EnglishName = "Test Day" });
        db.PublicHolidays.Add(new PublicHoliday { CountryCode = "GR", Year = 2026, Date = new DateTime(2026, 9, 22), EnglishName = "Elsewhere" });
        Leave(db, "p-ann", new DateTime(2026, 9, 21), new DateTime(2026, 9, 21), AnnualLeaveStatus.Approved, LeaveDuration.Full);
        Leave(db, "p-ann", new DateTime(2026, 9, 22), new DateTime(2026, 9, 22), AnnualLeaveStatus.Pending, LeaveDuration.Full);
        Leave(db, "p-ann", new DateTime(2026, 9, 24), new DateTime(2026, 9, 24), AnnualLeaveStatus.Approved, LeaveDuration.HalfDayMorning);
        Leave(db, "p-bob", new DateTime(2026, 9, 24), new DateTime(2026, 9, 24), AnnualLeaveStatus.Approved, LeaveDuration.HalfDayMorning);
        Leave(db, "p-bob", new DateTime(2026, 9, 24), new DateTime(2026, 9, 24), AnnualLeaveStatus.Approved, LeaveDuration.HalfDayAfternoon);
        db.SaveChanges();
        return db;
    }

    private static void Leave(AppDbContext db, string profileId, DateTime start, DateTime end, AnnualLeaveStatus status, LeaveDuration duration) =>
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = "u-" + profileId,
            EmployeeProfileId = profileId,
            StartDate = start,
            EndDate = end,
            Status = status,
            Duration = duration,
        });

    private static Task<DailyHoursContext> Load(AppDbContext db) =>
        DailyHoursContext.LoadAsync(db, ["p-ann", "p-bob"], Mon, Mon.AddDays(4), Now, CancellationToken.None);

    [Fact]
    public async Task Reads_the_schedule_net_of_the_break()
    {
        using var db = Seed();
        var ctx = await Load(db);
        Assert.Equal(480, ctx.ScheduledMinutes);
        Assert.Equal(new DateOnly(2026, 9, 24), ctx.TodayUtc);
    }

    [Fact]
    public async Task Only_the_configured_countrys_holidays_count()
    {
        using var db = Seed();
        var ctx = await Load(db);
        Assert.True(ctx.IsWorkingDay(new DateOnly(2026, 9, 22)));   // GR holiday: not ours
        Assert.False(ctx.IsWorkingDay(new DateOnly(2026, 9, 23)));  // CY holiday
        Assert.False(ctx.IsWorkingDay(new DateOnly(2026, 9, 26)));  // Saturday
    }

    [Fact]
    public async Task Only_approved_leave_counts_and_halves_are_told_apart()
    {
        using var db = Seed();
        var ctx = await Load(db);
        Assert.Equal(LeaveOnDay.Full, ctx.LeaveOn("p-ann", Mon));
        Assert.Equal(LeaveOnDay.None, ctx.LeaveOn("p-ann", Mon.AddDays(1)));        // pending
        Assert.Equal(LeaveOnDay.HalfDay, ctx.LeaveOn("p-ann", Mon.AddDays(3)));
        Assert.Equal(LeaveOnDay.Full, ctx.LeaveOn("p-bob", Mon.AddDays(3)));        // AM + PM
        Assert.Equal(LeaveOnDay.None, ctx.LeaveOn("p-nobody", Mon));
    }

    [Fact]
    public async Task A_past_day_is_closed_and_today_is_closed_only_once_checked_out()
    {
        using var db = Seed();
        var ctx = await Load(db);
        var open = new AttendanceDayState(AttendanceDayStatus.In, Now.AddHours(-4), null, null, 0, 240, false);
        var done = open with { Status = AttendanceDayStatus.Done, CheckOutAt = Now };

        Assert.True(ctx.IsClosed(Mon, null));
        Assert.False(ctx.IsClosed(ctx.TodayUtc, open));
        Assert.True(ctx.IsClosed(ctx.TodayUtc, done));
        Assert.False(ctx.IsClosed(ctx.TodayUtc.AddDays(1), null));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~DailyHoursContextTests"`
Expected: build FAIL — `DailyHoursContext` does not exist.

- [ ] **Step 3: Write `DailyHoursContext`**

Create `Application/Attendance/Support/DailyHoursContext.cs`:

```csharp
using Application.Settings.Support;
using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Attendance.Support;

/// <summary>
/// Everything <see cref="DailyHoursRule"/> needs for a set of people over a date
/// range, loaded in three queries: the settings row, the public holidays in the
/// range, and the approved leave overlapping it. Leave is matched on
/// <see cref="AnnualLeave.EmployeeProfileId"/>, the key attendance is recorded
/// against (see <see cref="AttendanceDay.LoadOnLeaveProfileIdsAsync"/>).
/// </summary>
public sealed class DailyHoursContext
{
    private readonly AppSettings? _settings;
    private readonly IReadOnlySet<DateOnly> _holidays;
    private readonly Dictionary<string, List<(DateOnly Start, DateOnly End, LeaveDuration Duration)>> _leaves;

    private DailyHoursContext(
        AppSettings? settings,
        IReadOnlySet<DateOnly> holidays,
        Dictionary<string, List<(DateOnly, DateOnly, LeaveDuration)>> leaves,
        DateOnly todayUtc)
    {
        _settings = settings;
        _holidays = holidays;
        _leaves = leaves;
        Schedule = WorkingDaySchedule.From(settings);
        TodayUtc = todayUtc;
    }

    public WorkingDaySchedule Schedule { get; }
    public int ScheduledMinutes => Schedule.ScheduledMinutes;

    /// <summary>The UTC calendar date of the clock the context was loaded at.</summary>
    public DateOnly TodayUtc { get; }

    public static async Task<DailyHoursContext> LoadAsync(
        AppDbContext context,
        IReadOnlyCollection<string> employeeProfileIds,
        DateOnly from,
        DateOnly to,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var holidays = await WorkingWeek.LoadHolidaysAsync(context, settings, from, to, cancellationToken);

        var start = from.ToDateTime(TimeOnly.MinValue);
        var endExclusive = to.ToDateTime(TimeOnly.MinValue).AddDays(1);
        var ids = employeeProfileIds.ToList();

        var rows = await context.AnnualLeaves.AsNoTracking()
            .Where(l => l.EmployeeProfileId != null
                && ids.Contains(l.EmployeeProfileId)
                && l.Status == AnnualLeaveStatus.Approved
                && l.StartDate < endExclusive && l.EndDate >= start)
            .Select(l => new { ProfileId = l.EmployeeProfileId!, l.StartDate, l.EndDate, l.Duration })
            .ToListAsync(cancellationToken);

        var leaves = rows
            .GroupBy(r => r.ProfileId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => (DateOnly.FromDateTime(r.StartDate), DateOnly.FromDateTime(r.EndDate), r.Duration)).ToList());

        return new DailyHoursContext(settings, holidays, leaves, DateOnly.FromDateTime(AttendanceDay.UtcDayStart(nowUtc)));
    }

    public bool IsWorkingDay(DateOnly day) => WorkingWeek.IsWorkingDay(_settings, day, _holidays);

    public LeaveOnDay LeaveOn(string employeeProfileId, DateOnly day) =>
        _leaves.TryGetValue(employeeProfileId, out var rows)
            ? DailyHoursRule.CombineLeave(rows.Where(r => r.Start <= day && r.End >= day).Select(r => r.Duration))
            : LeaveOnDay.None;

    /// <summary>A past date is over; today is over once checked out of; a future date is not.</summary>
    public bool IsClosed(DateOnly day, AttendanceDayState? state) =>
        day < TodayUtc || (day == TodayUtc && state?.Status == AttendanceDayStatus.Done);

    public DailyHoursVerdict Judge(string employeeProfileId, DateOnly day, AttendanceDayState? state) =>
        DailyHoursRule.Judge(
            IsWorkingDay(day),
            LeaveOn(employeeProfileId, day),
            ScheduledMinutes,
            state?.WorkedMinutes ?? 0,
            IsClosed(day, state));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~DailyHoursContextTests"`
Expected: PASS. If `AttendanceDayState`'s positional constructor differs from `(Status, CheckInAt, CheckOutAt, OnBreakSince, TotalBreakMinutes, WorkedMinutes, IsAutoBreak)`, check `Domain/Services/AttendanceDayStateCalculator.cs:42` and adjust the test's construction. Don't change the record.

- [ ] **Step 5: Commit**

```bash
git add Application/Attendance/Support/DailyHoursContext.cs Tests/WorkTrack.Tests/DailyHoursContextTests.cs
git commit -m "Load the working days and approved leave a short-day verdict needs, once per range

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: My Attendance history — `short`, `leave`, `off`

**Files:**
- Modify: `Application/Attendance/DTOs/AttendanceDtos.cs` (the `DayHistoryDto` record)
- Modify: `Application/Attendance/Queries/GetMyAttendanceHistory.cs`
- Modify: `Tests/WorkTrack.Tests/AttendanceActionsTests.cs:369-386`
- Test: `Tests/WorkTrack.Tests/ShortDayAttendanceTests.cs` (new; later tasks add to it)

**Interfaces:**
- Consumes: `DailyHoursContext.LoadAsync`, `.Judge`, `DayKind`.
- Produces: `DayHistoryDto(..., int? BreakVarianceMinutes = null, int? ShortByMinutes = null, bool OnLeave = false, int? TargetMinutes = null)`. Status vocabulary: `leave | off | absent | in-progress | short | late | complete`.

- [ ] **Step 1: Write the failing test**

Create `Tests/WorkTrack.Tests/ShortDayAttendanceTests.cs`:

```csharp
using Application.Attendance.Queries;
using Application.Attendance.Support;
using Domain;
using Domain.Services;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A working day that came in under the working hours net of the break, with no
/// approved leave to excuse it, is flagged on every attendance surface: the
/// employee's own history, the team board's week grid, and the dashboards' issue
/// lines. 08:00–17:00 with a flexible hour's break is an 8-hour day.
/// </summary>
public class ShortDayAttendanceTests
{
    private const int DepartmentId = 1;
    // Week of Mon 21 Sep 2026; "now" is Thursday afternoon.
    private static readonly DateTime Mon = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Mon.AddDays(3).AddHours(15);

    internal static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC", WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00",
            BreakMode = "flexible", BreakMinutes = 60, WorkingDays = "mon-fri",
        });
        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });

        // Sam: Mon full 8h, Tue 6h30 (short by 1h30), Wed 7h50 (within grace), Thu still in.
        AddEmployee(db, "sam", "Sam Short");
        Day(db, "sam", Mon, 8, 17, breakMinutes: 60);
        Day(db, "sam", Mon.AddDays(1), 8, 15, breakMinutes: 30);          // 7h − 30m = 6h30
        Day(db, "sam", Mon.AddDays(2), 8, 16, breakMinutes: 10);          // 8h − 10m = 7h50
        Add(db, "sam", Mon.AddDays(3).AddHours(8), AttendanceEventType.CheckIn);

        // Lea: on approved leave Tuesday, half-day Wednesday (worked 3h), nothing Monday.
        AddEmployee(db, "lea", "Lea Leave");
        Leave(db, "lea", Mon.AddDays(1), LeaveDuration.Full);
        Leave(db, "lea", Mon.AddDays(2), LeaveDuration.HalfDayMorning);
        Day(db, "lea", Mon.AddDays(2), 13, 16, breakMinutes: 0);           // 3h of a 4h target
        Day(db, "lea", Mon.AddDays(3), 8, 17, breakMinutes: 60);

        db.SaveChanges();
        return db;
    }

    private static void AddEmployee(AppDbContext db, string key, string name)
    {
        db.Users.Add(new User { Id = $"u-{key}", UserName = key, DisplayName = name });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"p-{key}", UserId = $"u-{key}", DepartmentId = DepartmentId });
    }

    private static void Day(AppDbContext db, string key, DateTime day, int inHour, int outHour, int breakMinutes)
    {
        Add(db, key, day.AddHours(inHour), AttendanceEventType.CheckIn);
        if (breakMinutes > 0)
        {
            Add(db, key, day.AddHours(12), AttendanceEventType.BreakStart);
            Add(db, key, day.AddHours(12).AddMinutes(breakMinutes), AttendanceEventType.BreakEnd);
        }
        Add(db, key, day.AddHours(outHour), AttendanceEventType.CheckOut);
    }

    private static void Add(AppDbContext db, string key, DateTime at, AttendanceEventType type) =>
        db.AttendanceEvents.Add(AttendanceDay.NewEvent($"p-{key}", at, type));

    private static void Leave(AppDbContext db, string key, DateTime day, LeaveDuration duration) =>
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = $"u-{key}",
            EmployeeProfileId = $"p-{key}",
            DepartmentId = DepartmentId,
            StartDate = day,
            EndDate = day,
            Status = AnnualLeaveStatus.Approved,
            Duration = duration,
        });

    private static async Task<List<Application.Attendance.DTOs.DayHistoryDto>> History(AppDbContext db, string key)
    {
        var result = await new GetMyAttendanceHistory.Handler(db).Handle(
            new GetMyAttendanceHistory.Query { RequestingUserId = $"u-{key}", Days = 6, NowUtc = Now },
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    // ── My Attendance ─────────────────────────────────────────────────────────

    [Fact]
    public async Task History_grades_a_short_day_and_leaves_one_within_the_grace_alone()
    {
        using var db = SeedWorld();
        var days = (await History(db, "sam")).ToDictionary(d => d.Date);

        Assert.Equal("complete", days["2026-09-21"].Status);
        Assert.Null(days["2026-09-21"].ShortByMinutes);
        Assert.Equal("short", days["2026-09-22"].Status);
        Assert.Equal(90, days["2026-09-22"].ShortByMinutes);
        Assert.Equal(480, days["2026-09-22"].TargetMinutes);
        Assert.Equal("complete", days["2026-09-23"].Status);            // 10 min short: within grace
        Assert.Equal("in-progress", days["2026-09-24"].Status);
        Assert.Null(days["2026-09-24"].ShortByMinutes);                  // open day says nothing
    }

    [Fact]
    public async Task History_reads_leave_as_leave_and_a_weekend_as_off()
    {
        using var db = SeedWorld();
        var days = (await History(db, "lea")).ToDictionary(d => d.Date);

        Assert.Equal("off", days["2026-09-19"].Status);                  // Saturday
        Assert.Null(days["2026-09-19"].ShortByMinutes);
        Assert.Equal("absent", days["2026-09-21"].Status);               // nothing, no leave
        Assert.Equal(480, days["2026-09-21"].ShortByMinutes);
        Assert.Equal("leave", days["2026-09-22"].Status);
        Assert.True(days["2026-09-22"].OnLeave);
        Assert.Null(days["2026-09-22"].ShortByMinutes);
        Assert.Equal(240, days["2026-09-23"].TargetMinutes);             // half day
        Assert.Equal(60, days["2026-09-23"].ShortByMinutes);
        Assert.Equal("short", days["2026-09-23"].Status);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~ShortDayAttendanceTests"`
Expected: build FAIL — `DayHistoryDto` has no `ShortByMinutes`, `TargetMinutes` or `OnLeave`.

- [ ] **Step 3: Extend the DTO**

In `Application/Attendance/DTOs/AttendanceDtos.cs`, replace the `DayHistoryDto` record with:

```csharp
// ShortByMinutes, wherever it appears below, is DailyHoursRule's verdict: minutes
// under the day's target (the working hours net of the break, halved for a half
// day of approved leave) beyond the 15-minute grace, on a working day that is
// over. Null when there is nothing to say — a day within the grace, on leave, not
// a working day, or still open. OnLeave is approved full-day leave.
public record DayHistoryDto(
    string Date,
    string Status,
    DateTime? CheckInAt,
    DateTime? CheckOutAt,
    int TotalBreakMinutes,
    int WorkedMinutes,
    int? BreakVarianceMinutes = null,
    int? ShortByMinutes = null,
    bool OnLeave = false,
    int? TargetMinutes = null);
```

- [ ] **Step 4: Judge each day in the query**

In `Application/Attendance/Queries/GetMyAttendanceHistory.cs`, update the class summary's first sentence to say "…including days with no events at all, graded against the day's target (DailyHoursRule)". Then:

After `var byDay = ...ToDictionary(...);` add:

```csharp
            var hours = await DailyHoursContext.LoadAsync(
                context, [profile.Id], DateOnly.FromDateTime(from), DateOnly.FromDateTime(today), now, cancellationToken);
```

Replace the loop body's `result.Add(...)` with:

```csharp
                var verdict = hours.Judge(profile.Id, DateOnly.FromDateTime(date), state);

                result.Add(new DayHistoryDto(
                    date.ToString("yyyy-MM-dd"),
                    HistoryStatus(state, schedule, verdict),
                    AttendanceDay.AsUtcNullable(state.CheckInAt),
                    AttendanceDay.AsUtcNullable(state.CheckOutAt),
                    state.TotalBreakMinutes,
                    state.WorkedMinutes,
                    schedule.BreakVariance(state, now),
                    verdict.ShortByMinutes,
                    verdict.Kind == DayKind.Leave,
                    verdict.TargetMinutes));
```

Replace `HistoryStatus` (with its doc comment) with:

```csharp
        /// <summary>
        /// The history strip has its own vocabulary. Approved leave reads as leave
        /// whatever else happened; a non-working day with nothing on it is off; a
        /// working day with nothing is absent; a day still open is in-progress; a
        /// finished day under its target is short, and otherwise late or complete
        /// on its check-in against the org's working-hours start. Short outranks
        /// late: the check-in time is in its own column anyway.
        /// </summary>
        private static string HistoryStatus(AttendanceDayState state, WorkingDaySchedule schedule, DailyHoursVerdict verdict)
        {
            if (verdict.Kind == DayKind.Leave) return "leave";
            if (state.Status == AttendanceDayStatus.Out)
                return verdict.Kind == DayKind.NonWorking ? "off" : "absent";
            if (state.Status is AttendanceDayStatus.In or AttendanceDayStatus.Break) return "in-progress";
            if (verdict.ShortByMinutes is not null) return "short";
            return state.CheckInAt.HasValue && schedule.IsLate(state.CheckInAt.Value) ? "late" : "complete";
        }
```

- [ ] **Step 5: Stop the existing real-clock test depending on the weekday**

In `Tests/WorkTrack.Tests/AttendanceActionsTests.cs`, in `History_reports_absent_days_and_the_day_in_progress`, replace:

```csharp
        Assert.All(days.Take(2), d => Assert.Equal("absent", d.Status));
```

with:

```csharp
        // Runs on the real clock, so the two days before today may be a weekend,
        // which reads as off rather than absent.
        Assert.All(days.Take(2), d => Assert.Contains(d.Status, new[] { "absent", "off" }));
```

Also update that test's doc comment to say "A day with no events reads as absent (or off on a non-working day)".

- [ ] **Step 6: Run the attendance tests**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~Attendance"`
Expected: all PASS. If `AttendanceBreakAllowanceTests` or `AttendanceLatenessFollowsSettingsTests` assert `"complete"`/`"late"` on a day that is now `short`, open the seed. Fix it by making the day full length (extend the check-out) so the test still pins what it was written for. Don't change the expected string to `short`.

- [ ] **Step 7: Commit**

```bash
git add Application/Attendance/DTOs/AttendanceDtos.cs Application/Attendance/Queries/GetMyAttendanceHistory.cs Tests/WorkTrack.Tests/ShortDayAttendanceTests.cs Tests/WorkTrack.Tests/AttendanceActionsTests.cs
git commit -m "Grade short days, leave and days off on the employee's attendance history

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Team board week grid and the previous-day digest

**Files:**
- Create: `Application/Attendance/Support/ShortDayDigest.cs`
- Modify: `Application/Attendance/DTOs/AttendanceDtos.cs` (`WeekDayHoursDto`, `TeamAttendanceDto`, new `ShortDayDto`)
- Modify: `Application/Attendance/Queries/GetTeamAttendance.cs`
- Test: `Tests/WorkTrack.Tests/ShortDayAttendanceTests.cs` (append)

**Interfaces:**
- Consumes: `DailyHoursContext`, `DayKind`, `AttendanceDay.LoadDayEventsByEmployeeAsync`, `AttendanceDay.StateFor`.
- Produces:
  - `record ShortDayDto(string EmployeeId, string EmployeeName, string DepartmentName, int WorkedMinutes, int ShortByMinutes)`
  - `record WeekDayHoursDto(string Date, int? WorkedMinutes, string? Note, int? ShortByMinutes = null, bool OnLeave = false)`
  - `record TeamAttendanceDto(List<TeamMemberAttendanceDto> Members, List<TeamWeekRowDto> Week, string? ShortDaysDate = null, List<ShortDayDto>? ShortDays = null)`
  - `ShortDayDigest.LookBackDays` (const 14); `ShortDayDigest.PreviousWorkingDay(DailyHoursContext, DateOnly today) → DateOnly?`; `ShortDayDigest.BuildAsync(AppDbContext, IReadOnlyList<EmployeeProfile>, DailyHoursContext, DateTime nowUtc, CancellationToken) → Task<ShortDayDigest.Digest?>` with `record Digest(DateOnly Day, List<ShortDayDto> People)`

- [ ] **Step 1: Write the failing tests**

Append inside the `ShortDayAttendanceTests` class:

```csharp
    // ── Team board ────────────────────────────────────────────────────────────

    private static async Task<Application.Attendance.DTOs.TeamAttendanceDto> Team(AppDbContext db, DateTime? now = null)
    {
        var result = await new GetTeamAttendance.Handler(db).Handle(
            new GetTeamAttendance.Query { RequestingUserId = "nobody", IsAdmin = true, NowUtc = now ?? Now },
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task Week_grid_marks_short_days_and_leave()
    {
        using var db = SeedWorld();
        var week = (await Team(db)).Week;

        var sam = Assert.Single(week, r => r.EmployeeName == "Sam Short").Days;
        Assert.Null(sam[0].ShortByMinutes);
        Assert.Equal(90, sam[1].ShortByMinutes);
        Assert.Null(sam[2].ShortByMinutes);   // within grace
        Assert.Null(sam[3].ShortByMinutes);   // today, still in
        Assert.Null(sam[4].ShortByMinutes);   // Friday, not yet

        var lea = Assert.Single(week, r => r.EmployeeName == "Lea Leave").Days;
        Assert.Equal(480, lea[0].ShortByMinutes);   // no attendance, no leave
        Assert.Null(lea[0].WorkedMinutes);
        Assert.True(lea[1].OnLeave);
        Assert.Null(lea[1].ShortByMinutes);
        Assert.Equal(60, lea[2].ShortByMinutes);    // 3h of a half-day 4h
    }

    [Fact]
    public async Task Team_board_lists_who_was_short_on_the_previous_working_day()
    {
        using var db = SeedWorld();
        var team = await Team(db, Mon.AddDays(2).AddHours(10)); // Wednesday morning → Tuesday

        Assert.Equal("2026-09-22", team.ShortDaysDate);
        var sam = Assert.Single(team.ShortDays!);   // Lea was on leave Tuesday
        Assert.Equal("Sam Short", sam.EmployeeName);
        Assert.Equal(90, sam.ShortByMinutes);
        Assert.Equal(390, sam.WorkedMinutes);
    }

    [Fact]
    public async Task On_a_Monday_the_digest_looks_back_to_Friday()
    {
        using var db = SeedWorld();
        var team = await Team(db, Mon.AddDays(7).AddHours(10));

        Assert.Equal("2026-09-25", team.ShortDaysDate);
        // Nobody recorded Friday, and nobody was on leave: both are short by the whole day.
        Assert.Equal(2, team.ShortDays!.Count);
        Assert.All(team.ShortDays, s => Assert.Equal(480, s.ShortByMinutes));
    }

    [Fact]
    public async Task A_past_day_left_checked_in_is_not_called_short()
    {
        using var db = SeedWorld();
        // Friday: Sam checks in and never checks out. The calculator runs the day to now.
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-sam", Mon.AddDays(4).AddHours(8), AttendanceEventType.CheckIn));
        db.SaveChanges();

        var team = await Team(db, Mon.AddDays(7).AddHours(10));

        Assert.DoesNotContain(team.ShortDays!, s => s.EmployeeName == "Sam Short");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~ShortDayAttendanceTests"`
Expected: build FAIL — `WeekDayHoursDto.ShortByMinutes`, `TeamAttendanceDto.ShortDays` do not exist.

- [ ] **Step 3: Extend the DTOs**

In `Application/Attendance/DTOs/AttendanceDtos.cs`, replace `WeekDayHoursDto` and `TeamAttendanceDto` with:

```csharp
public record WeekDayHoursDto(
    string Date,
    int? WorkedMinutes,
    string? Note,
    int? ShortByMinutes = null,
    bool OnLeave = false);
```

```csharp
// One person short on the previous working day (ShortDayDigest), for the
// dashboards' "short days" line.
public record ShortDayDto(
    string EmployeeId,
    string EmployeeName,
    string DepartmentName,
    int WorkedMinutes,
    int ShortByMinutes);

// ShortDaysDate is the previous working day ShortDays judges ("yyyy-MM-dd"),
// null when none was found in the look-back.
public record TeamAttendanceDto(
    List<TeamMemberAttendanceDto> Members,
    List<TeamWeekRowDto> Week,
    string? ShortDaysDate = null,
    List<ShortDayDto>? ShortDays = null);
```

- [ ] **Step 4: Write `ShortDayDigest`**

Create `Application/Attendance/Support/ShortDayDigest.cs`:

```csharp
using Application.Attendance.DTOs;
using Domain;
using Persistence;

namespace Application.Attendance.Support;

/// <summary>
/// Who was short on the previous working day, for the dashboards. Today is not
/// judged — it is not over — so the line always speaks about a finished day: on a
/// Monday that is Friday, after a Tuesday holiday it is Monday.
/// </summary>
public static class ShortDayDigest
{
    /// <summary>How far back a previous working day is looked for. The loaded context must cover it.</summary>
    public const int LookBackDays = 14;

    public sealed record Digest(DateOnly Day, List<ShortDayDto> People);

    public static DateOnly? PreviousWorkingDay(DailyHoursContext hours, DateOnly today)
    {
        for (var back = 1; back <= LookBackDays; back++)
        {
            var candidate = today.AddDays(-back);
            if (hours.IsWorkingDay(candidate)) return candidate;
        }
        return null;
    }

    public static async Task<Digest?> BuildAsync(
        AppDbContext context,
        IReadOnlyList<EmployeeProfile> profiles,
        DailyHoursContext hours,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (PreviousWorkingDay(hours, hours.TodayUtc) is not { } day) return null;

        var dayStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ids = profiles.Select(p => p.Id).ToList();
        var byEmployee = await AttendanceDay.LoadDayEventsByEmployeeAsync(context, ids, dayStart, cancellationToken);

        var people = new List<ShortDayDto>();
        foreach (var profile in profiles)
        {
            var state = AttendanceDay.StateFor(byEmployee, profile.Id, nowUtc);
            if (hours.Judge(profile.Id, day, state).ShortByMinutes is not { } shortBy) continue;

            people.Add(new ShortDayDto(
                profile.Id,
                AttendanceDay.DisplayNameOf(profile),
                profile.Department?.Name ?? "Unassigned",
                state.WorkedMinutes,
                shortBy));
        }

        return new Digest(day, people);
    }
}
```

- [ ] **Step 5: Use it in `GetTeamAttendance`**

In `Application/Attendance/Queries/GetTeamAttendance.cs`:

(a) Change the class summary's first line to: "The team board: today's status per member, a Mon–Fri minutes grid for the current ISO week with short days and leave marked (DailyHoursRule), and who was short on the previous working day."

(b) After `var onLeave = ...;` add:

```csharp
            var today = DateOnly.FromDateTime(AttendanceDay.UtcDayStart(now));
            var hours = await DailyHoursContext.LoadAsync(
                context, profileIds, today.AddDays(-ShortDayDigest.LookBackDays), today.AddDays(6), now, cancellationToken);
```

(c) Replace `var week = await BuildWeekAsync(profiles, profileIds, now, cancellationToken);` and the `return` with:

```csharp
            var week = await BuildWeekAsync(profiles, profileIds, hours, now, cancellationToken);
            var digest = await ShortDayDigest.BuildAsync(context, profiles, hours, now, cancellationToken);

            return Result<TeamAttendanceDto>.Success(new TeamAttendanceDto(
                members,
                week,
                digest?.Day.ToString("yyyy-MM-dd"),
                digest?.People ?? []));
```

(d) Change the `BuildWeekAsync` signature to take `DailyHoursContext hours` after `profileIds`, update its summary to add "Each day also carries DailyHoursRule's verdict: a short day's minutes and approved leave.", and replace the `days.Add(...)` line with:

```csharp
                    var verdict = hours.Judge(profile.Id, DateOnly.FromDateTime(dayStart), state);
                    days.Add(new WeekDayHoursDto(
                        dayStart.ToString("yyyy-MM-dd"),
                        minutes,
                        note,
                        verdict.ShortByMinutes,
                        verdict.Kind == DayKind.Leave));
```

- [ ] **Step 6: Run the tests**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~Attendance"`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add Application/Attendance/Support/ShortDayDigest.cs Application/Attendance/DTOs/AttendanceDtos.cs Application/Attendance/Queries/GetTeamAttendance.cs Tests/WorkTrack.Tests/ShortDayAttendanceTests.cs
git commit -m "Mark short days and leave on the team board, and list who was short on the previous working day

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Company dashboard — short-days issue, overtime on the schedule

**Files:**
- Modify: `Application/Attendance/Queries/GetCompanyAttendance.cs`
- Test: `Tests/WorkTrack.Tests/ShortDayAttendanceTests.cs` (append)

**Interfaces:**
- Consumes: `DailyHoursContext.LoadAsync`, `ShortDayDigest.BuildAsync`, `DailyHoursRule.Describe`, `DailyHoursRule.GraceMinutes`.
- Produces: `IssueDto("warning", "{n} short day(s) on {ddd d MMM}", "{Name} ({Dept}) · {1h 30m} short · …")`. Overtime issue title `"{n} over the {8h} working day today"`.

- [ ] **Step 1: Write the failing tests**

Append inside `ShortDayAttendanceTests`:

```csharp
    // ── Company dashboard ─────────────────────────────────────────────────────

    private static async Task<List<Application.Attendance.DTOs.IssueDto>> CompanyIssues(AppDbContext db, DateTime now)
    {
        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query { NowUtc = now }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Issues;
    }

    [Fact]
    public async Task Company_issues_name_who_was_short_on_the_previous_working_day()
    {
        using var db = SeedWorld();
        var issues = await CompanyIssues(db, Mon.AddDays(2).AddHours(10));

        var issue = Assert.Single(issues, i => i.Title.Contains("short day"));
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("1 short day on Tue 22 Sep", issue.Title);
        Assert.Contains("Sam Short (Engineering) · 1h 30m short", issue.Detail);
        Assert.DoesNotContain("Lea", issue.Detail);   // on leave that day
    }

    [Fact]
    public async Task On_Tuesday_morning_the_issue_speaks_about_Monday()
    {
        using var db = SeedWorld();
        var issues = await CompanyIssues(db, Mon.AddDays(1).AddHours(10)); // Tuesday → Monday

        // Monday: Sam made 8h; Lea recorded nothing and had no leave, so she is short.
        var issue = Assert.Single(issues, i => i.Title.Contains("short day"));
        Assert.Equal("1 short day on Mon 21 Sep", issue.Title);
        Assert.Contains("Lea Leave", issue.Detail);
        Assert.DoesNotContain("Sam", issue.Detail);
    }

    [Fact]
    public async Task Overtime_is_judged_against_the_scheduled_day_and_leaves_out_people_on_leave()
    {
        using var db = SeedWorld();
        // Thursday 15:00: Oli has worked 8h40 (over 8h + the 15-minute grace).
        // Lou has worked 9h too, but is on approved leave today, so is not counted.
        AddEmployee(db, "oli", "Oli Over");
        Add(db, "oli", Mon.AddDays(3).AddHours(6).AddMinutes(20), AttendanceEventType.CheckIn);
        AddEmployee(db, "lou", "Lou Leave");
        Add(db, "lou", Mon.AddDays(3).AddHours(6), AttendanceEventType.CheckIn);
        Leave(db, "lou", Mon.AddDays(3), LeaveDuration.Full);
        db.SaveChanges();

        var issues = await CompanyIssues(db, Now);

        Assert.Contains(issues, i => i.Title == "1 over the 8h working day today");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~ShortDayAttendanceTests.Company|FullyQualifiedName~ShortDayAttendanceTests.No_short|FullyQualifiedName~ShortDayAttendanceTests.Overtime"`
Expected: FAIL — no "short day" issue; overtime title is "… over 10 hours today".

- [ ] **Step 3: Implement**

In `Application/Attendance/Queries/GetCompanyAttendance.cs`:

(a) Delete the `private const int OvertimeMinutes = 600;` line. Change the constants' summary to end: "…flagged nothing before one in the afternoon. Overtime is not one either: it is the scheduled day net of the break (WorkingDaySchedule.ScheduledMinutes) plus DailyHoursRule's grace, where it used to be a flat ten hours." Add beside the other name-count constants:

```csharp
    private const int ShortDayNamesShown = 3;
```

(b) In `Handle`, replace:

```csharp
            var issues = BuildIssues(profiles, stateByProfileId, onLeave, departments, totals, schedule, now);
```

with:

```csharp
            var today = DateOnly.FromDateTime(AttendanceDay.UtcDayStart(now));
            var hours = await DailyHoursContext.LoadAsync(
                context, profileIds, today.AddDays(-ShortDayDigest.LookBackDays), today, now, cancellationToken);
            var shortDays = await ShortDayDigest.BuildAsync(context, profiles, hours, now, cancellationToken);

            var issues = BuildIssues(profiles, stateByProfileId, onLeave, departments, totals, schedule, shortDays, now);
```

(c) Add `ShortDayDigest.Digest? shortDays,` to the `BuildIssues` parameter list, before `DateTime now`.

(d) After the late check-ins block (`if (lateNames.Count > 0) { ... }`), insert:

```csharp
            // 2b) Short days on the previous working day: under the day's target
            //     net of the break, with no approved leave to excuse it
            //     (DailyHoursRule). Today is not judged; it is not over.
            if (shortDays is { People.Count: > 0 } digest)
            {
                var n = digest.People.Count;
                issues.Add(new IssueDto(
                    "warning",
                    $"{n} short day{(n == 1 ? "" : "s")} on {digest.Day.ToString("ddd d MMM", CultureInfo.InvariantCulture)}",
                    string.Join(" · ", digest.People
                        .Take(ShortDayNamesShown)
                        .Select(p => $"{p.EmployeeName} ({p.DepartmentName}) · {DailyHoursRule.Describe(p.ShortByMinutes)} short"))));
            }
```

Add `using System.Globalization;` at the top. The invariant culture keeps the title English ("Tue 22 Sep") whatever culture the host runs in.

(e) Replace the overtime block (section 5) with:

```csharp
            // 5) Overtime, against the scheduled day net of the break plus the grace,
            // people on leave left out. Always reported, so the panel says something
            // reassuring when nothing is wrong rather than going blank.
            var overtimeThreshold = schedule.ScheduledMinutes + DailyHoursRule.GraceMinutes;
            var overtime = schedule.ScheduledMinutes <= 0
                ? 0
                : profiles.Count(p => !onLeave.Contains(p.Id) && stateByProfileId[p.Id].WorkedMinutes > overtimeThreshold);
            issues.Add(overtime == 0
                ? new IssueDto("success", "No unusual overtime", "All employees within healthy hour ranges")
                : new IssueDto("warning", $"{overtime} over the {DailyHoursRule.Describe(schedule.ScheduledMinutes)} working day today", "Consider checking in"));
```

- [ ] **Step 4: Run the tests**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~Attendance"`
Expected: all PASS. Check that `CompanyAttendanceAggregationTests` and `AttendanceLatenessFollowsSettingsTests` still pass. If one filters issues by severity `"warning"` and now meets the extra short-day line, narrow its filter by title rather than deleting the new issue.

- [ ] **Step 5: Commit**

```bash
git add Application/Attendance/Queries/GetCompanyAttendance.cs Tests/WorkTrack.Tests/ShortDayAttendanceTests.cs
git commit -m "Name the previous working day's short days on the company dashboard, and judge overtime against the schedule

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Daily attendance report — "Short day" section

**Files:**
- Modify: `Application/Reminders/ReminderDispatcher.cs` (`DailyReport` record ~L552, `BuildDailyAttendanceReportAsync` ~L564-690, both renderers ~L715-750)
- Test: `Tests/WorkTrack.Tests/DailyAttendanceReportTests.cs` (append)

**Interfaces:**
- Consumes: `DailyHoursRule.Judge`, `DailyHoursRule.CombineLeave`, `LeaveOnDay`.
- Produces: an HTML `<h3>Short day</h3>` section and a text `Short day:` section, placed after Overtime. The section is left out when `ScheduledMinutes <= 0`.

- [ ] **Step 1: Write the failing tests**

Append inside `DailyAttendanceReportTests`. In `SeedWorld` the report day runs 09:00–18:00 with no break (540 min). Eve worked 09:30–17:00 (450) and Fay 11h.

```csharp
    [Fact]
    public async Task Report_lists_who_checked_out_under_the_scheduled_day()
    {
        using var db = SeedWorld();

        var mail = await RunAsync(db, Settings());

        var shortDay = Section(mail.HtmlBody, "Short day");
        Assert.Contains("Eve Employee (Engineering) — 1h 30m short (worked 7h 30m of 9h 00m)", shortDay);
        Assert.DoesNotContain("Fay", shortDay);    // overtime, not short
        Assert.DoesNotContain("Bob", shortDay);    // never checked out: reported under that heading
        Assert.DoesNotContain("Cara", shortDay);   // never checked in: reported under that heading
        Assert.DoesNotContain("Dan", shortDay);    // on leave
        Assert.Contains("Short day:", mail.TextBody);
    }

    [Fact]
    public async Task A_half_day_of_leave_halves_the_day_the_report_expects()
    {
        using var db = SeedWorld();
        SeedPerson(db, "hal", "Hal Half", "hal@example.com", DepartmentId);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = "hal-u", EmployeeProfileId = "hal-p", DepartmentId = DepartmentId, LeaveTypeId = 1,
            StartDate = Yesterday, EndDate = Yesterday, Status = AnnualLeaveStatus.Approved,
            Duration = Domain.Services.LeaveDuration.HalfDayMorning, CreatedAt = DateTime.UtcNow,
        });
        // 4h 30m worked against a 4h 30m half day: not short.
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("hal-p", Yesterday.AddHours(13), AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("hal-p", Yesterday.AddHours(17).AddMinutes(30), AttendanceEventType.CheckOut));
        db.SaveChanges();

        var mail = await RunAsync(db, Settings());

        Assert.DoesNotContain("Hal Half", Section(mail.HtmlBody, "Short day"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~DailyAttendanceReportTests"`
Expected: the two new tests FAIL with "Section 'Short day' missing from report".

- [ ] **Step 3: Implement**

In `Application/Reminders/ReminderDispatcher.cs` (add `using Application.Attendance.Support;` if it isn't already imported):

(a) In the `DailyReport` record, add `List<string>? ShortDay,` right after `List<string> Overtime,`.

(b) Replace the `onLeave` query with one that keeps the duration:

```csharp
        var onLeave = (await context.AnnualLeaves
            .Where(l => l.Status == AnnualLeaveStatus.Approved
                        && l.StartDate < dayEnd && l.EndDate >= dayStart
                        && userIds.Contains(l.EmployeeId))
            .Select(l => new { l.EmployeeId, LeaveType = l.LeaveType != null ? l.LeaveType.Name : null, l.Duration })
            .ToListAsync(ct))
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => (Name: g.First().LeaveType, Covers: DailyHoursRule.CombineLeave(g.Select(x => x.Duration))));
```

(c) After `var overtime = new List<string>();` add:

```csharp
        // Checked-out days under the scheduled day net of the break, beyond the
        // grace, with approved leave taken off the target (DailyHoursRule). Null,
        // and no section, when the settings describe no working day at all.
        var shortDay = scheduledMinutes > 0 ? new List<string>() : null;
```

(d) In the loop, change `var isOnLeave = onLeave.TryGetValue(person.UserId, out var leaveType);` and the leave line to:

```csharp
            var isOnLeave = onLeave.TryGetValue(person.UserId, out var leaveInfo);

            if (isOnLeave)
                leave.Add($"{label} — {leaveInfo.Name ?? "Leave"}");
```

and replace the `if (state.CheckOutAt is null) ... else if (overtime) ...` block with:

```csharp
                if (state.CheckOutAt is null)
                {
                    notOut.Add(label);
                }
                else
                {
                    if (state.WorkedMinutes > scheduledMinutes)
                        overtime.Add($"{label} — {HoursAndMinutes(state.WorkedMinutes - scheduledMinutes)} over (worked {HoursAndMinutes(state.WorkedMinutes)})");

                    var verdict = DailyHoursRule.Judge(
                        isWorkingDay: true,
                        isOnLeave ? leaveInfo.Covers : LeaveOnDay.None,
                        scheduledMinutes,
                        state.WorkedMinutes,
                        dayClosed: true);
                    if (shortDay is not null && verdict.ShortByMinutes is { } shortBy)
                        shortDay.Add($"{label} — {HoursAndMinutes(shortBy)} short (worked {HoursAndMinutes(state.WorkedMinutes)} of {HoursAndMinutes(verdict.TargetMinutes!.Value)})");
                }
```

`isWorkingDay: true` is correct here because the report only ever runs for the previous working day.

(e) Pass `shortDay` in the `return new DailyReport(...)` right after `overtime`.

(f) In `RenderDailyReportHtml`, after `{Section("Overtime", r.Overtime)}` add:

```csharp
{(r.ShortDay is null ? "" : Section("Short day", r.ShortDay))}
```

In `RenderDailyReportText`, after `Section("Overtime", r.Overtime),` add:

```csharp
            r.ShortDay is null ? null : Section("Short day", r.ShortDay),
```

- [ ] **Step 4: Run the tests**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~DailyAttendanceReportTests|FullyQualifiedName~Reminder"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add Application/Reminders/ReminderDispatcher.cs Tests/WorkTrack.Tests/DailyAttendanceReportTests.cs
git commit -m "Add a Short day section to the daily attendance report

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Timesheet list — compare each day with attendance

**Files:**
- Create: `Application/Timesheets/Support/TimesheetAttendanceComparison.cs`
- Modify: `Application/Timesheets/DTOs/TimesheetDto.cs`
- Modify: `Application/Timesheets/Queries/GetTimesheetList.cs`
- Test: `Tests/WorkTrack.Tests/TimesheetAttendanceMismatchTests.cs`

**Interfaces:**
- Consumes: `DailyHoursContext`, `DailyHoursRule.MismatchMinutes`, `DayKind`, `AttendanceDayStateCalculator.Calculate`.
- Produces:
  - `TimesheetDto.AttendanceMinutes: List<int>?`, `DayMismatchMinutes: List<int?>?`, `OnLeaveDays: List<bool>?`, `MismatchDayCount: int` (JSON: `attendanceMinutes`, `dayMismatchMinutes`, `onLeaveDays`, `mismatchDayCount`)
  - `GetTimesheetList.Query.NowUtc: DateTime?`
  - `TimesheetAttendanceComparison.WindowWeeks = 12`; `TimesheetAttendanceComparison.LoadAsync(AppDbContext, IReadOnlyList<Timesheet>, DateTime nowUtc, CancellationToken) → Task<TimesheetAttendanceComparison>`; `.For(string timesheetId) → Week?` with `record Week(List<int> AttendanceMinutes, List<int?> MismatchMinutes, List<bool> OnLeave, int MismatchDayCount)`

- [ ] **Step 1: Write the failing test**

Create `Tests/WorkTrack.Tests/TimesheetAttendanceMismatchTests.cs`:

```csharp
using Application.Attendance.Support;
using Application.Timesheets.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Each day of a timesheet is compared with the time attendance recorded for it.
/// A difference beyond the 15-minute grace is shown to the reviewer; a day of
/// approved leave is never a mismatch; a day still open is not judged. Flag only:
/// nothing about submitting or approving changes.
/// </summary>
public class TimesheetAttendanceMismatchTests
{
    private static readonly DateTime Mon = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc); // Saturday

    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC", WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00",
            BreakMode = "flexible", BreakMinutes = 60, WorkingDays = "mon-fri",
        });
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = 1, Name = "Alpha", Code = "ALP" });
        db.Users.Add(new User { Id = "u-tia", UserName = "tia", DisplayName = "Tia Timesheet" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p-tia", UserId = "u-tia", DepartmentId = 1 });

        // Attendance: Mon 8h, Tue 6h30, Wed on leave, Thu nothing, Fri nothing.
        Worked(db, Mon, 8, 16);
        Worked(db, Mon.AddDays(1), 8, 14, 30);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "l-1", EmployeeId = "u-tia", EmployeeProfileId = "p-tia", DepartmentId = 1,
            StartDate = Mon.AddDays(2), EndDate = Mon.AddDays(2), Status = AnnualLeaveStatus.Approved,
        });

        // Timesheet: Mon 8, Tue 8, Wed 4, Thu 8, Fri 0.
        var sheet = new Timesheet
        {
            Id = "ts-1", EmployeeProfileId = "p-tia", DepartmentId = 1,
            PeriodStart = new DateTime(2026, 9, 21), PeriodEnd = new DateTime(2026, 9, 27),
            Status = TimesheetStatus.Submitted, TotalHours = 28,
        };
        db.Timesheets.Add(sheet);
        Entry(db, 0, 8m);
        Entry(db, 1, 8m);
        Entry(db, 2, 4m);
        Entry(db, 3, 8m);

        // An old sheet, outside the comparison window.
        db.Timesheets.Add(new Timesheet
        {
            Id = "ts-old", EmployeeProfileId = "p-tia", DepartmentId = 1,
            PeriodStart = new DateTime(2026, 5, 4), PeriodEnd = new DateTime(2026, 5, 10),
            Status = TimesheetStatus.Approved, TotalHours = 0,
        });

        db.SaveChanges();
        return db;
    }

    private static void Worked(AppDbContext db, DateTime day, int inHour, int outHour, int outMinute = 0)
    {
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-tia", day.AddHours(inHour), AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-tia", day.AddHours(outHour).AddMinutes(outMinute), AttendanceEventType.CheckOut));
    }

    private static void Entry(AppDbContext db, int dayOffset, decimal hours) =>
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            TimesheetId = "ts-1", ProjectId = 1, Date = new DateTime(2026, 9, 21).AddDays(dayOffset), HoursWorked = hours,
        });

    private static async Task<List<Application.Timesheets.DTOs.TimesheetDto>> List(AppDbContext db)
    {
        var result = await new GetTimesheetList.Handler(db).Handle(
            new GetTimesheetList.Query { RequestingUserId = "nobody", IsAdmin = true, NowUtc = Now },
            CancellationToken.None);
        return result.Items;
    }

    [Fact]
    public async Task Each_day_carries_attendance_and_the_mismatch_beyond_the_grace()
    {
        using var db = SeedWorld();
        var ts = Assert.Single(await List(db), t => t.Id == "ts-1");

        Assert.Equal([480, 390, 0, 0, 0], ts.AttendanceMinutes);
        Assert.Equal([null, 90, null, 480, null], ts.DayMismatchMinutes);
        Assert.Equal([false, false, true, false, false], ts.OnLeaveDays);
        Assert.Equal(2, ts.MismatchDayCount);
    }

    [Fact]
    public async Task A_sheet_older_than_the_window_is_not_compared()
    {
        using var db = SeedWorld();
        var old = Assert.Single(await List(db), t => t.Id == "ts-old");

        Assert.Null(old.AttendanceMinutes);
        Assert.Null(old.DayMismatchMinutes);
        Assert.Null(old.OnLeaveDays);
        Assert.Equal(0, old.MismatchDayCount);
    }

    [Fact]
    public async Task Today_is_not_judged_while_it_is_open()
    {
        using var db = SeedWorld();
        // Friday is "today"; Tia is checked in with 2h so far and logged 8h.
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-tia", Mon.AddDays(4).AddHours(8), AttendanceEventType.CheckIn));
        Entry(db, 4, 8m);
        db.SaveChanges();

        var result = await new GetTimesheetList.Handler(db).Handle(
            new GetTimesheetList.Query { RequestingUserId = "nobody", IsAdmin = true, NowUtc = Mon.AddDays(4).AddHours(10) },
            CancellationToken.None);
        var ts = Assert.Single(result.Items, t => t.Id == "ts-1");

        Assert.Null(ts.DayMismatchMinutes![4]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~TimesheetAttendanceMismatchTests"`
Expected: build FAIL — `NowUtc`, `AttendanceMinutes` etc. do not exist.

- [ ] **Step 3: Extend the DTO**

In `Application/Timesheets/DTOs/TimesheetDto.cs`, after the `AwaitingManager` property add:

```csharp
        /// <summary>
        /// Minutes attendance recorded per weekday (index 0 = Monday … 4 = Friday),
        /// parallel to <see cref="DailyHours"/>. Null for a sheet older than
        /// <c>TimesheetAttendanceComparison.WindowWeeks</c>, which is not compared.
        /// </summary>
        public List<int>? AttendanceMinutes { get; set; }

        /// <summary>
        /// Logged minus attended per weekday, in minutes, when they differ by more
        /// than the grace (<c>DailyHoursRule.MismatchMinutes</c>); null on a day that
        /// agrees, a day of approved leave, or a day still open.
        /// </summary>
        public List<int?>? DayMismatchMinutes { get; set; }

        /// <summary>Approved full-day leave per weekday.</summary>
        public List<bool>? OnLeaveDays { get; set; }

        /// <summary>How many weekdays disagree with attendance; 0 when not compared.</summary>
        public int MismatchDayCount { get; set; }
```

- [ ] **Step 4: Write `TimesheetAttendanceComparison`**

Create `Application/Timesheets/Support/TimesheetAttendanceComparison.cs`:

```csharp
using Application.Attendance.Support;
using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Support;

/// <summary>
/// Each weekday of a timesheet against the time attendance recorded for it, judged
/// by <see cref="DailyHoursRule.MismatchMinutes"/>. Only sheets whose week starts
/// within <see cref="WindowWeeks"/> are compared: the list endpoint is unpaged by
/// default, and loading every attendance event behind every sheet ever filed would
/// make it pay for history nobody is reviewing any more.
/// </summary>
public sealed class TimesheetAttendanceComparison
{
    public const int WindowWeeks = 12;

    public sealed record Week(List<int> AttendanceMinutes, List<int?> MismatchMinutes, List<bool> OnLeave, int MismatchDayCount);

    private readonly Dictionary<string, Week> _byTimesheetId;

    private TimesheetAttendanceComparison(Dictionary<string, Week> byTimesheetId) => _byTimesheetId = byTimesheetId;

    public Week? For(string timesheetId) => _byTimesheetId.GetValueOrDefault(timesheetId);

    public static async Task<TimesheetAttendanceComparison> LoadAsync(
        AppDbContext context, IReadOnlyList<Timesheet> timesheets, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = AttendanceDay.UtcDayStart(nowUtc).AddDays(-7 * WindowWeeks);
        var inWindow = timesheets.Where(t => AttendanceDay.UtcDayStart(t.PeriodStart) >= cutoff).ToList();
        if (inWindow.Count == 0) return new([]);

        var profileIds = inWindow.Select(t => t.EmployeeProfileId).Distinct().ToList();
        var from = inWindow.Min(t => AttendanceDay.UtcDayStart(t.PeriodStart));
        var toExclusive = inWindow.Max(t => AttendanceDay.UtcDayStart(t.PeriodStart)).AddDays(5);

        var events = await context.AttendanceEvents.AsNoTracking()
            .Where(e => profileIds.Contains(e.EmployeeProfileId) && e.At >= from && e.At < toExclusive)
            .ToListAsync(cancellationToken);
        var byDay = events
            .GroupBy(e => (e.EmployeeProfileId, Day: AttendanceDay.UtcDayStart(e.At)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var hours = await DailyHoursContext.LoadAsync(
            context, profileIds, DateOnly.FromDateTime(from), DateOnly.FromDateTime(toExclusive.AddDays(-1)), nowUtc, cancellationToken);

        var result = new Dictionary<string, Week>();
        foreach (var sheet in inWindow)
        {
            var monday = AttendanceDay.UtcDayStart(sheet.PeriodStart);
            var attended = new List<int>(5);
            var mismatch = new List<int?>(5);
            var onLeave = new List<bool>(5);

            for (var i = 0; i < 5; i++)
            {
                var dayStart = monday.AddDays(i);
                var day = DateOnly.FromDateTime(dayStart);
                byDay.TryGetValue((sheet.EmployeeProfileId, dayStart), out var dayEvents);
                var state = AttendanceDayStateCalculator.Calculate(dayEvents ?? [], nowUtc);
                var verdict = hours.Judge(sheet.EmployeeProfileId, day, state);
                // Entry dates are calendar dates stored at midnight with no zone.
                var logged = sheet.Entries.Where(e => e.Date.Date == dayStart.Date).Sum(e => e.HoursWorked);

                attended.Add(state.WorkedMinutes);
                onLeave.Add(verdict.Kind == DayKind.Leave);
                mismatch.Add(hours.IsClosed(day, state)
                    ? DailyHoursRule.MismatchMinutes(verdict.Kind, logged, state.WorkedMinutes)
                    : null);
            }

            result[sheet.Id] = new Week(attended, mismatch, onLeave, mismatch.Count(m => m is not null));
        }

        return new(result);
    }
}
```

- [ ] **Step 5: Fill the fields in `GetTimesheetList`**

In `Application/Timesheets/Queries/GetTimesheetList.cs`:

(a) Add to `Query`:

```csharp
            /// <summary>Test seam for the clock; the controller leaves it null.</summary>
            public DateTime? NowUtc { get; init; }
```

(b) After `var timesheets = await pageQuery.ToListAsync(cancellationToken);` add:

```csharp
                var now = request.NowUtc ?? DateTime.UtcNow;

                // Each weekday against the time attendance recorded for it. Flag only:
                // the reviewer sees the difference, nothing is blocked.
                var comparison = await TimesheetAttendanceComparison.LoadAsync(_context, timesheets, now, cancellationToken);
```

and change `TimesheetReviewRule.ManagerAvailableAsync(_context, openSubmitters, DateTime.UtcNow, cancellationToken)` to use `now`.

(c) In the `Select`, add `var week = comparison.For(t.Id);` right after the `daily` loop, and add to the initializer after `AwaitingManager = ...`:

```csharp
                        AttendanceMinutes = week?.AttendanceMinutes,
                        DayMismatchMinutes = week?.MismatchMinutes,
                        OnLeaveDays = week?.OnLeave,
                        MismatchDayCount = week?.MismatchDayCount ?? 0,
```

- [ ] **Step 6: Run the tests**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd --filter "FullyQualifiedName~Timesheet"`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add Application/Timesheets/Support/TimesheetAttendanceComparison.cs Application/Timesheets/DTOs/TimesheetDto.cs Application/Timesheets/Queries/GetTimesheetList.cs Tests/WorkTrack.Tests/TimesheetAttendanceMismatchTests.cs
git commit -m "Compare each timesheet day with the time attendance recorded for it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Client wording and types

**Files:**
- Create: `client/src/lib/daily-hours.ts`
- Create: `client/src/lib/daily-hours.test.ts`
- Modify: `client/src/lib/types/attendance.ts`
- Modify: `client/src/lib/types/timesheet.ts`

**Interfaces:**
- Consumes: `formatBreakMinutes` from `client/src/lib/break-policy.ts` (`45 → "45 min"`, `60 → "1h"`, `90 → "1h 30m"`).
- Produces:
  - `describeShortDay(shortBy?: number | null): string | null` → `"1h 30m short"`
  - `describeMismatch(loggedHours: number, attendedMinutes: number | null | undefined, mismatch: number | null | undefined): string | null` → `"Logged 8h · attended 6h 30m"` / `"Logged 8h · no attendance"`
  - `describeMismatchCount(count?: number): string | null` → `"1 day doesn't match attendance"` / `"2 days don't match attendance"`
  - Types: `AttendanceHistoryStatus` adds `'short' | 'leave' | 'off'`; `AttendanceHistoryDay` gets `shortByMinutes?`, `onLeave?`, `targetMinutes?`; `WeekDayHours` gets `shortByMinutes?`, `onLeave?`; new `ShortDay`; `TeamAttendance` gets `shortDaysDate?`, `shortDays?`; `Timesheet` gets `attendanceMinutes?`, `dayMismatchMinutes?`, `onLeaveDays?`, `mismatchDayCount?`.

- [ ] **Step 1: Write the failing test**

Create `client/src/lib/daily-hours.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { describeMismatch, describeMismatchCount, describeShortDay } from './daily-hours'

/*
 * The server decides (DailyHoursRule); the client only words it. A missing or null
 * figure — an older API, or nothing to say — renders nothing.
 */
describe('describeShortDay', () => {
    it('words the minutes short', () => {
        expect(describeShortDay(90)).toBe('1h 30m short')
        expect(describeShortDay(480)).toBe('8h short')
        expect(describeShortDay(16)).toBe('16 min short')
    })
    it('says nothing without a figure', () => {
        expect(describeShortDay(null)).toBeNull()
        expect(describeShortDay(undefined)).toBeNull()
    })
})

describe('describeMismatch', () => {
    it('quotes both sides', () => {
        expect(describeMismatch(8, 390, 90)).toBe('Logged 8h · attended 6h 30m')
        expect(describeMismatch(6, 480, -120)).toBe('Logged 6h · attended 8h')
        expect(describeMismatch(7.5, 300, 150)).toBe('Logged 7h 30m · attended 5h')
    })
    it('says when nothing was attended', () => {
        expect(describeMismatch(8, 0, 480)).toBe('Logged 8h · no attendance')
    })
    it('says nothing when the day agrees', () => {
        expect(describeMismatch(8, 480, null)).toBeNull()
        expect(describeMismatch(8, undefined, undefined)).toBeNull()
    })
})

describe('describeMismatchCount', () => {
    it('counts days', () => {
        expect(describeMismatchCount(1)).toBe("1 day doesn't match attendance")
        expect(describeMismatchCount(3)).toBe("3 days don't match attendance")
    })
    it('says nothing for none', () => {
        expect(describeMismatchCount(0)).toBeNull()
        expect(describeMismatchCount(undefined)).toBeNull()
    })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/lib/daily-hours.test.ts`
Expected: FAIL — cannot resolve `./daily-hours`. Check the banner reads `RUN v3.x`, not v5.

- [ ] **Step 3: Write `daily-hours.ts`**

Create `client/src/lib/daily-hours.ts`:

```ts
import { formatBreakMinutes } from './break-policy'

/**
 * Wording for the server's short-day and timesheet-mismatch verdicts
 * (`Application/Attendance/Support/DailyHoursRule.cs`). Nothing here decides
 * anything: the target, the leave, the working week and the 15-minute grace are
 * all applied on the server, and every function renders nothing for a missing
 * figure, so an API predating the fields shows no flag rather than a false one.
 */

/** "1h 30m short", or null when there is nothing to say. */
export function describeShortDay(shortByMinutes: number | null | undefined): string | null {
    if (shortByMinutes === null || shortByMinutes === undefined) return null
    return `${formatBreakMinutes(shortByMinutes)} short`
}

/** "Logged 8h · attended 6h 30m", or null when the day agrees with attendance. */
export function describeMismatch(
    loggedHours: number,
    attendedMinutes: number | null | undefined,
    mismatchMinutes: number | null | undefined,
): string | null {
    if (mismatchMinutes === null || mismatchMinutes === undefined) return null
    const logged = formatBreakMinutes(Math.round(loggedHours * 60))
    const attended = attendedMinutes ? `attended ${formatBreakMinutes(attendedMinutes)}` : 'no attendance'
    return `Logged ${logged} · ${attended}`
}

/** "2 days don't match attendance", or null for none. */
export function describeMismatchCount(count: number | null | undefined): string | null {
    if (!count) return null
    return count === 1 ? "1 day doesn't match attendance" : `${count} days don't match attendance`
}
```

- [ ] **Step 4: Extend the types**

In `client/src/lib/types/attendance.ts`:

Replace the `AttendanceHistoryStatus` line with:

```ts
export type AttendanceHistoryStatus = 'complete' | 'in-progress' | 'late' | 'absent' | 'short' | 'leave' | 'off'
```

Add to `AttendanceHistoryDay` after `breakVarianceMinutes?`:

```ts
    /**
     * Minutes under the day's target beyond the grace, decided by the server
     * (DailyHoursRule): working hours net of the break, halved for a half day of
     * approved leave. Null when there is nothing to say; optional for an older API.
     */
    shortByMinutes?: number | null
    /** Approved full-day leave. */
    onLeave?: boolean
    /** The minutes the day asked for; null on leave, a day off, or with no schedule. */
    targetMinutes?: number | null
```

Replace `WeekDayHours` with:

```ts
export interface WeekDayHours {
    date: string
    workedMinutes: number | null
    note: string | null
    /** See AttendanceHistoryDay.shortByMinutes. */
    shortByMinutes?: number | null
    /** See AttendanceHistoryDay.onLeave. */
    onLeave?: boolean
}
```

Replace `TeamAttendance` with:

```ts
export interface ShortDay {
    employeeId: string
    employeeName: string
    departmentName: string
    workedMinutes: number
    shortByMinutes: number
}

export interface TeamAttendance {
    members: TeamMemberAttendance[]
    week: TeamWeekRow[]
    /** The previous working day `shortDays` judges ("yyyy-MM-dd"). */
    shortDaysDate?: string | null
    shortDays?: ShortDay[]
}
```

Check that `client/src/lib/types/index.ts` re-exports everything from `./attendance` (e.g. `export * from './attendance'`). If it lists names instead, add `ShortDay`.

In `client/src/lib/types/timesheet.ts`, after `awaitingManager?: boolean;` add:

```ts
  /**
   * Minutes attendance recorded per weekday, parallel to dailyHours. Absent or
   * null for an older API and for a sheet the server does not compare (older than
   * its comparison window).
   */
  attendanceMinutes?: number[] | null;
  /** Logged minus attended per weekday beyond the grace; null where the day agrees. */
  dayMismatchMinutes?: (number | null)[] | null;
  /** Approved full-day leave per weekday. */
  onLeaveDays?: boolean[] | null;
  /** How many weekdays disagree with attendance. */
  mismatchDayCount?: number;
```

- [ ] **Step 5: Run the test and the type check**

Run: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/lib/daily-hours.test.ts`
Expected: PASS.
Run: `cd /c/Practice/Own/2026/WorkTrack/client && npx tsc -b --noEmit` (or `npm run build` if `tsc -b --noEmit` is rejected by the composite config).
Expected: no errors. Adding union members may surface an exhaustive `switch` elsewhere; if so, handle the new members there by treating `short` like `late`, `leave` like `absent` and `off` like `absent`.

- [ ] **Step 6: Commit**

```bash
git add client/src/lib/daily-hours.ts client/src/lib/daily-hours.test.ts client/src/lib/types/attendance.ts client/src/lib/types/timesheet.ts
git commit -m "Word short days and timesheet mismatches on the client

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: My Attendance — history grades and the bar target

**Files:**
- Modify: `client/src/components/attendance/AttendancePage.tsx` (This Week's Hours card ~L322-357; history status cell ~L422-428)
- Test: `client/src/components/attendance/AttendancePage.test.tsx` (append)

**Interfaces:**
- Consumes: `describeShortDay` (Task 8); `AttendanceHistoryDay.shortByMinutes`, `.targetMinutes`, `status` values `short | leave | off`.

- [ ] **Step 1: Write the failing tests**

Append to `client/src/components/attendance/AttendancePage.test.tsx` (add `AttendanceHistoryDay` to the type import at the top):

```tsx
function historyDay(overrides: Partial<AttendanceHistoryDay>): AttendanceHistoryDay {
    return {
        date: '2026-09-22', status: 'complete', checkInAt: '2026-09-22T08:00:00Z', checkOutAt: '2026-09-22T17:00:00Z',
        totalBreakMinutes: 60, workedMinutes: 480, breakVarianceMinutes: null,
        ...overrides,
    }
}

describe('My Attendance flags a short day and reads leave as leave', () => {
    it('grades a short day with how far short it was', async () => {
        api.getAttendanceHistory.mockResolvedValue([
            historyDay({ status: 'short', workedMinutes: 390, shortByMinutes: 90, targetMinutes: 480 }),
        ])
        await renderPage(SETTINGS)

        expect(await screen.findByText('1h 30m short')).toBeInTheDocument()
    })

    it('reads a leave day as leave and a weekend as a day off', async () => {
        api.getAttendanceHistory.mockResolvedValue([
            historyDay({ date: '2026-09-19', status: 'off', checkInAt: null, checkOutAt: null, workedMinutes: 0, totalBreakMinutes: 0 }),
            historyDay({ date: '2026-09-21', status: 'leave', onLeave: true, checkInAt: null, checkOutAt: null, workedMinutes: 0, totalBreakMinutes: 0 }),
        ])
        await renderPage(SETTINGS)

        expect(await screen.findByText('On leave')).toBeInTheDocument()
        expect(screen.getByText('Day off')).toBeInTheDocument()
        expect(screen.queryByText('No record')).not.toBeInTheDocument()
    })

    it('reads an older API with no short-day fields as it always did', async () => {
        api.getAttendanceHistory.mockResolvedValue([historyDay({ status: 'complete' })])
        await renderPage(SETTINGS)

        expect(await screen.findByText('Complete')).toBeInTheDocument()
        expect(screen.queryByText(/short$/)).not.toBeInTheDocument()
    })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/attendance/AttendancePage.test.tsx`
Expected: the first two new tests FAIL ("1h 30m short", "On leave" not found).

- [ ] **Step 3: Implement**

In `client/src/components/attendance/AttendancePage.tsx`:

(a) Add the import: `import { describeShortDay } from '../../lib/daily-hours'`.

(b) In the This Week's Hours map, replace:

```tsx
                            const pct = Math.min(100, (day.workedMinutes / (8 * 60)) * 100)
                            const barColor = day.workedMinutes === 0
                                ? 'divider'
                                : isInProgress ? BLUE : GREEN
```

with:

```tsx
                            // The day's own target from the server (net of the break,
                            // halved for a half day of leave); 8h for an older API.
                            const target = day.targetMinutes || 8 * 60
                            const pct = Math.min(100, (day.workedMinutes / target) * 100)
                            const barColor = day.workedMinutes === 0
                                ? 'divider'
                                : isInProgress ? BLUE : day.shortByMinutes != null ? AMBER : GREEN
```

(c) Replace the status cell's nested ternary with:

```tsx
                                        <TableCell sx={TD}>
                                            {d.status === 'in-progress'
                                                ? <StatusPill kind="in">In progress</StatusPill>
                                                : d.status === 'complete'
                                                    ? <StatusPill kind="in">Complete</StatusPill>
                                                    : d.status === 'late'
                                                        ? <StatusPill kind="late">Late arrival</StatusPill>
                                                        : d.status === 'short'
                                                            ? <StatusPill kind="break">{describeShortDay(d.shortByMinutes) ?? 'Short day'}</StatusPill>
                                                            : d.status === 'leave'
                                                                ? <StatusPill kind="leave">On leave</StatusPill>
                                                                : d.status === 'off'
                                                                    ? <StatusPill kind="out">Day off</StatusPill>
                                                                    : <StatusPill kind="out">
                                                                        {describeShortDay(d.shortByMinutes) ? `No record · ${describeShortDay(d.shortByMinutes)}` : 'No record'}
                                                                    </StatusPill>}
                                        </TableCell>
```

The `break` pill kind is the amber style: short is a warning, not an error.

- [ ] **Step 4: Run the tests**

Run: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/attendance/AttendancePage.test.tsx`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add client/src/components/attendance/AttendancePage.tsx client/src/components/attendance/AttendancePage.test.tsx
git commit -m "Show short days, leave and days off on My Attendance

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Team Attendance grid, short-days note, manager dashboard

**Files:**
- Create: `client/src/components/attendance/ShortDaysNote.tsx`
- Modify: `client/src/components/attendance/TeamAttendancePage.tsx` (week grid cell ~L267-277; note above the grid)
- Modify: `client/src/components/annual-leave/DashboardHome.tsx` (`TeamStatusNowCard` ~L2033-2066)
- Test: `client/src/components/attendance/TeamAttendancePage.test.tsx` (append)

**Interfaces:**
- Consumes: `describeShortDay` (Task 8); `TeamAttendance.shortDays`, `.shortDaysDate`; `WeekDayHours.shortByMinutes`, `.onLeave`.
- Produces: `ShortDaysNote({ date, people }: { date?: string | null; people?: ShortDay[] })` (default export). It renders nothing when `people` is empty or missing.

- [ ] **Step 1: Write the failing tests**

Append to `client/src/components/attendance/TeamAttendancePage.test.tsx` (add `TeamWeekRow` to the type import):

```tsx
async function renderWithWeek(week: TeamWeekRow[], extra: Partial<TeamAttendance> = {}) {
    api.getTeamAttendance.mockResolvedValue({ members: [member({ employeeName: 'Someone' })], week, ...extra })
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={queryClient}><TeamAttendancePage /></QueryClientProvider>)
    await screen.findByText('Weekly Attendance Log')
}

const days = (overrides: Partial<TeamWeekRow['days'][number]>[]): TeamWeekRow['days'] =>
    overrides.map((o, i) => ({ date: `2026-09-2${1 + i}`, workedMinutes: 480, note: null, ...o }))

describe('Team board marks short days and leave on the week grid', () => {
    it('says how far short a day was, and reads a missing day as no attendance', async () => {
        await renderWithWeek([{
            employeeId: 'p-sam', employeeName: 'Sam Short', totalMinutes: 870,
            days: days([{}, { workedMinutes: 390, shortByMinutes: 90 }, { workedMinutes: null, shortByMinutes: 480 }, { workedMinutes: null }, { workedMinutes: null }]),
        }])

        expect(screen.getByText('1h 30m short')).toBeInTheDocument()
        expect(screen.getByText('No attendance')).toBeInTheDocument()
    })

    it('reads a leave day as leave instead of a dash', async () => {
        await renderWithWeek([{
            employeeId: 'p-lea', employeeName: 'Lea Leave', totalMinutes: 0,
            days: days([{ workedMinutes: null, onLeave: true }, { workedMinutes: null }, { workedMinutes: null }, { workedMinutes: null }, { workedMinutes: null }]),
        }])

        expect(screen.getByText('Leave')).toBeInTheDocument()
    })

    it("names the previous working day's short days above the grid", async () => {
        await renderWithWeek(
            [{ employeeId: 'p-sam', employeeName: 'Sam Short', totalMinutes: 0, days: days([{}, {}, {}, {}, {}]) }],
            {
                shortDaysDate: '2026-09-22',
                shortDays: [{ employeeId: 'p-sam', employeeName: 'Sam Short', departmentName: 'Engineering', workedMinutes: 390, shortByMinutes: 90 }],
            },
        )

        expect(screen.getByText(/Short days on Tue 22 Sep/)).toBeInTheDocument()
        expect(screen.getByText(/Sam Short · 1h 30m short/)).toBeInTheDocument()
    })

    it('says nothing when nobody was short', async () => {
        await renderWithWeek([{ employeeId: 'p-sam', employeeName: 'Sam Short', totalMinutes: 0, days: days([{}, {}, {}, {}, {}]) }])

        expect(screen.queryByText(/Short days on/)).not.toBeInTheDocument()
    })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/attendance/TeamAttendancePage.test.tsx`
Expected: the four new tests FAIL.

- [ ] **Step 3: Write `ShortDaysNote`**

Create `client/src/components/attendance/ShortDaysNote.tsx`:

```tsx
import Box from '@mui/material/Box'
import { describeShortDay } from '../../lib/daily-hours'
import type { ShortDay } from '../../lib/types'

/*
 * Who came in under the day's target on the previous working day, as the server
 * judged it (DailyHoursRule, ShortDayDigest). Shared by Team Attendance and the
 * manager's dashboard; renders nothing when nobody was short or the API predates
 * the field.
 */
export default function ShortDaysNote({ date, people }: { date?: string | null; people?: ShortDay[] }) {
    if (!date || !people || people.length === 0) return null

    const label = new Date(`${date}T00:00:00Z`).toLocaleDateString('en-GB', {
        weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC',
    }).replace(',', '')

    return (
        <Box sx={{
            bgcolor: 'warning.light', color: 'warning.dark',
            border: '1px solid', borderColor: 'warning.main',
            borderRadius: '8px', p: '8px 12px', fontSize: 12, lineHeight: 1.5,
        }}>
            <Box component="strong">Short days on {label}:</Box>{' '}
            {people.map((p) => `${p.employeeName} · ${describeShortDay(p.shortByMinutes)}`).join(' — ')}
        </Box>
    )
}
```

`toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })` gives "Tue 22 Sep". The `.replace(',', '')` guards against the engines that insert a comma.

- [ ] **Step 4: Use it on Team Attendance and change the grid cell**

In `client/src/components/attendance/TeamAttendancePage.tsx`:

(a) Add imports:

```tsx
import ShortDaysNote from './ShortDaysNote'
import { describeShortDay } from '../../lib/daily-hours'
```

(b) Find where the component reads the query result (e.g. `const week = data?.week ?? []`). Right before the `{week.length > 0 && (` block, add:

```tsx
            <ShortDaysNote date={data?.shortDaysDate} people={data?.shortDays} />
```

Use whatever variable already holds the query result. If it is destructured as `{ members, week }`, read `shortDaysDate`/`shortDays` from the same object.

(c) Replace the grid cell body (the `{d.workedMinutes == null ? ... : (...)}` expression inside `<TableCell key={idx} sx={TD}>`) with:

```tsx
                                                {d.onLeave
                                                    ? <Box component="span" sx={{ color: 'info.dark' }}>Leave</Box>
                                                    : d.workedMinutes == null
                                                        ? (d.shortByMinutes != null
                                                            ? <Box component="span" sx={{ color: AMBER }}>No attendance</Box>
                                                            : <Box component="span" sx={{ color: 'text.disabled' }}>—</Box>)
                                                        : (
                                                            <>
                                                                {formatElapsed(d.workedMinutes)}
                                                                {d.note === 'in' && <Box component="span" sx={{ color: 'text.secondary', fontSize: 11, ml: 0.5 }}>(in)</Box>}
                                                                {d.note === 'break' && <Box component="span" sx={{ color: AMBER, fontSize: 11, ml: 0.5 }}>(break)</Box>}
                                                                {describeShortDay(d.shortByMinutes) && (
                                                                    <Box component="span" sx={{ display: 'block', color: AMBER, fontSize: 11 }}>
                                                                        {describeShortDay(d.shortByMinutes)}
                                                                    </Box>
                                                                )}
                                                            </>
                                                        )}
```

(d) Change the grid subtitle "Time clocked in — separate from the hours your team logs on timesheets" to "Time clocked in against the working day, net of the break — separate from the hours your team logs on timesheets".

- [ ] **Step 5: Show it on the manager dashboard**

In `client/src/components/annual-leave/DashboardHome.tsx`, add `import ShortDaysNote from '../attendance/ShortDaysNote'`. In `TeamStatusNowCard`, directly after the tiles grid `</Box>` (the one mapping `TeamMemberTile`) and before `</ActionCard>`, add:

```tsx
            <Box sx={{ mt: '12px' }}>
                <ShortDaysNote date={team.shortDaysDate} people={team.shortDays} />
            </Box>
```

Wrap it so an empty note leaves no gap: `{(team.shortDays?.length ?? 0) > 0 && (...)}`.

- [ ] **Step 6: Run the tests**

Run: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/attendance`
Then: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/annual-leave`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add client/src/components/attendance/ShortDaysNote.tsx client/src/components/attendance/TeamAttendancePage.tsx client/src/components/attendance/TeamAttendancePage.test.tsx client/src/components/annual-leave/DashboardHome.tsx
git commit -m "Mark short days and leave on the team week grid, and name yesterday's short days for the manager

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Timesheets — per-day mismatch and row chips

**Files:**
- Create: `client/src/components/timesheet/MismatchChip.tsx`
- Modify: `client/src/components/timesheet/TimesheetDailyBreakdown.tsx`
- Modify: `client/src/components/timesheet/AllTimesheetsPage.tsx` (row name block ~L250-262)
- Modify: `client/src/components/timesheet/TeamTimesheetPage.tsx` (employee cell ~L386-389)
- Modify: `client/src/components/timesheet/MyTimesheetPage.tsx` (`TimesheetCard` header ~L756-764 and day boxes ~L771-789)
- Test: `client/src/components/timesheet/AllTimesheetsPage.test.tsx` (append)

**Interfaces:**
- Consumes: `describeMismatch`, `describeMismatchCount` (Task 8); `Timesheet.dayMismatchMinutes`, `.attendanceMinutes`, `.onLeaveDays`, `.mismatchDayCount`.
- Produces: `MismatchChip({ count }: { count?: number })` (default export).

- [ ] **Step 1: Write the failing tests**

Append to `client/src/components/timesheet/AllTimesheetsPage.test.tsx`:

```tsx
describe('each timesheet day is checked against attendance', () => {
    const mismatched = sheet({
        id: 'ts-mm',
        employeeName: 'Mia Mismatch',
        status: 'Submitted',
        periodStart: '2026-09-21T00:00:00',
        periodEnd: '2026-09-27T00:00:00',
        dailyHours: [8, 8, 0, 8, 0],
        attendanceMinutes: [480, 390, 0, 0, 0],
        dayMismatchMinutes: [null, 90, null, 480, null],
        onLeaveDays: [false, false, true, false, false],
        mismatchDayCount: 2,
        entries: [
            { id: 'e1', timesheetId: 'ts-mm', projectId: 6, date: '2026-09-21T00:00:00', hoursWorked: 8 },
            { id: 'e2', timesheetId: 'ts-mm', projectId: 6, date: '2026-09-22T00:00:00', hoursWorked: 8 },
            { id: 'e4', timesheetId: 'ts-mm', projectId: 6, date: '2026-09-24T00:00:00', hoursWorked: 8 },
        ] as TimesheetEntry[],
    })

    it('puts a chip on the row', async () => {
        await renderPage(systemAdministrator, [mismatched])

        expect(screen.getByText("2 days don't match attendance")).toBeInTheDocument()
    })

    it('says on each day card what was logged against what was attended', async () => {
        await renderPage(systemAdministrator, [mismatched])
        fireEvent.click(screen.getByText('Mia Mismatch'))
        await screen.findByText('Daily breakdown')

        expect(dayCard('2026-09-22').getByText('Logged 8h · attended 6h 30m')).toBeInTheDocument()
        expect(dayCard('2026-09-24').getByText('Logged 8h · no attendance')).toBeInTheDocument()
        expect(dayCard('2026-09-23').getByText('On leave')).toBeInTheDocument()
        expect(dayCard('2026-09-21').queryByText(/^Logged/)).not.toBeInTheDocument()
    })

    it('shows nothing for a sheet from an older API', async () => {
        await renderPage(systemAdministrator, [approved])

        expect(screen.queryByText(/match attendance/)).not.toBeInTheDocument()
    })
})
```

If the entry shape in the existing `septemberWeek` fixture of this file has more required fields, copy its shape. The fields shown are the ones the breakdown reads.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/timesheet/AllTimesheetsPage.test.tsx`
Expected: the three new tests FAIL. The third may already pass; that's fine.

- [ ] **Step 3: Write `MismatchChip`**

Create `client/src/components/timesheet/MismatchChip.tsx`:

```tsx
import Box from '@mui/material/Box'
import { describeMismatchCount } from '../../lib/daily-hours'

/*
 * "2 days don't match attendance" — how many weekdays of a timesheet disagree with
 * the time attendance recorded, as the server judged them (DailyHoursRule). A flag
 * for the reviewer; nothing about submitting or approving depends on it.
 */
export default function MismatchChip({ count }: { count?: number }) {
    const text = describeMismatchCount(count)
    if (!text) return null
    return (
        <Box component="span" sx={{
            display: 'inline-block',
            bgcolor: 'warning.light', color: 'warning.dark',
            borderRadius: '4px', px: 0.75, py: '1px',
            fontSize: 11, fontWeight: 500,
        }}>
            {text}
        </Box>
    )
}
```

- [ ] **Step 4: Add the per-day line to `TimesheetDailyBreakdown`**

In `client/src/components/timesheet/TimesheetDailyBreakdown.tsx`:

(a) Add `import { describeMismatch } from '../../lib/daily-hours'`.

(b) Extend the `Day` type with `mismatch: string | null` and `onLeave: boolean`. In the `days` `useMemo` loop, before `out.push(...)`, add:

```tsx
            const onLeave = ts.onLeaveDays?.[i] ?? false
            const mismatch = describeMismatch(total, ts.attendanceMinutes?.[i], ts.dayMismatchMinutes?.[i])
```

Change the push to `out.push({ key, name, dateLabel, total, tasks, mismatch, onLeave })`, and add `ts.onLeaveDays, ts.attendanceMinutes, ts.dayMismatchMinutes` to the memo's dependency list.

(c) In the card, directly after the header `</Stack>` (the one holding the day name and total), add:

```tsx
                            {d.onLeave && (
                                <Box sx={{ fontSize: 10, color: 'info.dark', mb: 0.5 }}>On leave</Box>
                            )}
                            {d.mismatch && (
                                <Box sx={{ fontSize: 10, color: 'warning.dark', fontWeight: 600, mb: 0.5 }}>{d.mismatch}</Box>
                            )}
```

- [ ] **Step 5: Add the chip to the three lists**

`AllTimesheetsPage.tsx`: import `MismatchChip from './MismatchChip'`. In the row's name block, right after the department badge `</Box>` (the one rendering `{deptName}`), add:

```tsx
                        {(ts.mismatchDayCount ?? 0) > 0 && (
                            <Box sx={{ mt: '2px' }}><MismatchChip count={ts.mismatchDayCount} /></Box>
                        )}
```

`TeamTimesheetPage.tsx`: import `MismatchChip from './MismatchChip'`. In the employee cell, after `{noteFor(ts) && <ReviewNote note={noteFor(ts)!} compact />}`, add:

```tsx
                                                {(ts.mismatchDayCount ?? 0) > 0 && (
                                                    <Box sx={{ mt: 0.5 }}><MismatchChip count={ts.mismatchDayCount} /></Box>
                                                )}
```

(Add `import Box from '@mui/material/Box'` if it isn't already imported.)

`MyTimesheetPage.tsx`: import `MismatchChip from './MismatchChip'` and `{ describeMismatch } from '../../lib/daily-hours'`. In `TimesheetCard`, after `<StatusBadge status={status} />`, add `<MismatchChip count={t.mismatchDayCount} />`. In the day-box map, compute:

```tsx
                            const mismatch = describeMismatch(h, t.attendanceMinutes?.[i], t.dayMismatchMinutes?.[i])
```

then give the day box `title={mismatch ?? undefined}` and change its `bgcolor` to `mismatch ? 'warning.light' : filled ? softBg('success') : 'action.hover'`.

- [ ] **Step 6: Run the timesheet tests**

Run: `/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/timesheet`
Expected: all PASS, including `TeamTimesheetPage.test.tsx`.

- [ ] **Step 7: Commit**

```bash
git add client/src/components/timesheet/MismatchChip.tsx client/src/components/timesheet/TimesheetDailyBreakdown.tsx client/src/components/timesheet/AllTimesheetsPage.tsx client/src/components/timesheet/TeamTimesheetPage.tsx client/src/components/timesheet/MyTimesheetPage.tsx client/src/components/timesheet/AllTimesheetsPage.test.tsx
git commit -m "Show where a timesheet disagrees with attendance, day by day

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Document it and verify the whole branch

**Files:**
- Modify: `CLAUDE.md` (Key Configuration, directly after the "Reminders follow the Working Week" bullet)

- [ ] **Step 1: Add the CLAUDE.md paragraph**

Insert as a new bullet under **Key Configuration**, after the "Reminders follow the Working Week, in one place." bullet:

```markdown
- **Short days and timesheet ↔ attendance match, in one place.**
  `Application/Attendance/Support/DailyHoursRule.cs` is the rule and
  `DailyHoursContext` loads its inputs for a range (settings, the holiday
  country's holidays, **Approved** leave matched on `EmployeeProfileId`). A
  working day's target is `WorkingDaySchedule.ScheduledMinutes` — the working
  hours net of the break — halved for a half day of leave (two halves are a
  whole); full leave, a non-working day or no schedule has none. A day is
  **short** once it is over (a past date, or today checked out) and more than
  `GraceMinutes` (15) under target; no attendance on a past working day is
  short by the whole target. A timesheet day is a **mismatch** when logged and
  attended differ by more than the same 15, never on a leave day, never while
  open, and weekends are compared. Consumers: My Attendance's `short`/`leave`/`off`
  grades (`GetMyAttendanceHistory`), the team week grid and the previous working
  day's digest (`GetTeamAttendance`, `ShortDayDigest`, shown by `ShortDaysNote` on
  Team Attendance and the manager dashboard), a "N short days on …" issue on the
  company dashboard, the daily report's "Short day" section, and
  `TimesheetDto.DayMismatchMinutes`/`MismatchDayCount`
  (`TimesheetAttendanceComparison`, last 12 weeks only) under
  `TimesheetDailyBreakdown` and as `MismatchChip` on the three timesheet lists.
  Flag only — nothing blocks submit or approve. The company overtime issue is
  judged against the same schedule plus the grace, leave excluded (it was a flat
  10 hours). `client/src/lib/daily-hours.ts` only words the server's figures.
  A past day left checked in reads long, not short — the calculator runs it to
  now, as on every other attendance screen. `DailyHoursRuleTests`,
  `DailyHoursContextTests`, `ShortDayAttendanceTests`,
  `TimesheetAttendanceMismatchTests` and `DailyAttendanceReportTests` pin the
  server.
```

- [ ] **Step 2: Run the full server suite**

Run: `DOTNET_EnableWriteXorExecute=0 dotnet test Tests/WorkTrack.Tests/WorkTrack.Tests.csproj --nologo --artifacts-path C:/temp/wt-tdd`
Expected: all PASS (~730 tests). Report any failure with its output; don't mark it skipped.

- [ ] **Step 3: Run the client suites in batches, plus lint and build**

```bash
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/lib
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/attendance
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/timesheet
/c/Practice/Own/2026/WorkTrack/client/node_modules/.bin/vitest run --root /c/Practice/Own/2026/WorkTrack/client src/components/annual-leave
cd /c/Practice/Own/2026/WorkTrack/client && npm run lint && npm run build
```

Expected: all PASS; lint clean; build succeeds. The full vitest run can run out of memory and still exit 0, which is why these run in batches. Read each batch's summary line, not just the exit code.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "Document where the short-day and timesheet-match rule lives

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Remind the user to restart the API**

Vite hot-reloads the UI, but the running API still serves the old DTOs until it is restarted. Until then the new fields read as missing and nothing is flagged. Say so in the hand-off.
