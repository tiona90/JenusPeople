using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Commands;

public class DeleteProject
{
    public class Command : IRequest<Result<Unit>>
    {
        public int Id { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var project = await context.Projects.FindAsync([request.Id], cancellationToken);
            if (project is null)
                return Result<Unit>.Failure("Project not found.");

            var hasEntries = await context.TimesheetEntries.AnyAsync(e => e.ProjectId == request.Id, cancellationToken);
            if (hasEntries)
                return Result<Unit>.Failure("Cannot delete a project with logged timesheet entries. Set it to Inactive instead.");

            // The AuditingSaveChangesInterceptor converts physical deletes on
            // ISoftDeletable entities to IsDeleted = true and records an audit entry.
            context.Projects.Remove(project);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
