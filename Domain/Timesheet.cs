using System;
using System.Collections.Generic;
using Domain.Interfaces;

namespace Domain;

public enum TimesheetStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Resubmitted = 4
}

public class Timesheet : IAuditable
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EmployeeId { get; set; } = string.Empty;
    public EmployeeProfile? Employee { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal TotalHours { get; set; }
    public TimesheetStatus Status { get; set; }
    public string? ApproverId { get; set; }
    public User? Approver { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<TimesheetEntry> Entries { get; set; } = new List<TimesheetEntry>();
    public ICollection<TimesheetStatusHistory> StatusHistory { get; set; } = new List<TimesheetStatusHistory>();
}
