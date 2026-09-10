using Domain;

namespace Application.LeaveTypes.DTOs;

public class LeaveTypeDto
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
    public AttachmentPolicy AttachmentPolicy { get; set; }
    public int DefaultAllowance { get; set; }
    public string AllowanceUnit { get; set; } = "days/year";
    public int MaxCarryoverDays { get; set; }
    public bool PerChildEntitlement { get; set; }
    public int PerChildTotalWeeks { get; set; }
    public int PerChildWeeksPerYear { get; set; }
    public int ChildEligibleUntilAge { get; set; }
    public string AccrualNotes { get; set; } = string.Empty;
    public int MinNoticeDays { get; set; }
    public int MaxConsecutiveDays { get; set; }
    public bool HalfDayAllowed { get; set; }
    public string EligibilityNotes { get; set; } = "All employees";
    public EligibilityScope EligibilityScope { get; set; }
}
