using Application.Attendance.Commands;
using Application.Attendance.DTOs;
using Application.Attendance.Queries;
using Application.Attendance.Support;
using Application.Core;
using Domain;
using Domain.Services;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The check-in / break / check-out state machine, and the wire vocabulary the
/// SPA depends on.
///
/// Both were unreachable from a test while they lived inside AttendanceController:
/// the guards sat in HTTP actions, and the status strings were produced inline. The
/// day rules moved to <see cref="AttendanceDayStateCalculator"/> as a typed enum,
/// so the string mapping is now a single translation at the edge — and one worth
/// pinning, because the client types these as a closed union
/// ('out' | 'in' | 'break' | 'done') and a renamed case would break it silently
/// rather than fail to compile.
/// </summary>
public class AttendanceActionsTests
{
    private const string UserId = "u1";
    private const string ProfileId = "p1";
    private const string NoProfileUserId = "nobody";

    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Users.Add(new User { Id = UserId, UserName = "worker", Email = "worker@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = ProfileId, UserId = UserId, DepartmentId = 1 });

        db.Users.Add(new User { Id = NoProfileUserId, UserName = "nobody", Email = "nobody@test.local" });

        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static Task<Result<TodayStateDto>> CheckInFor(AppDbContext db, string userId = UserId) =>
        new CheckIn.Handler(db).Handle(new CheckIn.Command { RequestingUserId = userId }, CancellationToken.None);

    private static Task<Result<TodayStateDto>> CheckOutFor(AppDbContext db, string userId = UserId) =>
        new CheckOut.Handler(db).Handle(new CheckOut.Command { RequestingUserId = userId }, CancellationToken.None);

    private static Task<Result<TodayStateDto>> StartBreakFor(AppDbContext db, string userId = UserId, bool isAutomatic = false) =>
        new StartBreak.Handler(db).Handle(
            new StartBreak.Command { RequestingUserId = userId, IsAutomatic = isAutomatic }, CancellationToken.None);

    private static Task<Result<TodayStateDto>> EndBreakFor(AppDbContext db, string userId = UserId, bool isAutomatic = false) =>
        new EndBreak.Handler(db).Handle(
            new EndBreak.Command { RequestingUserId = userId, IsAutomatic = isAutomatic }, CancellationToken.None);

    private static Task<Result<TodayStateDto>> TodayFor(AppDbContext db, string userId = UserId) =>
        new GetTodayState.Handler(db).Handle(new GetTodayState.Query { RequestingUserId = userId }, CancellationToken.None);

    private static TodayStateDto Ok(Result<TodayStateDto> result)
    {
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    /* ── the wire contract ───────────────────────────────────────────────── */

    /// <summary>
    /// AttendanceStatus in client/src/lib/types/attendance.ts. Every status the
    /// calculator can produce has to map onto one of these four spellings.
    /// </summary>
    [Theory]
    [InlineData(AttendanceDayStatus.Out, "out")]
    [InlineData(AttendanceDayStatus.In, "in")]
    [InlineData(AttendanceDayStatus.Break, "break")]
    [InlineData(AttendanceDayStatus.Done, "done")]
    public void Day_statuses_keep_their_wire_spelling(AttendanceDayStatus status, string expected)
    {
        Assert.Equal(expected, AttendanceDay.WireStatus(status));
    }

    [Fact]
    public void Every_day_status_has_a_distinct_wire_spelling()
    {
        var spellings = Enum.GetValues<AttendanceDayStatus>()
            .Select(AttendanceDay.WireStatus)
            .ToList();

        Assert.Equal(spellings.Count, spellings.Distinct().Count());
    }

    /// <summary>AttendanceEventType in the same client type module.</summary>
    [Theory]
    [InlineData(AttendanceEventType.CheckIn, "check-in")]
    [InlineData(AttendanceEventType.CheckOut, "check-out")]
    [InlineData(AttendanceEventType.BreakStart, "break-start")]
    [InlineData(AttendanceEventType.BreakEnd, "break-end")]
    [InlineData(AttendanceEventType.AutoBreakStart, "auto-break-start")]
    [InlineData(AttendanceEventType.AutoBreakEnd, "auto-break-end")]
    public void Event_types_keep_their_wire_spelling(AttendanceEventType type, string expected)
    {
        Assert.Equal(expected, AttendanceDay.EventTypeName(type));
    }

    /* ── the happy path ─────────────────────────────────────────────────── */

    [Fact]
    public async Task A_full_day_moves_out_in_break_in_done()
    {
        using var db = SeedWorld();

        Assert.Equal("out", Ok(await TodayFor(db)).Status);
        Assert.Equal("in", Ok(await CheckInFor(db)).Status);
        Assert.Equal("break", Ok(await StartBreakFor(db)).Status);
        Assert.Equal("in", Ok(await EndBreakFor(db)).Status);
        Assert.Equal("done", Ok(await CheckOutFor(db)).Status);
    }

    [Fact]
    public async Task Checking_in_records_the_event_and_reports_it()
    {
        using var db = SeedWorld();

        var state = Ok(await CheckInFor(db));

        Assert.NotNull(state.CheckInAt);
        Assert.Null(state.CheckOutAt);
        var only = Assert.Single(state.Events);
        Assert.Equal("check-in", only.Type);
    }

    /// <summary>
    /// The date field is the UTC day the events were bucketed into, which is the
    /// same boundary every other attendance query uses.
    /// </summary>
    [Fact]
    public async Task Today_is_reported_as_the_utc_day()
    {
        using var db = SeedWorld();

        var state = Ok(await TodayFor(db));

        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), state.Date);
    }

    /* ── the guards ─────────────────────────────────────────────────────── */

    [Fact]
    public async Task Checking_in_twice_is_a_conflict()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));

        var again = await CheckInFor(db);

        Assert.False(again.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, again.ErrorKind);
        Assert.Equal("Already checked in.", again.Error);
    }

    /// <summary>Being on a break still counts as being checked in.</summary>
    [Fact]
    public async Task Checking_in_while_on_break_is_a_conflict()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));
        Ok(await StartBreakFor(db));

        var again = await CheckInFor(db);

        Assert.Equal(ResultErrorKind.Conflict, again.ErrorKind);
        Assert.Equal("Already checked in.", again.Error);
    }

    [Fact]
    public async Task Checking_out_without_checking_in_is_a_conflict()
    {
        using var db = SeedWorld();

        var result = await CheckOutFor(db);

        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal("Not currently checked in.", result.Error);
    }

    [Fact]
    public async Task Checking_out_twice_is_a_conflict()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));
        Ok(await CheckOutFor(db));

        var again = await CheckOutFor(db);

        Assert.Equal(ResultErrorKind.Conflict, again.ErrorKind);
    }

    [Fact]
    public async Task A_break_cannot_start_before_checking_in()
    {
        using var db = SeedWorld();

        var result = await StartBreakFor(db);

        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal("Can only start a break while working.", result.Error);
    }

    [Fact]
    public async Task A_second_break_cannot_start_while_one_is_running()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));
        Ok(await StartBreakFor(db));

        var result = await StartBreakFor(db);

        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
    }

    [Fact]
    public async Task A_break_cannot_end_when_none_is_running()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));

        var result = await EndBreakFor(db);

        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal("Not currently on break.", result.Error);
    }

    /// <summary>
    /// Checking out while on a break writes the break-end too, so the break is
    /// recorded as having finished rather than being inferred later from the
    /// check-out. The two share a timestamp, so the break contributes no time.
    /// </summary>
    [Fact]
    public async Task Checking_out_while_on_break_closes_the_break_explicitly()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));
        Ok(await StartBreakFor(db));

        var state = Ok(await CheckOutFor(db));

        Assert.Equal("done", state.Status);
        Assert.Null(state.OnBreakSince);
        Assert.Equal(
            ["check-in", "break-start", "break-end", "check-out"],
            state.Events.Select(e => e.Type));
    }

    /* ── auto (idle-detected) breaks ──────────────────────────────────────── */

    [Fact]
    public async Task An_automatic_break_start_succeeds_only_while_working()
    {
        using var db = SeedWorld();

        var beforeCheckIn = await StartBreakFor(db, isAutomatic: true);
        Assert.Equal(ResultErrorKind.Conflict, beforeCheckIn.ErrorKind);

        Ok(await CheckInFor(db));
        var state = Ok(await StartBreakFor(db, isAutomatic: true));

        Assert.Equal("break", state.Status);
        Assert.True(state.IsAutoBreak);
        Assert.Equal("auto-break-start", state.Events[^1].Type);
    }

    /// <summary>
    /// Idle detection reporting activity resumed must never silently end a break
    /// the user started by hand — only the human's own Resume click may do that.
    /// </summary>
    [Fact]
    public async Task An_automatic_resume_leaves_a_manually_started_break_running()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));
        Ok(await StartBreakFor(db));

        var state = Ok(await EndBreakFor(db, isAutomatic: true));

        Assert.Equal("break", state.Status);
        Assert.DoesNotContain(state.Events, e => e.Type is "break-end" or "auto-break-end");
    }

    [Fact]
    public async Task An_automatic_resume_ends_a_break_it_opened_itself()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));
        Ok(await StartBreakFor(db, isAutomatic: true));

        var state = Ok(await EndBreakFor(db, isAutomatic: true));

        Assert.Equal("in", state.Status);
        Assert.False(state.IsAutoBreak);
        Assert.Equal("auto-break-end", state.Events[^1].Type);
    }

    /// <summary>A human clicking Resume always ends whichever break is open, auto or manual.</summary>
    [Fact]
    public async Task A_manual_resume_ends_an_automatically_opened_break()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));
        Ok(await StartBreakFor(db, isAutomatic: true));

        var state = Ok(await EndBreakFor(db));

        Assert.Equal("in", state.Status);
        Assert.Equal("break-end", state.Events[^1].Type);
    }

    /* ── the precondition ───────────────────────────────────────────────── */

    /// <summary>
    /// Attendance hangs off an EmployeeProfile, so a user without one cannot take
    /// part. Reported as Invalid — a 400 carrying the reason — rather than as
    /// ValidationErrors, which the client treats as form feedback and for which it
    /// deliberately withholds the global error notification. Through
    /// ValidationFailure the user would see nothing at all.
    /// </summary>
    [Fact]
    public async Task A_user_with_no_employee_profile_is_refused_with_a_reason()
    {
        using var db = SeedWorld();

        var results = new[]
        {
            await TodayFor(db, NoProfileUserId),
            await CheckInFor(db, NoProfileUserId),
            await CheckOutFor(db, NoProfileUserId),
            await StartBreakFor(db, NoProfileUserId),
            await EndBreakFor(db, NoProfileUserId),
        };

        Assert.All(results, result =>
        {
            Assert.False(result.IsSuccess);
            Assert.Equal(ResultErrorKind.Invalid, result.ErrorKind);
            Assert.Equal("No employee profile found.", result.Error);
            Assert.Null(result.ValidationErrors);
        });
    }

    [Fact]
    public async Task A_refused_action_writes_nothing()
    {
        using var db = SeedWorld();

        await CheckOutFor(db);
        await StartBreakFor(db);
        await EndBreakFor(db);
        await CheckInFor(db, NoProfileUserId);

        Assert.Empty(db.AttendanceEvents);
    }

    /* ── history vocabulary ─────────────────────────────────────────────── */

    /// <summary>
    /// AttendanceHistoryStatus in the client types. A day with no events reads as
    /// absent, and an open day as in-progress.
    /// </summary>
    [Fact]
    public async Task History_reports_absent_days_and_the_day_in_progress()
    {
        using var db = SeedWorld();
        Ok(await CheckInFor(db));

        var result = await new GetMyAttendanceHistory.Handler(db).Handle(
            new GetMyAttendanceHistory.Query { RequestingUserId = UserId, Days = 3 },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var days = result.Value!;

        Assert.Equal(3, days.Count);
        Assert.Equal("in-progress", days[^1].Status);          // today, still open
        Assert.All(days.Take(2), d => Assert.Equal("absent", d.Status));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(GetMyAttendanceHistory.MaxDays + 1)]
    public async Task History_clamps_an_out_of_range_day_count_to_the_default(int requested)
    {
        using var db = SeedWorld();

        var result = await new GetMyAttendanceHistory.Handler(db).Handle(
            new GetMyAttendanceHistory.Query { RequestingUserId = UserId, Days = requested },
            CancellationToken.None);

        Assert.Equal(GetMyAttendanceHistory.DefaultDays, result.Value!.Count);
    }
}
