using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectActivityTypes.Commands;

public class DeleteProjectActivityType
{
    public class Command : IRequest<Result<Unit>>
    {
        public int Id { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var activityType = await context.ProjectActivityTypes.FindAsync([request.Id], cancellationToken);
            if (activityType is null)
                return Result<Unit>.Failure("Activity type not found.");

            var inUse = await context.TimesheetEntries.AnyAsync(e => e.ActivityTypeId == request.Id, cancellationToken);
            if (inUse)
                return Result<Unit>.Failure("Cannot delete activity type because it is used by timesheet entries.");

            context.ProjectActivityTypes.Remove(activityType);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
