using System;
using System.Collections.Generic;

namespace Application.Timesheets.DTOs
{
    public class TimesheetProjectSummaryDto
    {
        public int ProjectId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Hours { get; set; }
    }

    public class TimesheetDto
    {
        public string Id { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal TotalHours { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<TimesheetProjectSummaryDto> ProjectSummaries { get; set; } = new();

        /// <summary>Hours per weekday — index 0 = Monday … 4 = Friday.</summary>
        public List<decimal> DailyHours { get; set; } = new() { 0, 0, 0, 0, 0 };

        /// <summary>
        /// A submitted timesheet a manager is available to review today, so it is the
        /// manager's, not HR's (<c>TimesheetReviewRule</c>). False once decided, and
        /// false when nobody but HR can review it — the submitter is the only manager,
        /// the department has none, or every manager is on leave.
        /// </summary>
        public bool AwaitingManager { get; set; }

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
    }
}
