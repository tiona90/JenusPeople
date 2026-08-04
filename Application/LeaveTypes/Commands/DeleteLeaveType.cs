using Application.Core;
using MediatR;
using Persistence;

namespace Application.LeaveTypes.Commands;

public class DeleteLeaveType
{
    public class Command : IRequest<Result<Unit>>
    {
        public int Id { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var leaveType = await context.LeaveTypes.FindAsync([request.Id], cancellationToken);
            if (leaveType is null)
                return Result<Unit>.Failure("Leave type not found.");

            var inUse = context.AnnualLeaves.Any(al => al.LeaveTypeId == request.Id);
            if (inUse)
                return Result<Unit>.Conflict("Cannot delete leave type because it is used by leave requests.");

            context.LeaveTypes.Remove(leaveType);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
