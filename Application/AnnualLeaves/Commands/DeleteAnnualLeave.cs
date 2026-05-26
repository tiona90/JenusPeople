using System;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

public class DeleteAnnualLeave
{
    public class Command : IRequest
    {
        public required string Id { get; set; }
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }
    public class Handler(AppDbContext context) : IRequestHandler<Command>
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var annualLeave = await context.AnnualLeaves
             .FindAsync([request.Id], cancellationToken)
             ?? throw new Exception("Cannot find the annual leave");

            if (string.IsNullOrWhiteSpace(request.RequestingUserId))
            {
                throw new UnauthorizedAccessException("User context is required.");
            }

            bool canDelete;
            if (request.IsAdmin)
            {
                canDelete = true;
            }
            else if (request.IsManager)
            {
                // Managers can only cancel their own leaves
                canDelete = annualLeave.EmployeeId == request.RequestingUserId;
            }
            else
            {
                // Employees can only cancel their own pending leaves
                canDelete = annualLeave.EmployeeId == request.RequestingUserId
                    && annualLeave.Status == AnnualLeaveStatus.Pending;
            }

            if (!canDelete)
            {
                throw new UnauthorizedAccessException("You are not allowed to cancel this leave request.");
            }

            var employeeProfile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.Id == annualLeave.EmployeeProfileId, cancellationToken);

            context.Remove(annualLeave);

            await context.SaveChangesAsync(cancellationToken);

            if (employeeProfile is not null)
            {
                await AnnualLeaveBalanceCalculator.SyncCurrentYearBalanceAsync(context, employeeProfile, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
