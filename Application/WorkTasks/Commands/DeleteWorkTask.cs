using Application.Core;
using Application.WorkTasks.Support;
using MediatR;
using Persistence;

namespace Application.WorkTasks.Commands;

/// <summary>Returns the department the task was in, so the controller can tell that department's pages to refresh.</summary>
public class DeleteWorkTask
{
    public class Command : IRequest<Result<int>>
    {
        public int Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<int>>
    {
        public async Task<Result<int>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.Id, request.CallerUserId, cancellationToken);
            if (task is null)
                return Result<int>.Failure(WorkTaskAccess.NotFoundMessage);
            if (task.CreatedById != request.CallerUserId)
                return Result<int>.Forbidden(WorkTaskAccess.NotCreatorMessage);

            context.WorkTasks.Remove(task);
            await context.SaveChangesAsync(cancellationToken);
            return Result<int>.Success(task.DepartmentId);
        }
    }
}
