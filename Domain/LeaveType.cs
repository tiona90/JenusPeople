namespace Domain;

public enum AttachmentPolicy
{
    None = 0,
    Optional = 1,
    Required = 2
}

public enum EligibilityScope
{
    All = 0,
    Limited = 1
}

public class LeaveType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool IsActive { get; set; }
    public bool AffectsBalance { get; set; }

    public string Icon { get; set; } = "🏷️";
    public string ColorKey { get; set; } = "default";
    public string Description { get; set; } = string.Empty;
    public bool Paid { get; set; } = true;
    public AttachmentPolicy AttachmentPolicy { get; set; } = AttachmentPolicy.None;
    public int DefaultAllowance { get; set; }
    public string AllowanceUnit { get; set; } = "days/year";
    /* How many unused days of this type survive the year-end rollover. This was
       org-wide (AppSettings.MaxCarryoverDays), which made it a second number free to
       disagree with the allowance it caps, and left the cap unstated for every type
       but annual leave. It belongs beside the allowance, per type. */
    public int MaxCarryoverDays { get; set; }

    /* Per-child entitlement. Paternity leave is not one budget per employee but one
       per child, bounded by the child's age -- so the three numbers that describe it
       sit here beside the allowance, the same move MoveCarryoverCapToLeaveType made
       for the carryover cap. When the toggle is false all three are ignored and the
       type behaves exactly as it did.

       Note the opposite hazard to EmployeeProfile.AnnualLeaveEntitlement, where a
       stored 0 disables the balance check outright: a 0 here refuses every request
       instead of waving them all through. The validator still refuses 0, but the
       safe direction is the default. */
    public bool PerChildEntitlement { get; set; }
    /// <summary>Lifetime entitlement per eligible child, in weeks. 18 for paternity leave.</summary>
    public int PerChildTotalWeeks { get; set; }
    /// <summary>Cap per leave year per eligible child, in weeks. 5 for paternity leave.</summary>
    public int PerChildWeeksPerYear { get; set; }
    /// <summary>The age at which a child stops being eligible. 15 for paternity leave.</summary>
    public int ChildEligibleUntilAge { get; set; }

    public string AccrualNotes { get; set; } = string.Empty;
    public int MinNoticeDays { get; set; }
    public int MaxConsecutiveDays { get; set; }
    public bool HalfDayAllowed { get; set; }
    public string EligibilityNotes { get; set; } = "All employees";
    public EligibilityScope EligibilityScope { get; set; } = EligibilityScope.All;

    public ICollection<AnnualLeave> AnnualLeaves { get; set; } = new List<AnnualLeave>();
}
