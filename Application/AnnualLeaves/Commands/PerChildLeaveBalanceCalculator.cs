using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// Enforces a per-child leave entitlement — paternity leave in practice: so many
/// weeks per child, only while that child is under a given age, and only so many of
/// them in any one leave year.
///
/// A sibling of <see cref="AnnualLeaveBalanceCalculator"/> and deliberately the same
/// shape: it loads its inputs from the DbContext, delegates every calculation to the
/// domain services, and returns a human-readable message rather than throwing, so a
/// handler can map it straight to <c>Result&lt;T&gt;.Failure</c>.
///
/// This is a second ledger, distinct from the pooled <c>EmployeeProfile.LeaveBalance</c>
/// the other calculator keeps. The two must never both apply to one leave type —
/// <c>UpsertLeaveTypeRequestValidator</c> refuses a type that sets both
/// <c>PerChildEntitlement</c> and <c>AffectsBalance</c> — or one day of leave would
/// be charged twice.
///
/// Nothing is stored: the ledger is a projection over approved leave rows, which is
/// why a child aging out needs no recalculation step. Their usage stays in history
/// and their remaining entitlement is simply gone.
/// </summary>
internal static class PerChildLeaveBalanceCalculator
{
    /// <summary>
    /// Returns a human-readable error when the request breaks the per-child rules,
    /// or <c>null</c> when it is allowed — including when the leave type has no
    /// per-child entitlement, in which case there is nothing to enforce.
    /// </summary>
    public static async Task<string?> CheckPerChildEntitlementAsync(
        AppDbContext context,
        AnnualLeave annualLeave,
        EmployeeProfile employeeProfile,
        string? excludeLeaveId,
        CancellationToken cancellationToken)
    {
        var leaveType = annualLeave.LeaveTypeId.HasValue
            ? await context.LeaveTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(lt => lt.Id == annualLeave.LeaveTypeId.Value, cancellationToken)
            : null;

        if (leaveType is null || !leaveType.PerChildEntitlement)
            return null;

        if (string.IsNullOrWhiteSpace(annualLeave.ChildId))
            return $"Select the child this {leaveType.Name} is for.";

        var childId = annualLeave.ChildId;
        var child = await context.Children
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == childId, cancellationToken);

        // One message for "no such child" and "not yours" on purpose: telling the
        // caller which of the two it was would confirm the existence of another
        // employee's record.
        if (child is null || child.EmployeeProfileId != employeeProfile.Id)
            return "The selected child is not on your profile.";

        var lastEligibleDate = PerChildLeaveCalculationService.LastEligibleDate(
            child.DateOfBirth, leaveType.ChildEligibleUntilAge);

        // Asked of the END date, so no request is ever part-eligible. This single
        // check covers a request that starts after the birthday and one that
        // straddles it.
        if (DateOnly.FromDateTime(annualLeave.EndDate.Date) > lastEligibleDate)
        {
            var birthday = lastEligibleDate.AddDays(1);
            return $"{child.Name} turns {leaveType.ChildEligibleUntilAge} on {birthday:dd MMM yyyy} — " +
                $"{leaveType.Name} for this child must end on or before {lastEligibleDate:dd MMM yyyy}.";
        }

        return null;
    }
}
