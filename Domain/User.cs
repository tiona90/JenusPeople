using Microsoft.AspNetCore.Identity;

namespace Domain;

public class User : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    // Date of birth (date only, no time). Used by the birthday reminder.
    // PhoneNumber is inherited from IdentityUser.
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>
    /// Recorded HR data, maintained by an administrator. Nothing consults it: it
    /// does not gate maternity or paternity leave, and the eligibility notes on
    /// those leave types stay decorative. Deliberately so — the per-child
    /// paternity entitlement is gender-neutral, and a rule here would narrow it.
    ///
    /// Nullable because every account predating the column has no value, and a
    /// default would assert a fact about a real person that nobody entered.
    /// <c>null</c> means "not specified", which an admin can also choose
    /// explicitly to clear a value set by mistake.
    /// </summary>
    public Gender? Gender { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this account may sign in. An administrator switches a leaver off
    /// here rather than deleting them: DeleteAdminUser has to null out every
    /// approval they ever gave, so deletion rewrites history, while an enabled
    /// account leaves working credentials behind.
    ///
    /// Enforced by <c>API.Security.ActiveUserSignInManager</c> — inside Identity,
    /// so no sign-in path can miss it. Deliberately distinct from
    /// <c>LockoutEnd</c>, which is the 15-minute brake on password guessing.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public ICollection<AnnualLeave> AnnualLeaves { get; set; } = new List<AnnualLeave>();
    public ICollection<AnnualLeave> ApprovedAnnualLeaves { get; set; } = new List<AnnualLeave>();
    public ICollection<LeaveStatusHistory> LeaveStatusChanges { get; set; } = new List<LeaveStatusHistory>();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<UserDepartment> UserDepartments { get; set; } = new List<UserDepartment>();
    public ICollection<UserDepartment> AssignedUserDepartments { get; set; } = new List<UserDepartment>();
    public EmployeeProfile? EmployeeProfile { get; set; }
    public ICollection<Timesheet> ApprovedTimesheets { get; set; } = new List<Timesheet>();
    public ICollection<TimesheetStatusHistory> ChangedTimesheetStatuses { get; set; } = new List<TimesheetStatusHistory>();
}

/// <summary>
/// Serialised as "Male" / "Female" rather than 1 / 2: <c>API/Program.cs</c>
/// registers <c>JsonStringEnumConverter</c>, so the client types this as a string
/// union the same way it types <c>AttachmentPolicy</c> and <c>EligibilityScope</c>.
/// </summary>
public enum Gender
{
    Male = 1,
    Female = 2,
}
