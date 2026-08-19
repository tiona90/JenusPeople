using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    /// <summary>
    /// FK to <see cref="Domain.EmployeeProfile"/>.Id — <b>not</b> AspNetUsers.Id.
    /// The sibling <see cref="AnnualLeave.EmployeeId"/> is a user id, so the two
    /// must never be compared or interchanged.
    /// </summary>
    public string EmployeeProfileId { get; set; } = string.Empty;
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

    /// <summary>
    /// Optimistic-concurrency token. SQL Server stamps this on every update; a
    /// stale value on SaveChanges raises <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
