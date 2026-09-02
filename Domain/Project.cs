using System;
using System.Collections.Generic;
using Domain.Interfaces;

namespace Domain;

public enum ProjectStatus
{
    Active = 0,
    OnHold = 1,
    Inactive = 2
}

public class Project : ISoftDeletable, IAuditable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
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
    public ICollection<ProjectActivityAssignment> ActivityAssignments { get; set; } = new List<ProjectActivityAssignment>();

    /// <summary>
    /// The components this project is made up of, narrowed from the org-wide
    /// catalogue. Empty means the project has declared none.
    /// </summary>
    public ICollection<ProjectComponentAssignment> ComponentAssignments { get; set; } = new List<ProjectComponentAssignment>();

    /// <summary>
    /// The departments this project belongs to, which is also who can see it.
    /// Never legitimately empty — see <see cref="ProjectDepartment"/>.
    /// </summary>
    public ICollection<ProjectDepartment> DepartmentAssignments { get; set; } = new List<ProjectDepartment>();
}
