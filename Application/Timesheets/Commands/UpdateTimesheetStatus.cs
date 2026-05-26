using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Commands;

public class UpdateTimesheetStatus
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public required TimesheetStatus NewStatus { get; set; }
        public required string RequestingUserId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.NewStatus is not (TimesheetStatus.Approved or TimesheetStatus.Rejected))
            {
                return Result<Unit>.ValidationFailure(
                    new Dictionary<string, string[]>
                    {
                        ["NewStatus"] = ["Only Approved or Rejected transitions are supported by this command."]
                    },
                    "Invalid timesheet status transition.");
            }

            var timesheet = await context.Timesheets.FindAsync([request.Id], cancellationToken);
            if (timesheet is null)
            {
                return Result<Unit>.Failure("Timesheet not found.");
            }

            if (!request.IsAdmin)
            {
                if (!request.IsManager)
                {
                    return Result<Unit>.ValidationFailure(
                        new Dictionary<string, string[]>
                        {
                            ["Authorization"] = ["You are not authorized to update this timesheet."]
                        },
                        "You are not authorized to update this timesheet.");
                }

                var scope = await ManagerAccessScopeResolver.ResolveAsync(
                    context, request.RequestingUserId, cancellationToken);

                var inScope = scope.ManagedDepartmentIds.Contains(timesheet.DepartmentId);
                if (!inScope && scope.ManagerProfileIds.Count > 0)
                {
                    inScope = await context.EmployeeProfiles
                        .AsNoTracking()
                        .AnyAsync(ep =>
                            ep.Id == timesheet.EmployeeId
                            && ep.ManagerId != null
                            && scope.ManagerProfileIds.Contains(ep.ManagerId),
                            cancellationToken);
                }

                if (!inScope)
                {
                    return Result<Unit>.ValidationFailure(
                        new Dictionary<string, string[]>
                        {
                            ["Authorization"] = ["You are not authorized to update timesheets outside your departments."]
                        },
                        "You are not authorized to update this timesheet.");
                }
            }

            timesheet.Status = request.NewStatus;
            if (request.NewStatus == TimesheetStatus.Approved)
            {
                timesheet.ApprovedAt = DateTime.UtcNow;
                timesheet.ApproverId = request.RequestingUserId;
            }

            await context.SaveChangesAsync(cancellationToken);
            return Result<Unit>.Success(Unit.Value);
        }
    }
}
