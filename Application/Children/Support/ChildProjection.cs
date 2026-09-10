using Application.Children.DTOs;
using Domain;
using Domain.Services;

namespace Application.Children.Support;

/// <summary>
/// Child → <see cref="ChildDto"/>, including the computed age and eligibility.
/// Shared so the list, the create/update responses and the entitlement ledger cannot
/// answer the eligibility question three slightly different ways.
/// </summary>
internal static class ChildProjection
{
    /// <summary>
    /// Used when no per-child leave type is in play (a plain child list). The real
    /// figure comes from <c>LeaveType.ChildEligibleUntilAge</c>; this is only the
    /// fallback for rendering a list when no type has been chosen yet.
    /// </summary>
    public const int DefaultEligibilityAge = 15;

    public static ChildDto ToDto(Child child, int eligibleUntilAge)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new ChildDto
        {
            Id = child.Id,
            Name = child.Name,
            DateOfBirth = child.DateOfBirth,
            AgeYears = PerChildLeaveCalculationService.AgeOn(child.DateOfBirth, today),
            IsEligible = PerChildLeaveCalculationService.IsEligibleOn(child.DateOfBirth, today, eligibleUntilAge),
            LastEligibleDate = PerChildLeaveCalculationService.LastEligibleDate(child.DateOfBirth, eligibleUntilAge),
        };
    }
}
