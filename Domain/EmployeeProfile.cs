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

    /// <summary>
    /// Whether this employee has declared having children — a genuine tri-state:
    /// null means they have never been asked, false that they declared none, true
    /// that they have some. Deriving it from <see cref="Children"/> cannot tell
    /// "hasn't told us" from "has none", which is exactly the distinction the leave
    /// request form needs in order to say something useful.
    ///
    /// Invariant, enforced in the handlers: it cannot be saved false while
    /// <see cref="Children"/> is non-empty, and adding a child sets it true. The
    /// flag and the list therefore cannot disagree.
    /// </summary>
    public bool? HasChildren { get; set; }

    public ICollection<Child> Children { get; set; } = new List<Child>();

    public string? JobTitle { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
