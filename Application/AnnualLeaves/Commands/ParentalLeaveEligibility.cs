using Application.Children.Support;
using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// Who may file Maternity and Paternity Leave: an employee whose recorded
/// <see cref="Gender"/> matches the type, and who has a child young enough to
/// qualify. Returns the refusal message, or <c>null</c> when the request may go
/// ahead — the same shape as <see cref="PerChildLeaveBalanceCalculator"/>, which
/// runs beside it.
///
/// This is the one place <see cref="User.Gender"/> is consulted. It was recorded
/// HR data that gated nothing until this rule; the comment on the property says so
/// and is kept in step with it.
///
/// Two deliberate asymmetries, both of which the tests pin:
///
/// <list type="bullet">
/// <item><description>
/// An unspecified gender passes. <c>null</c> means "nobody has entered it" — the
/// state of every account created before the column existed — so reading it as a
/// mismatch would take parental leave away from the whole company until an
/// administrator filled the field in one person at a time. The check narrows only
/// on a gender somebody actually recorded.
/// </description></item>
/// <item><description>
/// The eligible-child rule is skipped for a type that keeps its own per-child
/// ledger. Paternity Leave already refuses a request naming no child, a child that
/// is not the employee's, or one who has aged out, each with a message naming the
/// child and the date — see <see cref="PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync"/>.
/// Restating the rule in front of it would replace those messages with a vaguer one.
/// </description></item>
/// </list>
/// </summary>
public static class ParentalLeaveEligibility
{
    /// <summary>
    /// The gender a parental leave type is offered to, or <c>null</c> for a type
    /// the rule does not reach. Keyed off <see cref="SystemLeaveTypes"/> rather
    /// than a column, for the reason that class documents: these two names are
    /// frozen, and a stored flag could drift from the name displayed beside it.
    /// </summary>
    private static Gender? OfferedTo(string? leaveTypeName)
    {
        if (SystemLeaveTypes.IsMaternity(leaveTypeName)) return Gender.Female;
        if (SystemLeaveTypes.IsPaternity(leaveTypeName)) return Gender.Male;
        return null;
    }

    public static async Task<string?> CheckAsync(
        AppDbContext context,
        LeaveType leaveType,
        string employeeUserId,
        EmployeeProfile employeeProfile,
        CancellationToken cancellationToken)
    {
        var offeredTo = OfferedTo(leaveType.Name);
        if (offeredTo is null)
            return null;

        var gender = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == employeeUserId)
            .Select(user => user.Gender)
            .FirstOrDefaultAsync(cancellationToken);

        if (gender.HasValue && gender.Value != offeredTo.Value)
            return $"{leaveType.Name} is not available to you.";

        // Paternity Leave's per-child ledger enforces this far more precisely.
        if (leaveType.PerChildEntitlement)
            return null;

        var eligibleUntilAge = await ResolveEligibleUntilAgeAsync(context, leaveType, cancellationToken);

        // Nothing configured says what "eligible" means for this type, so there is
        // no rule to apply. Deliberately the opposite reading to
        // ChildProjection.ResolveEligibleUntilAgeAsync, where a 0 correctly means
        // "no per-child entitlement exists, so nobody has one": here the type grants
        // leave whatever the per-child numbers say, and refusing everyone against an
        // age nobody set would be refusing against a blank field.
        if (eligibleUntilAge <= 0)
            return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var birthdays = await context.Children
            .AsNoTracking()
            .Where(child => child.EmployeeProfileId == employeeProfile.Id)
            .Select(child => child.DateOfBirth)
            .ToListAsync(cancellationToken);

        var hasEligibleChild = birthdays.Any(
            dateOfBirth => PerChildLeaveCalculationService.IsEligibleOn(dateOfBirth, today, eligibleUntilAge));

        if (!hasEligibleChild)
            return $"{leaveType.Name} is available only while you have a child under {eligibleUntilAge}.";

        return null;
    }

    /// <summary>
    /// The cut-off age this type is measured against: its own when it configures
    /// one, otherwise the per-child type's. Maternity Leave is seeded with all
    /// three per-child columns at 0 — it grants a flat allowance, not a per-child
    /// one — so without the fallback it would quote "under 0" at every employee.
    /// </summary>
    private static async Task<int> ResolveEligibleUntilAgeAsync(
        AppDbContext context, LeaveType leaveType, CancellationToken cancellationToken)
    {
        if (leaveType.ChildEligibleUntilAge > 0)
            return leaveType.ChildEligibleUntilAge;

        return await ChildProjection.ResolveEligibleUntilAgeAsync(context, cancellationToken);
    }
}
