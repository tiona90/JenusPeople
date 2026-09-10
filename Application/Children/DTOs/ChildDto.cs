namespace Application.Children.DTOs;

/// <summary>
/// One child as a client sees them. Age and eligibility are computed on every read
/// from <see cref="DateOfBirth"/> — nothing about them is stored, which is what makes
/// a child aging out of eligibility need no job and no recalculation step.
/// </summary>
public class ChildDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public int AgeYears { get; set; }
    public bool IsEligible { get; set; }

    /// <summary>The last date leave for this child may end on.</summary>
    public DateOnly LastEligibleDate { get; set; }
}
