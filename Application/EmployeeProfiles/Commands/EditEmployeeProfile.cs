using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Application.EmployeeProfiles.DTOs;

namespace Application.EmployeeProfiles.Commands;

public class EditEmployeeProfile
{
    public class Command : IRequest<Result<Unit>>
    {
        public required EditEmployeeProfileRequest EmployeeProfile { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var employeeProfile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.Id == request.EmployeeProfile.Id, cancellationToken);

            if (employeeProfile is null)
                return Result<Unit>.Failure("Cannot find employee profile.");

            employeeProfile.DepartmentId = request.EmployeeProfile.DepartmentId;
            employeeProfile.ManagerId = request.EmployeeProfile.ManagerId;
            employeeProfile.AnnualLeaveEntitlement = request.EmployeeProfile.AnnualLeaveEntitlement;
            employeeProfile.LeaveBalance = request.EmployeeProfile.LeaveBalance;
            employeeProfile.JobTitle = request.EmployeeProfile.JobTitle;

            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}