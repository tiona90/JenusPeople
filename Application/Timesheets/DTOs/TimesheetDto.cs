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
    }
}
