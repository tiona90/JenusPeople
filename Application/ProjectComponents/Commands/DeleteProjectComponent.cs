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

            // No in-use check, unlike DeleteProjectActivityType: a project's
            // component assignments cascade away with the component, and no
            // timesheet entry records one. Add a check here the moment one does.
            context.ProjectComponents.Remove(component);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
