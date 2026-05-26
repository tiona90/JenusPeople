using System;
using System.Collections.Generic;

namespace Domain;

public enum ProjectStatus
{
    Active = 0,
    OnHold = 1,
    Inactive = 2
}

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public string? OwnerId { get; set; }
    public User? Owner { get; set; }
    public string ColorKey { get; set; } = "p1";
    public int TargetWeeklyHours { get; set; }
    public int TargetMonthlyHours { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public ICollection<TimesheetEntry> TimesheetEntries { get; set; } = new List<TimesheetEntry>();
}
