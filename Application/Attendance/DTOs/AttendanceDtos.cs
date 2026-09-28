namespace Application.Attendance.DTOs;

public record AttendanceEventDto(string Id, DateTime At, string Type);

// BreakVarianceMinutes, wherever it appears below, is the break taken against
// the break the Organization settings allow for, read through
// WorkingDaySchedule.BreakVariance: positive minutes over, negative minutes
// under, 0 exactly on it, and null when there is nothing to say — no break
// configured, or a day still open that has not gone over yet. A client built
// before the field reads a missing one as null, i.e. nothing to say.

public record TodayStateDto(
    string Date,
    string Status,
    DateTime? CheckInAt,
    DateTime? CheckOutAt,
    DateTime? OnBreakSince,
    int TotalBreakMinutes,
    int WorkedMinutes,
    List<AttendanceEventDto> Events,
    bool IsAutoBreak,
    int? BreakVarianceMinutes = null);

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

// BreakMinutes is the break taken so far today, a running break included
// (WorkingDaySchedule.BreakMinutesTaken), so the board can quote it beside
// the variance.
public record TeamMemberAttendanceDto(
    string EmployeeId,
    string EmployeeName,
    string DepartmentName,
    string? JobTitle,
    string Status,
    DateTime? CheckInAt,
    int WorkedMinutes,
    DateTime? OnBreakSince,
    string TodayNote,
    bool IsAutoBreak,
    int BreakMinutes = 0,
    int? BreakVarianceMinutes = null);

public record WeekDayHoursDto(
    string Date,
    int? WorkedMinutes,
    string? Note,
    int? ShortByMinutes = null,
    bool OnLeave = false);

public record TeamWeekRowDto(
    string EmployeeId,
    string EmployeeName,
    List<WeekDayHoursDto> Days,
    int TotalMinutes);

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

// History endpoint: per-day earliest-check-in time per team member over the
// last N days. The check-in is expressed as minutes-from-midnight in the org's
// time zone (WorkingDaySchedule) so the frontend can plot it on a numeric
// y-axis without timezone math, and "09:00" on that axis is the settings' 09:00.
public record MemberCheckInDayDto(
    string Date,
    int? CheckInMinutesFromMidnight);

public record TeamMemberHistoryDto(
    string EmployeeId,
    string EmployeeName,
    List<MemberCheckInDayDto> Days);

public record TeamHistoryDto(List<TeamMemberHistoryDto> Members);

/// <param name="Members">
/// The people behind the counts, so Company Attendance can open a department row
/// into who is in, on a break, done, not in or on leave. Defaulted so positional
/// callers keep compiling; the handler always fills it.
/// </param>
public record DeptAttendanceDto(
    string Name,
    int Total,
    int In,
    int Break,
    int Out,
    int Leave,
    int TotalMinutes,
    int AvgMinutes,
    List<DeptMemberAttendanceDto>? Members = null);

/// <summary>
/// One person's day on Company Attendance's department breakdown. Status is
/// <c>in</c>, <c>break</c>, <c>done</c> (checked out), <c>not-in</c> (no check-in
/// yet) or <c>leave</c> — the department row's Off column is <c>done</c> plus
/// <c>not-in</c>, split here because "went home" and "never came" are different
/// news. Leave outranks attendance, as it does in the counts. LateMinutes is
/// minutes past the configured start (WorkingDaySchedule.MinutesLate), null when
/// on time or not checked in. BreakMinutes counts a running break, and
/// BreakVarianceMinutes reads as everywhere else in this file.
/// </summary>
public record DeptMemberAttendanceDto(
    string EmployeeId,
    string EmployeeName,
    string? JobTitle,
    string Status,
    DateTime? CheckInAt,
    DateTime? CheckOutAt,
    DateTime? OnBreakSince,
    bool IsAutoBreak,
    int WorkedMinutes,
    int BreakMinutes,
    int? BreakVarianceMinutes,
    int? LateMinutes);

/// <param name="BreakVarianceMinutes">
/// On a break's end only: how many minutes over the configured allowance the
/// day's break stood at that moment (<c>WorkingDaySchedule.BreakVariance</c>, as
/// of the event, not as of now). Null on every other row, when no break is
/// configured, and when the break was within the allowance — under is not news
/// on a feed, as it is not on the issues card. Defaulted so positional callers
/// keep compiling.
/// </param>
public record RecentActivityDto(
    string EmployeeName,
    string DepartmentName,
    string Action,
    DateTime? At,
    int? MinutesAgo,
    int? BreakVarianceMinutes = null);

public record IssueDto(
    string Severity,
    string Title,
    string Detail);

// Per-user presence for today, keyed by the Identity user id. Presence is
// derived from today's attendance events only: a user is online once they have
// checked in and stays online until they check out. Never checked in, or
// already checked out, is offline.
public record UserPresenceDto(
    string UserId,
    string Status,
    DateTime? CheckInAt,
    DateTime? LastActivityAt,
    bool IsAutoBreak);

public record CompanyAttendanceDto(
    int Total,
    int In,
    int Break,
    int Out,
    int Leave,
    int TotalMinutesToday,
    int AvgMinutesToday,
    List<DeptAttendanceDto> Departments,
    List<RecentActivityDto> Recent,
    List<IssueDto> Issues);
