namespace Application.Attendance.DTOs;

public record AttendanceEventDto(string Id, DateTime At, string Type);

public record TodayStateDto(
    string Date,
    string Status,
    DateTime? CheckInAt,
    DateTime? CheckOutAt,
    DateTime? OnBreakSince,
    int TotalBreakMinutes,
    int WorkedMinutes,
    List<AttendanceEventDto> Events);

public record DayHistoryDto(
    string Date,
    string Status,
    DateTime? CheckInAt,
    DateTime? CheckOutAt,
    int TotalBreakMinutes,
    int WorkedMinutes);

public record TeamMemberAttendanceDto(
    string EmployeeId,
    string EmployeeName,
    string DepartmentName,
    string? JobTitle,
    string Status,
    DateTime? CheckInAt,
    int WorkedMinutes,
    DateTime? OnBreakSince,
    string TodayNote);

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
// last N days. The check-in is expressed as minutes-from-midnight (UTC) so the
// frontend can plot it on a numeric y-axis without timezone math.
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

public record RecentActivityDto(
    string EmployeeName,
    string DepartmentName,
    string Action,
    DateTime? At,
    int? MinutesAgo);

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
    DateTime? LastActivityAt);

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
