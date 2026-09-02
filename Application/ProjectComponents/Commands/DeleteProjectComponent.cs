using Application.Core;
using MediatR;
using Persistence;

namespace Application.ProjectComponents.Commands;

public class DeleteProjectComponent
{
    public class Command : IRequest<Result<Unit>>
    {
        public int Id { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var component = await context.ProjectComponents.FindAsync([request.Id], cancellationToken);
            if (component is null)
                return Result<Unit>.Failure("Component not found.");

            // No in-use check, unlike DeleteProjectActivityType: nothing references
            // a component yet. Add one here the moment something does.
            context.ProjectComponents.Remove(component);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
