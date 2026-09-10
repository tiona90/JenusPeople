using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
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

            // Checked before "in use", because being seeded is the more fundamental
            // reason and holds even for a type nobody has requested yet: seeding does
            // not restore a deleted type, so the app would come up missing one with
            // nothing offering to recreate it.
            if (SystemLeaveTypes.IsSystem(leaveType.Name))
                return Result<Unit>.Conflict($"{leaveType.Name} is a built-in leave type and cannot be deleted.");

            var inUse = await context.AnnualLeaves.AnyAsync(al => al.LeaveTypeId == request.Id, cancellationToken);
            if (inUse)
                return Result<Unit>.Conflict("Cannot delete leave type because it is used by leave requests.");

            context.LeaveTypes.Remove(leaveType);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
