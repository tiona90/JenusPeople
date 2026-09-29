using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Commands;

/// <summary>
/// Takes a file off a task and deletes it: whoever attached it may, and so may
/// whoever may edit the task (<see cref="WorkTaskAccess.CanManageAsync"/>). The
/// stored file goes, and its attachment row with it by cascade.
/// </summary>
public class RemoveWorkTaskAttachment
{
    public const string NotFoundMessage = "Attachment not found.";
    public const string NotYoursMessage = "Only the person who attached this file, or who can edit the task, can remove it.";

    public class Command : IRequest<Result<WorkTaskDto>>
    {
        public int Id { get; set; }
        public int AttachmentId { get; set; }
        public string CallerUserId { get; set; } = string.Empty;

        /// <summary>An Employee: only a task they are on or created is visible.</summary>
        public bool AssignedOnly { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<WorkTaskDto>>
    {
        public async Task<Result<WorkTaskDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.Id, request.CallerUserId, cancellationToken, request.AssignedOnly);
            if (task is null)
                return Result<WorkTaskDto>.Failure(WorkTaskAccess.NotFoundMessage);

            var attachment = await context.WorkTaskAttachments
                .Where(a => a.Id == request.AttachmentId && a.WorkTaskId == task.Id)
                .Select(a => new { a.StoredFileId, a.StoredFile!.UploadedById })
                .FirstOrDefaultAsync(cancellationToken);
            if (attachment is null)
                return Result<WorkTaskDto>.Failure(NotFoundMessage);

            if (attachment.UploadedById != request.CallerUserId
                && !await WorkTaskAccess.CanManageAsync(context, task, request.CallerUserId, cancellationToken, request.AssignedOnly))
                return Result<WorkTaskDto>.Forbidden(NotYoursMessage);

            // Set-based, so the bytes are never loaded; the attachment row cascades.
            await context.StoredFiles.Where(f => f.Id == attachment.StoredFileId).ExecuteDeleteAsync(cancellationToken);

            return Result<WorkTaskDto>.Success(await WorkTaskProjection.LoadDtoAsync(
                context, task.Id, request.CallerUserId, cancellationToken, callerManages: !request.AssignedOnly));
        }
    }
}
