using System;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

public class DeleteAnnualLeave
{
    public const string DecidedRequestMessage =
        "Only a request still awaiting a decision can be withdrawn; an approved leave is cancelled instead.";

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
                // The HR Administrator, cancelling on somebody's behalf — inside their
                // assigned departments, or their own request.
                var scope = await ManagerAccessScopeResolver.ResolveAsync(context, request.RequestingUserId, cancellationToken);
                canDelete = annualLeave.EmployeeId == request.RequestingUserId
                    || (annualLeave.DepartmentId.HasValue && scope.ManagedDepartmentIds.Contains(annualLeave.DepartmentId.Value))
                    || scope.DirectReportUserIds.Contains(annualLeave.EmployeeId)
                    // A department-less leave is only ever an administrator's own —
                    // nobody else has no department — and an HR Administrator is who
                    // decided it before this task. Scoping it to nobody would strand
                    // it Pending forever.
                    || (request.IsAdmin && !annualLeave.DepartmentId.HasValue);
            }
            else if (request.IsManager)
            {
                // Managers can only cancel their own leaves
                canDelete = annualLeave.EmployeeId == request.RequestingUserId;
            }
            else
            {
                // Employees can only cancel their own open leaves (Pending or awaiting HR)
                canDelete = annualLeave.EmployeeId == request.RequestingUserId
                    && ApprovalStageRule.IsOpen(annualLeave.Status);
            }

            if (!canDelete)
            {
                return Result<Unit>.Failure("You are not allowed to cancel this leave request.");
            }

            // Deleting is withdrawing a request nobody has decided yet, for every
            // role. A decided one is taken back through its status (UpdateLeaveStatus
            // → Cancelled), which applies CancellationRule, writes history and tells
            // the delegate and the managers; deleting an approved leave instead would
            // hand days already taken back to the balance with no trace of them.
            if (!ApprovalStageRule.IsOpen(annualLeave.Status))
                return Result<Unit>.Failure(DecidedRequestMessage);

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
