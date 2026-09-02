using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectTypes.Commands;

public class DeleteProjectType
{
    public class Command : IRequest<Result<Unit>>
    {
        public int Id { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var projectType = await context.ProjectTypes.FindAsync([request.Id], cancellationToken);
            if (projectType is null)
                return Result<Unit>.Failure("Project type not found.");

            // Refused while projects carry it, as DeleteProjectActivityType is.
            // A component assignment cascades away harmlessly; a type is the
            // classification an admin chose, so the choice is to refuse rather than
            // silently reclassify every project holding it.
            // The FK is Restrict too, in case anything reaches the delete without
            // passing here. The count goes in the message: knowing how many
            // projects to reassign first is the whole point of the refusal.
            var inUse = await context.ProjectTypeAssignments.CountAsync(a => a.ProjectTypeId == request.Id, cancellationToken);
            if (inUse > 0)
                return Result<Unit>.Conflict(
                    $"Cannot delete project type because {inUse} project{(inUse == 1 ? "" : "s")} {(inUse == 1 ? "uses" : "use")} it.");

            context.ProjectTypes.Remove(projectType);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
