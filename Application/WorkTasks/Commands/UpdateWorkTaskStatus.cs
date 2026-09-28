using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Persistence;

namespace Application.WorkTasks.Commands;

public class UpdateWorkTaskStatus
{
    public class Command : IRequest<Result<WorkTaskDto>>
    {
        public int Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public WorkTaskStatus Status { get; set; }

        /// <summary>An Employee: only a task they are on is visible, and only as its assignee.</summary>
        public bool AssignedOnly { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<WorkTaskDto>>
    {
        public async Task<Result<WorkTaskDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.Id, request.CallerUserId, cancellationToken, request.AssignedOnly);
            if (task is null)
                return Result<WorkTaskDto>.Failure(WorkTaskAccess.NotFoundMessage);
            if (!task.Assignees.Any(a => a.UserId == request.CallerUserId)
                && !await WorkTaskAccess.CanManageAsync(context, task, request.CallerUserId, cancellationToken))
                return Result<WorkTaskDto>.Forbidden(WorkTaskAccess.NotParticipantMessage);

            if (task.Status != request.Status)
            {
                var now = DateTime.UtcNow;
                task.CompletedAtUtc = request.Status == WorkTaskStatus.Done ? now : null;
                task.Status = request.Status;
                task.UpdatedAtUtc = now;
                await context.SaveChangesAsync(cancellationToken);
            }

            return Result<WorkTaskDto>.Success(
                await WorkTaskProjection.LoadDtoAsync(context, task.Id, request.CallerUserId, cancellationToken, callerManages: !request.AssignedOnly));
        }
    }
}
