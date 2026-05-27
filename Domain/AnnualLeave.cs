using System;
using Domain.Interfaces;
using Domain.Services;

namespace Domain;

public enum AnnualLeaveStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled
}

public class AnnualLeave : IAuditable
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EmployeeId { get; set; } = Guid.NewGuid().ToString();
    public User? Employee { get; set; }
    public string? ApprovedById { get; set; }
    public User? ApprovedBy { get; set; }
    public string? EmployeeProfileId { get; set; }
    public EmployeeProfile? EmployeeProfile { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceUrl { get; set; }
    public AnnualLeaveStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Weekend-aware business-day count for this leave request, ignoring public
    /// holidays. Holiday-aware calculations require external input and should be
    /// done via <see cref="LeaveCalculationService.CalculateBusinessDays"/> directly.
    /// </summary>
    public int TotalDays => LeaveCalculationService.CalculateBusinessDays(StartDate, EndDate);
    public ICollection<LeaveStatusHistory> StatusHistory { get; set; } = new List<LeaveStatusHistory>();
}


