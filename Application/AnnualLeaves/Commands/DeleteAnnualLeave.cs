using System;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

public class DeleteAnnualLeave
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }
    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var annualLeave = await context.AnnualLeaves
                .FindAsync([request.Id], cancellationToken);

            if (annualLeave is null)
                return Result<Unit>.Failure("Cannot find the annual leave.");

            if (string.IsNullOrWhiteSpace(request.RequestingUserId))
            {
                return Result<Unit>.Failure("User context is required.");
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
                return Result<Unit>.Failure("You are not allowed to cancel this leave request.");
            }

            var employeeProfile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.Id == annualLeave.EmployeeProfileId, cancellationToken);

            context.Remove(annualLeave);

            // One transaction over both saves: the balance sync reads approved leave
            // back out of the database, so it has to run after the delete is written
            // (otherwise it still counts this leave), and a failure on the second
            // write must not leave the balance stale against a leave that is gone.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            if (employeeProfile is not null)
            {
                await AnnualLeaveBalanceCalculator.SyncCurrentYearBalanceAsync(context, employeeProfile, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
