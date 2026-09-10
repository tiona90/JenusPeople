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
    /// The leave type carrying a per-child entitlement (there is at most meant to be
    /// one — see <c>LeaveType.PerChildEntitlement</c>), or <c>null</c> when none is
    /// configured. Extracted so the age-only callers
    /// (<see cref="ResolveEligibleUntilAgeAsync"/>) and a caller needing the whole
    /// row (the entitlement ledger, which also reads
    /// <c>PerChildTotalWeeks</c>/<c>PerChildWeeksPerYear</c>) share one lookup
    /// rather than two copies drifting apart.
    ///
    /// <para><b>Deliberately not filtered on <c>IsActive</c>.</b> Enforcement does not
    /// filter either: <see cref="Application.AnnualLeaves.Commands.PerChildLeaveBalanceCalculator"/>
    /// and <c>EditAnnualLeave</c> both look the type up by id alone. When the two
    /// disagreed, deactivating Paternity Leave left the API still demanding a child on
    /// an edit or an approval while this ledger reported no per-child type at all — so
    /// the picker told an employee with three children to go and add some, and a
    /// pending row became unapprovable. Reporting is now matched to enforcement rather
    /// than the other way round: making the calculator skip an inactive type would
    /// instead turn every cap off the moment the type is deactivated, letting a pending
    /// paternity row approve for unbounded days. A deactivated type cannot be chosen
    /// for a new request anyway — <c>CreateAnnualLeave</c> filters on <c>IsActive</c>
    /// — so nothing new is offered by reporting it; only the rows that already exist
    /// are described correctly.</para>
    ///
    /// <para>An active type still wins when several carry the toggle, so the ordinary
    /// case (one active per-child type, plus a retired one) resolves exactly as it did
    /// before.</para>
    /// </summary>
    public static Task<LeaveType?> ResolvePerChildLeaveTypeAsync(AppDbContext context, CancellationToken cancellationToken)
        => context.LeaveTypes
            .AsNoTracking()
            .Where(lt => lt.PerChildEntitlement)
            .OrderByDescending(lt => lt.IsActive)
            .ThenBy(lt => lt.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The age at which a child stops being eligible, per the active per-child
    /// leave type. No invented fallback: when no such type exists, 0 is returned,
    /// which reads every child as ineligible. That is the truth — nothing grants
    /// per-child leave — where a made-up number like 15 would tell a caller
    /// "eligible" only to have the leave request refused later against the real,
    /// unconfigured figure.
    /// </summary>
    public static async Task<int> ResolveEligibleUntilAgeAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var leaveType = await ResolvePerChildLeaveTypeAsync(context, cancellationToken);
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
