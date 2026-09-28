using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks.Commands;

/// <summary>
/// The creator's full replace of a task's details. Eligibility is re-checked only
/// when the department or the assignee changes: an assignee who has since left the
/// department must not stop the creator fixing a typo in the title.
/// </summary>
public class UpdateWorkTask
{
    public class Command : IRequest<Result<WorkTaskDto>>
    {
        public int Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public required UpsertWorkTaskRequest Task { get; set; }
    }

    public class Handler(AppDbContext context, IEmailService emailService, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<WorkTaskDto>>
    {
        public async Task<Result<WorkTaskDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.Id, request.CallerUserId, cancellationToken);
            if (task is null)
                return Result<WorkTaskDto>.Failure(WorkTaskAccess.NotFoundMessage);
            if (!await WorkTaskAccess.CanManageAsync(context, task, request.CallerUserId, cancellationToken))
                return Result<WorkTaskDto>.Forbidden(WorkTaskAccess.NotCreatorMessage);

            var input = request.Task;
            var departmentChanged = input.DepartmentId != task.DepartmentId;
            var assigneeChanged = input.AssigneeId != task.AssigneeId;

            if (departmentChanged)
            {
                var departmentIds = await WorkTaskAccess.DepartmentIdsAsync(context, request.CallerUserId, cancellationToken);
                if (!departmentIds.Contains(input.DepartmentId))
                    return Result<WorkTaskDto>.Invalid(WorkTaskAccess.DepartmentOutOfScopeMessage);
            }
            if ((departmentChanged || assigneeChanged)
                && !await WorkTaskAssigneeRule.IsEligibleAsync(context, input.AssigneeId, input.DepartmentId, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskAssigneeRule.NotEligibleMessage);

            task.Title = input.Title.Trim();
            task.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            task.DepartmentId = input.DepartmentId;
            task.AssigneeId = input.AssigneeId;
            task.DueDate = input.DueDate;
            task.Priority = input.Priority;
            task.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            if (assigneeChanged && task.AssigneeId != request.CallerUserId)
                await WorkTaskAssignmentNotification.SendAsync(context, emailService, logger, task, cancellationToken);

            return Result<WorkTaskDto>.Success(
                await WorkTaskProjection.LoadDtoAsync(context, task.Id, request.CallerUserId, cancellationToken));
        }
    }
}
