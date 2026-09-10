namespace Domain;

/// <summary>
/// One declared child of an employee. Children hang off the HR record rather than
/// the login because the entitlement they carry is an HR fact, and
/// <see cref="EmployeeProfile"/> is already where leave entitlement lives.
///
/// Stores a date of birth and nothing derived from it — see
/// <see cref="Services.PerChildLeaveCalculationService"/>. A child who has passed
/// the eligibility age is kept, not deleted: their approved leave is still history,
/// and the row is what the per-child ledger is queried by.
/// </summary>
public class Child
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string EmployeeProfileId { get; set; } = string.Empty;
    public EmployeeProfile? EmployeeProfile { get; set; }

    /// <summary>First name is enough to tell one employee's children apart in a picker.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Date only, like <see cref="User.DateOfBirth"/>. Age is never stored.</summary>
    public DateOnly DateOfBirth { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AnnualLeave> LeaveRequests { get; set; } = new List<AnnualLeave>();
}
