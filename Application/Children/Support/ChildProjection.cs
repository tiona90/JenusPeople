using Application.Children.DTOs;
using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Support;

/// <summary>
/// Child → <see cref="ChildDto"/>, including the computed age and eligibility.
/// Shared so the list, the create/update responses and the entitlement ledger cannot
/// answer the eligibility question three slightly different ways.
/// </summary>
internal static class ChildProjection
{
    /// <summary>
    /// The age at which a child stops being eligible, per the active per-child
    /// leave type (there is at most meant to be one — see
    /// <c>LeaveType.PerChildEntitlement</c>). No invented fallback: when no such
    /// type exists, 0 is returned, which reads every child as ineligible. That is
    /// the truth — nothing grants per-child leave — where a made-up number like 15
    /// would tell a caller "eligible" only to have the leave request refused later
    /// against the real, unconfigured figure.
    /// </summary>
    public static async Task<int> ResolveEligibleUntilAgeAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var leaveType = await context.LeaveTypes
            .AsNoTracking()
            .Where(lt => lt.PerChildEntitlement && lt.IsActive)
            .OrderBy(lt => lt.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return leaveType?.ChildEligibleUntilAge ?? 0;
    }

    /// <summary>
    /// <paramref name="onDate"/> is a parameter rather than read from
    /// <see cref="DateTime.UtcNow"/> internally so a test can pin it and assert a
    /// deterministic age — otherwise an assertion on <c>AgeYears</c> quietly breaks
    /// on whatever date the birthday next falls on, reading as a bug in
    /// <see cref="PerChildLeaveCalculationService.AgeOn"/> rather than as the
    /// passage of time it actually is.
    /// </summary>
    public static ChildDto ToDto(Child child, int eligibleUntilAge, DateOnly onDate)
    {
        return new ChildDto
        {
            Id = child.Id,
            Name = child.Name,
            DateOfBirth = child.DateOfBirth,
            AgeYears = PerChildLeaveCalculationService.AgeOn(child.DateOfBirth, onDate),
            IsEligible = PerChildLeaveCalculationService.IsEligibleOn(child.DateOfBirth, onDate, eligibleUntilAge),
            LastEligibleDate = PerChildLeaveCalculationService.LastEligibleDate(child.DateOfBirth, eligibleUntilAge),
        };
    }
}
