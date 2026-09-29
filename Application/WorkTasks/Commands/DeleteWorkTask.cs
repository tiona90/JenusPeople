using Application.Core;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Commands;

/// <summary>Returns the department the task was in, so the controller can tell that department's pages to refresh.</summary>
public class DeleteWorkTask
{
    public class Command : IRequest<Result<int>>
    {
        public int Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;

        /// <summary>An Employee: only a task they created themselves.</summary>
        public bool AssignedOnly { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<int>>
    {
        public async Task<Result<int>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.Id, request.CallerUserId, cancellationToken, request.AssignedOnly);
            if (task is null)
                return Result<int>.Failure(WorkTaskAccess.NotFoundMessage);
            if (!await WorkTaskAccess.CanManageAsync(context, task, request.CallerUserId, cancellationToken, request.AssignedOnly))
                return Result<int>.Forbidden(WorkTaskAccess.NotCreatorMessage);

            // The attachment rows cascade with the task; the files would not, so they
            // go too — as stubs, never loading the bytes.
            var fileIds = await context.WorkTaskAttachments
                .Where(a => a.WorkTaskId == task.Id)
                .Select(a => a.StoredFileId)
                .ToListAsync(cancellationToken);
            foreach (var fileId in fileIds)
                context.StoredFiles.Remove(new StoredFile { Id = fileId });

            context.WorkTasks.Remove(task);
            await context.SaveChangesAsync(cancellationToken);
            return Result<int>.Success(task.DepartmentId);
        }
    }
}
