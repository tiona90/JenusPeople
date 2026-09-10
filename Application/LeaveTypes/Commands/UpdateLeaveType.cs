using Application.AnnualLeaves.Commands;
using Application.Core;
using Application.LeaveTypes.DTOs;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.LeaveTypes.Commands;

public class UpdateLeaveType
{
    public class Command : IRequest<Result<LeaveTypeDto>>
    {
        public int Id { get; set; }
        public required UpsertLeaveTypeRequest LeaveType { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<LeaveTypeDto>>
    {
        public async Task<Result<LeaveTypeDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var leaveType = await context.LeaveTypes.FindAsync([request.Id], cancellationToken);
            if (leaveType is null)
                return Result<LeaveTypeDto>.Failure("Leave type not found.");

            var wasRequiringApproval = leaveType.RequiresApproval;
            var previousAllowance = leaveType.DefaultAllowance;

            /* A seeded type cannot be renamed — other code finds these by name, so a
               rename breaks it silently. Refused rather than quietly ignored: the edit
               dialog makes the field read-only, so anything reaching here with a
               different name is a caller going around the UI, and it should be told.
               Checked before the map, which would otherwise have already overwritten
               the stored name. Everything else about the type stays editable. */
            if (SystemLeaveTypes.IsSystem(leaveType.Name)
                && !string.Equals(leaveType.Name.Trim(), request.LeaveType.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Result<LeaveTypeDto>.Conflict(
                    $"{leaveType.Name} is a built-in leave type and cannot be renamed.");
            }

            mapper.Map(request.LeaveType, leaveType);

            var affectedProfiles = new Dictionary<string, EmployeeProfile>();

            /* Leave is configured once, for everyone: this allowance is the annual-leave
               budget, so moving it has to move every employee with it. Leaving profiles
               on their old AnnualLeaveEntitlement would have the screens quote the new
               figure while AnnualLeaveBalanceCalculator still enforces the old one. */
            var allowanceMoved = leaveType.AffectsBalance && leaveType.DefaultAllowance != previousAllowance;

            if (allowanceMoved && leaveType.DefaultAllowance <= 0)
            {
                // An entitlement of 0 switches the balance check off outright, so this
                // would quietly unpolice every request in the company at once.
                return Result<LeaveTypeDto>.Failure(
                    "The annual leave allowance must be at least 1 day — a 0 would remove the balance check for every employee.");
            }

            if (allowanceMoved)
            {
                var everyProfile = await context.EmployeeProfiles.ToListAsync(cancellationToken);
                foreach (var employeeProfile in everyProfile)
                {
                    employeeProfile.AnnualLeaveEntitlement = leaveType.DefaultAllowance;
                    // Picked up by the balance sync below, which recomputes what is left
                    // after the days each of them has already taken.
                    affectedProfiles[employeeProfile.Id] = employeeProfile;
                }
            }

            if (wasRequiringApproval && !leaveType.RequiresApproval && leaveType.IsActive)
            {
                var pendingLeaves = await context.AnnualLeaves
                    .Where(al => al.LeaveTypeId == leaveType.Id && al.Status == AnnualLeaveStatus.Pending)
                    .ToListAsync(cancellationToken);

                foreach (var annualLeave in pendingLeaves)
                {
                    var employeeProfile = await context.EmployeeProfiles
                        .FirstOrDefaultAsync(ep => ep.Id == annualLeave.EmployeeProfileId, cancellationToken);

                    if (employeeProfile is not null)
                    {
                        var balanceError = await AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync(
                            context,
                            employeeProfile,
                            annualLeave,
                            excludeLeaveId: annualLeave.Id,
                            cancellationToken);
                        if (balanceError is not null)
                            return Result<LeaveTypeDto>.Conflict(balanceError);

                        affectedProfiles[employeeProfile.Id] = employeeProfile;
                    }

                    annualLeave.Status = AnnualLeaveStatus.Approved;
                    annualLeave.ApprovedAt = DateTime.UtcNow;
                    annualLeave.ApprovedById = null;

                    context.LeaveStatusHistories.Add(new LeaveStatusHistory
                    {
                        Id = Guid.NewGuid().ToString(),
                        AnnualLeaveId = annualLeave.Id,
                        ChangedByUserId = annualLeave.EmployeeId,
                        OldStatus = AnnualLeaveStatus.Pending,
                        NewStatus = AnnualLeaveStatus.Approved,
                        Comment = "Automatically approved based on leave type settings.",
                        ChangedAt = DateTime.UtcNow,
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            foreach (var employeeProfile in affectedProfiles.Values)
            {
                await AnnualLeaveBalanceCalculator.SyncCurrentYearBalanceAsync(context, employeeProfile, cancellationToken);
            }

            if (affectedProfiles.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
            }

            return Result<LeaveTypeDto>.Success(mapper.Map<LeaveTypeDto>(leaveType));
        }
    }
}
