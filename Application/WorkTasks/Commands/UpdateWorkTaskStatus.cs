using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks.Commands;

public class UpdateWorkTaskStatus
{
    public class Command : IRequest<Result<WorkTaskDto>>
    {
        public int Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public WorkTaskStatus Status { get; set; }

        /// <summary>Why a reviewer sends a waiting task back. Required then, ignored otherwise.</summary>
        public string? Reason { get; set; }

        /// <summary>An Employee: only a task they are on or created is visible.</summary>
        public bool AssignedOnly { get; set; }
    }

    public class Handler(AppDbContext context, IEmailService emailService, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<WorkTaskDto>>
    {
        public async Task<Result<WorkTaskDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.Id, request.CallerUserId, cancellationToken, request.AssignedOnly);
            if (task is null)
                return Result<WorkTaskDto>.Failure(WorkTaskAccess.NotFoundMessage);

            var waiting = task.Status == WorkTaskStatus.AwaitingConfirmation;
            var isReviewer = WorkTaskReviewRule.IsReviewer(task, request.CallerUserId, request.AssignedOnly);
            var participates = task.Assignees.Any(a => a.UserId == request.CallerUserId)
                || await WorkTaskAccess.CanManageAsync(context, task, request.CallerUserId, cancellationToken, request.AssignedOnly);
            // A reviewer who is neither on the task nor its manager acts on it only once it waits for them.
            if (!participates && !(waiting && isReviewer))
                return Result<WorkTaskDto>.Forbidden(WorkTaskAccess.NotParticipantMessage);

            if (request.Status == WorkTaskStatus.AwaitingConfirmation && !waiting)
                return Result<WorkTaskDto>.Failure(WorkTaskReviewRule.StageIsDerivedMessage);

            var target = request.Status;
            var sendingBack = false;
            if (waiting && !isReviewer)
            {
                // The assignee's Done is already given: a repeat moves nothing. They may
                // take the task back to In progress; everything else is the reviewer's.
                if (target is WorkTaskStatus.Done or WorkTaskStatus.AwaitingConfirmation)
                    return await DtoAsync(task.Id, request, cancellationToken);
                if (target != WorkTaskStatus.InProgress)
                    return Result<WorkTaskDto>.Failure(WorkTaskReviewRule.AwaitingConfirmationMessage);
            }
            else if (waiting && (target is WorkTaskStatus.ToDo or WorkTaskStatus.InProgress))
            {
                sendingBack = true;
            }
            else if (target == WorkTaskStatus.Done && !waiting && !isReviewer
                && WorkTaskReviewRule.NeedsConfirmation(task)
                && await WorkTaskReviewRule.AnyReviewerAsync(context, task, cancellationToken))
            {
                target = WorkTaskStatus.AwaitingConfirmation;
            }

            string? reason = null;
            if (sendingBack)
            {
                reason = request.Reason?.Trim();
                if (string.IsNullOrEmpty(reason))
                    return Result<WorkTaskDto>.Failure(WorkTaskReviewRule.SendBackReasonRequiredMessage);
                if (reason.Length > WorkTask.SentBackReasonMaxLength)
                    return Result<WorkTaskDto>.Failure(WorkTaskReviewRule.SendBackReasonTooLongMessage);
            }

            if (task.Status != target)
            {
                var now = DateTime.UtcNow;
                if (target == WorkTaskStatus.Done)
                {
                    task.CompletedAtUtc = now;
                    task.ConfirmedById = request.CallerUserId;
                    task.SentBackReason = null;
                    task.SentBackAtUtc = null;
                }
                else
                {
                    task.CompletedAtUtc = null;
                    task.ConfirmedById = null;
                }
                if (sendingBack)
                {
                    task.SentBackReason = reason;
                    task.SentBackAtUtc = now;
                }
                task.Status = target;
                task.UpdatedAtUtc = now;
                await context.SaveChangesAsync(cancellationToken);
            }

            return await DtoAsync(task.Id, request, cancellationToken);
        }

        private async Task<Result<WorkTaskDto>> DtoAsync(int id, Command request, CancellationToken cancellationToken) =>
            Result<WorkTaskDto>.Success(
                await WorkTaskProjection.LoadDtoAsync(context, id, request.CallerUserId, cancellationToken, callerManages: !request.AssignedOnly));
    }
}
