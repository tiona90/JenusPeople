using System.ComponentModel.DataAnnotations;
using Domain.Interfaces;

namespace Domain;

public class EmployeeProfile : ISoftDeletable, IAuditable
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

    /// <summary>
    /// Null for a System Administrator, who sits outside the department structure: the role sees
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

    /// <summary>
    /// Whole days, always: an entitlement is stamped from
    /// <see cref="LeaveType.DefaultAllowance"/>, which is an int, and is never
    /// edited per person. Never write a 0 — <c>CheckSufficientBalanceAsync</c>
    /// reads it as "no balance check at all", not as "no allowance".
    /// </summary>
    public int AnnualLeaveEntitlement { get; set; }

    /// <summary>
    /// What is left of the entitlement, and decimal because a half day costs 0.5.
    /// An entitlement of 23 days with one half day taken sits at 22.5.
    ///
    /// Derived, never edited per person: only the three writers named in CLAUDE.md
    /// set it, all of them from the allowance.
    /// </summary>
    public decimal LeaveBalance { get; set; }

    /// <summary>
    /// Bumped by every approval that charges this employee's leave, in the same
    /// save as the approval, and checked on it — so two approvals racing for one
    /// person's days cannot both commit. Each checks the balance against the rows
    /// it can see, and neither sees the other until it commits; without a token
    /// on the ledger they share, both passed and the days were charged twice.
    /// <c>AnnualLeave.RowVersion</c> protects one row only. An int rather than a
    /// rowversion so the SQLite tests can exercise it too.
    /// </summary>
    [ConcurrencyCheck]
    public int LedgerVersion { get; set; }

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

    /// <summary>
    /// The day this person started working here. Recorded HR data an administrator
    /// maintains on the Users panel, beside the department and the job title — the
    /// three fields that say where somebody sits in the organisation.
    ///
    /// Required for an Employee and a Manager, and refused for a System Administrator, exactly as
    /// <see cref="DepartmentId"/> is: the Profile section that collects all three is
    /// hidden for a System Administrator, so a rule that demanded one would make a System Administrator
    /// impossible to create through the only screen that creates users. Enforced by
    /// <c>CreateAdminUserValidator</c> and <c>EditEmployeeProfileRequestValidator</c>,
    /// and mirrored on the client by <c>lib/validation/person.ts</c>.
    ///
    /// Not <see cref="CreatedAt"/>, which is when the row was written — when an
    /// administrator got round to keying the account in, and the same day for
    /// everybody migrated in at once.
    ///
    /// Nullable because every profile predating the column has no value, and a hire
    /// date for a real person is not ours to invent. Nothing backfills it: the rule
    /// is what makes it mandatory, so an older account has to be given one the next
    /// time it is saved — the same trade <c>PersonFieldRules.ValidDateOfBirth</c>
    /// documents.
    ///
    /// Two things read it. <c>MinimumServiceRule</c> measures it against
    /// <c>LeaveType.MinServiceMonths</c> to decide whether a type is offered yet, and
    /// <c>AnnualLeaveBalanceCalculator.EntitlementForLeaveYear</c> scales the first
    /// leave year's balance from it when the balance type sets
    /// <c>LeaveType.ProRateFirstYear</c>. A null passes both, for the reason above —
    /// nobody entered it, which is not "started today". Neither writes
    /// <see cref="AnnualLeaveEntitlement"/>, which is stamped from the leave type's
    /// allowance in full and never set per person: the pro-rating is applied to one
    /// leave year on every read and check, which is what makes the next year full
    /// without a job.
    /// </summary>
    public DateOnly? EmploymentStartDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
