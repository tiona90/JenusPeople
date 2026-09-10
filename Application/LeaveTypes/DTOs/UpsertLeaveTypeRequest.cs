using System.ComponentModel.DataAnnotations;
using Domain;

namespace Application.LeaveTypes.DTOs;

public class UpsertLeaveTypeRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public bool RequiresApproval { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public bool AffectsBalance { get; set; } = false;

    [StringLength(16)]
    public string Icon { get; set; } = "🏷️";

    [StringLength(30)]
    public string ColorKey { get; set; } = "default";

    [StringLength(300)]
    public string Description { get; set; } = string.Empty;

    public bool Paid { get; set; } = true;

    public AttachmentPolicy AttachmentPolicy { get; set; } = AttachmentPolicy.None;

    [Range(0, 365)]
    public int DefaultAllowance { get; set; }

    [StringLength(30)]
    public string AllowanceUnit { get; set; } = "days/year";

    [Range(0, 365)]
    public int MaxCarryoverDays { get; set; }

    /// <summary>
    /// Turns the per-child entitlement engine on. The three numbers below are
    /// validated only when this is true — every other leave type leaves them at 0.
    /// </summary>
    public bool PerChildEntitlement { get; set; }

    [Range(0, 260)]
    public int PerChildTotalWeeks { get; set; }

    [Range(0, 52)]
    public int PerChildWeeksPerYear { get; set; }

    [Range(0, 30)]
    public int ChildEligibleUntilAge { get; set; }

    [StringLength(250)]
    public string AccrualNotes { get; set; } = string.Empty;

    [Range(0, 365)]
    public int MinNoticeDays { get; set; }

    [Range(0, 365)]
    public int MaxConsecutiveDays { get; set; }

    public bool HalfDayAllowed { get; set; }

    [StringLength(250)]
    public string EligibilityNotes { get; set; } = "All employees";

    public EligibilityScope EligibilityScope { get; set; } = EligibilityScope.All;
}
