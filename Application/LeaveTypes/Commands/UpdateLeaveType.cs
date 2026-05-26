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

            mapper.Map(request.LeaveType, leaveType);

            var affectedProfiles = new Dictionary<string, EmployeeProfile>();

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
                        await AnnualLeaveBalanceCalculator.EnsureSufficientBalanceAsync(
                            context,
                            employeeProfile,
                            annualLeave,
                            excludeLeaveId: annualLeave.Id,
                            cancellationToken);

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
