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
    public string AccrualNotes { get; set; } = string.Empty;
    public int MinNoticeDays { get; set; }
    public int MaxConsecutiveDays { get; set; }
    public bool HalfDayAllowed { get; set; }
    public string EligibilityNotes { get; set; } = "All employees";
    public EligibilityScope EligibilityScope { get; set; } = EligibilityScope.All;

    public ICollection<AnnualLeave> AnnualLeaves { get; set; } = new List<AnnualLeave>();
}
