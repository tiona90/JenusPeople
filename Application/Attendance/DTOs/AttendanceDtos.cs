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
