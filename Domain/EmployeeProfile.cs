using Domain.Interfaces;

namespace Domain;

public class EmployeeProfile : ISoftDeletable, IAuditable
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

    /// <summary>
    /// Null for an Admin, who sits outside the department structure: the role sees
    /// every department, so belonging to one grants nothing. Left non-null it was
    /// an invented assignment, and it counted — the admin appeared in that
    /// department's headcount and attendance warnings, and blocked its deletion.
    /// </summary>
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string? ManagerId { get; set; }
    public EmployeeProfile? Manager { get; set; }
    public ICollection<EmployeeProfile> DirectReports { get; set; } = new List<EmployeeProfile>();
    public ICollection<AnnualLeave> AnnualLeaves { get; set; } = new List<AnnualLeave>();
    public ICollection<Timesheet> Timesheets { get; set; } = new List<Timesheet>();

    public int AnnualLeaveEntitlement { get; set; }
    public int LeaveBalance { get; set; }

    public string? JobTitle { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
