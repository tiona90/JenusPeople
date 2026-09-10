using Application.AnnualLeaves.Commands;
using Application.Children.DTOs;
using Application.Children.Support;
using Application.Core;
using Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Queries;

public class GetChildLeaveEntitlements
{
    public class Query : IRequest<Result<ChildLeaveEntitlementSummaryDto>>
    {
        /// <summary>The employee's user id. Null means the caller themselves.</summary>
        public string? EmployeeId { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<ChildLeaveEntitlementSummaryDto>>
    {
        public async Task<Result<ChildLeaveEntitlementSummaryDto>> Handle(
            Query request, CancellationToken cancellationToken)
        {
            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, request.EmployeeId,
                request.IsAdmin, request.IsManager, forWrite: false, cancellationToken);

            if (!access.IsSuccess)
                return Result<ChildLeaveEntitlementSummaryDto>.FailureFrom(access);

            var profile = access.Value!;

            // Whichever active leave type carries a per-child entitlement — paternity
            // in practice. No type, no ledger: there is nothing to be entitled to.
            var leaveType = await ChildProjection.ResolvePerChildLeaveTypeAsync(context, cancellationToken);

            var summary = new ChildLeaveEntitlementSummaryDto();

            if (leaveType is null)
                return Result<ChildLeaveEntitlementSummaryDto>.Success(summary);

            summary.LeaveTypeId = leaveType.Id;
            summary.LeaveTypeName = leaveType.Name;

            var children = await context.Children
                .AsNoTracking()
                .Where(c => c.EmployeeProfileId == profile.Id)
                .OrderBy(c => c.DateOfBirth)
                .ToListAsync(cancellationToken);

            var totalDays = PerChildLeaveCalculationService.WeeksToBusinessDays(leaveType.PerChildTotalWeeks);
            var yearCapDays = PerChildLeaveCalculationService.WeeksToBusinessDays(leaveType.PerChildWeeksPerYear);

            var startMonth = await LeaveYearQueries.GetLeaveYearStartMonthAsync(context, cancellationToken);
            var leaveYearKey = LeaveCalculationService.GetLeaveYearKey(DateTime.UtcNow, startMonth);
            var (leaveYearStart, leaveYearEnd) = LeaveCalculationService.GetLeaveYearBounds(leaveYearKey, startMonth);
            var yearHolidays = await LeaveYearQueries.GetHolidaySetAsync(
                context, leaveYearStart, leaveYearEnd, cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            foreach (var child in children)
            {
                var approved = await PerChildLeaveBalanceCalculator.ApprovedLeaveForChildAsync(
                    context, child.Id, excludeLeaveId: null, cancellationToken);

                var usedDays = await PerChildLeaveBalanceCalculator.UsedBusinessDaysAsync(
                    context, approved, cancellationToken);

                var usedThisYear = approved.Sum(leave => LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
                    leave.StartDate, leave.EndDate, leaveYearKey, startMonth, yearHolidays));

                var isEligible = PerChildLeaveCalculationService.IsEligibleOn(
                    child.DateOfBirth, today, leaveType.ChildEligibleUntilAge);

                /* An ineligible child has no remaining entitlement, whatever the
                   arithmetic would say — the rule applies regardless of how much
                   they had used. Their used figure stays, because it happened.
                   The configured entitlement (TotalDays/TotalWeeks/ThisYearCapDays)
                   is reported regardless of eligibility -- zeroing it would misstate
                   the child's real entitlement rather than explain why it is now
                   moot. Only the two *remaining* figures are forced to zero. */
                var remainingDays = isEligible
                    ? PerChildLeaveCalculationService.RemainingDays(totalDays, usedDays)
                    : 0;
                // The lesser of the yearly remainder and the lifetime remainder:
                // with 3 days of total entitlement left, "25 days this year" would
                // be a lie.
                var remainingThisYear = isEligible
                    ? Math.Min(
                        PerChildLeaveCalculationService.RemainingDays(yearCapDays, usedThisYear),
                        remainingDays)
                    : 0;

                summary.Children.Add(new ChildLeaveEntitlementDto
                {
                    ChildId = child.Id,
                    Name = child.Name,
                    DateOfBirth = child.DateOfBirth,
                    AgeYears = PerChildLeaveCalculationService.AgeOn(child.DateOfBirth, today),
                    IsEligible = isEligible,
                    LastEligibleDate = PerChildLeaveCalculationService.LastEligibleDate(
                        child.DateOfBirth, leaveType.ChildEligibleUntilAge),
                    TotalDays = totalDays,
                    TotalWeeks = PerChildLeaveCalculationService.BusinessDaysToWeeks(totalDays),
                    UsedDays = usedDays,
                    RemainingDays = remainingDays,
                    ThisYearCapDays = yearCapDays,
                    ThisYearUsedDays = usedThisYear,
                    ThisYearRemainingDays = remainingThisYear,
                    LeaveYearStart = leaveYearStart,
                    LeaveYearEnd = leaveYearEnd,
                });
            }

            // Requirement 10: the employee's totals are a projection over eligible
            // children, so they fall the moment one ages out — no recalculation.
            var eligible = summary.Children.Where(c => c.IsEligible).ToList();
            summary.EligibleChildCount = eligible.Count;
            summary.TotalRemainingDays = eligible.Sum(c => c.RemainingDays);
            summary.ThisYearCapDays = eligible.Count * yearCapDays;
            summary.ThisYearRemainingDays = eligible.Sum(c => c.ThisYearRemainingDays);

            return Result<ChildLeaveEntitlementSummaryDto>.Success(summary);
        }
    }
}
