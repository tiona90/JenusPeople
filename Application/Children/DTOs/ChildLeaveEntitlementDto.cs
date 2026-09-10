namespace Application.Children.DTOs;

/// <summary>
/// One child's per-child leave ledger. Every figure is derived — from the child's
/// date of birth, the leave type's configuration, and that child's approved leave —
/// so nothing here can go stale and nothing needs recalculating when a child ages
/// out.
/// </summary>
public class ChildLeaveEntitlementDto
{
    public string ChildId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public int AgeYears { get; set; }
    public bool IsEligible { get; set; }
    public DateOnly LastEligibleDate { get; set; }

    /// <summary>Lifetime entitlement in business days. 90 for 18 weeks.</summary>
    public int TotalDays { get; set; }
    public decimal TotalWeeks { get; set; }
    public int UsedDays { get; set; }
    public int RemainingDays { get; set; }

    /// <summary>The per-year cap in business days. 25 for 5 weeks.</summary>
    public int ThisYearCapDays { get; set; }
    public int ThisYearUsedDays { get; set; }
    public int ThisYearRemainingDays { get; set; }

    /// <summary>The leave-year window the "this year" figures describe.</summary>
    public DateTime LeaveYearStart { get; set; }
    public DateTime LeaveYearEnd { get; set; }
}

/// <summary>
/// The employee's per-child ledger. The totals cover <b>eligible</b> children only,
/// which is what makes a child turning 15 reduce them with no recalculation step.
/// </summary>
public class ChildLeaveEntitlementSummaryDto
{
    /// <summary>Null when no active leave type has a per-child entitlement.</summary>
    public int? LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;

    public int EligibleChildCount { get; set; }
    public int TotalRemainingDays { get; set; }
    public int ThisYearCapDays { get; set; }
    public int ThisYearRemainingDays { get; set; }

    /// <summary>
    /// Every declared child, ineligible ones included with <c>IsEligible</c> false,
    /// so the UI can explain why rather than silently omitting them.
    /// </summary>
    public List<ChildLeaveEntitlementDto> Children { get; set; } = [];
}
