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

    /// <summary>
    /// Whether this is one of the seeded types that cannot be renamed or deleted
    /// (<see cref="SystemLeaveTypes"/>). Everything else about it stays editable.
    ///
    /// Derived from the name rather than mapped or stored, so the flag a screen
    /// reads can never disagree with the name beside it, and the client needs no
    /// copy of the list. The server is still the one that enforces it — this only
    /// tells the UI which controls to lock.
    /// </summary>
    public bool IsSystem => SystemLeaveTypes.IsSystem(Name);
}
