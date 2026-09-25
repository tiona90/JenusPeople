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

public record DayHistoryDto(
    string Date,
    string Status,
    DateTime? CheckInAt,
    DateTime? CheckOutAt,
    int TotalBreakMinutes,
    int WorkedMinutes,
    int? BreakVarianceMinutes = null);

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

public record WeekDayHoursDto(string Date, int? WorkedMinutes, string? Note);

public record TeamWeekRowDto(
    string EmployeeId,
    string EmployeeName,
    List<WeekDayHoursDto> Days,
    int TotalMinutes);

public record TeamAttendanceDto(
    List<TeamMemberAttendanceDto> Members,
    List<TeamWeekRowDto> Week);

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

public record DeptAttendanceDto(
    string Name,
    int Total,
    int In,
    int Break,
    int Out,
    int Leave,
    int TotalMinutes,
    int AvgMinutes);

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
