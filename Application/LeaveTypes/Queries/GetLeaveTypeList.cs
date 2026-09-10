using Application.LeaveTypes.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.LeaveTypes.Queries;

public class GetLeaveTypeList
{
    public class Query : IRequest<List<LeaveTypeDto>>
    {
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, List<LeaveTypeDto>>
    {
        public async Task<List<LeaveTypeDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            return await context.LeaveTypes
                .AsNoTracking()
                .OrderBy(lt => lt.Name)
                .Select(lt => new LeaveTypeDto
                {
                    Id = lt.Id,
                    Name = lt.Name,
                    RequiresApproval = lt.RequiresApproval,
                    IsActive = lt.IsActive,
                    AffectsBalance = lt.AffectsBalance,
                    Icon = lt.Icon,
                    ColorKey = lt.ColorKey,
                    Description = lt.Description,
                    Paid = lt.Paid,
                    AttachmentPolicy = lt.AttachmentPolicy,
                    DefaultAllowance = lt.DefaultAllowance,
                    AllowanceUnit = lt.AllowanceUnit,
                    MaxCarryoverDays = lt.MaxCarryoverDays,
                    PerChildEntitlement = lt.PerChildEntitlement,
                    PerChildTotalWeeks = lt.PerChildTotalWeeks,
                    PerChildWeeksPerYear = lt.PerChildWeeksPerYear,
                    ChildEligibleUntilAge = lt.ChildEligibleUntilAge,
                    AccrualNotes = lt.AccrualNotes,
                    MinNoticeDays = lt.MinNoticeDays,
                    MaxConsecutiveDays = lt.MaxConsecutiveDays,
                    HalfDayAllowed = lt.HalfDayAllowed,
                    EligibilityNotes = lt.EligibilityNotes,
                    EligibilityScope = lt.EligibilityScope
                })
                .ToListAsync(cancellationToken);
        }
    }
}
