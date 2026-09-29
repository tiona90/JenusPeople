using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks.Commands;

public class CreateWorkTask
{
    public class Command : IRequest<Result<WorkTaskDto>>
    {
        public string CallerUserId { get; set; } = string.Empty;
        public required UpsertWorkTaskRequest Task { get; set; }

        /// <summary>
        /// An Employee: the task is their own, assigned to themselves whatever the
        /// request's assignee list says — an Employee hands nobody else work.
        /// </summary>
        public bool AssignedOnly { get; set; }
    }

    public class Handler(AppDbContext context, IEmailService emailService, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<WorkTaskDto>>
    {
        public async Task<Result<WorkTaskDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var input = request.Task;
            var departmentIds = await WorkTaskAccess.DepartmentIdsAsync(context, request.CallerUserId, cancellationToken);
            if (!departmentIds.Contains(input.DepartmentId))
                return Result<WorkTaskDto>.Invalid(WorkTaskAccess.DepartmentOutOfScopeMessage);
            if (input.ProjectId is not { } projectId
                || !await WorkTaskProjectRule.IsAvailableAsync(context, projectId, input.DepartmentId, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskProjectRule.NotAvailableMessage);
            var assigneeIds = request.AssignedOnly
                ? [request.CallerUserId]
                : await WorkTaskAssigneeRule.ResolveAsync(context, input.AssigneeIds, input.DepartmentId, cancellationToken);
            if (assigneeIds.Count == 0)
                return Result<WorkTaskDto>.Invalid(WorkTaskAssigneeRule.NobodyEligibleMessage);
            if (await WorkTaskAssigneeRule.AnyHrAdministratorAsync(context, assigneeIds, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskAssigneeRule.HrNotAssignableMessage);
            if (!await WorkTaskAssigneeRule.AllEligibleAsync(context, assigneeIds, input.DepartmentId, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskAssigneeRule.NotEligibleMessage);

            var now = DateTime.UtcNow;
            var task = new WorkTask
            {
                Title = input.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
                DepartmentId = input.DepartmentId,
                ProjectId = input.ProjectId,
                Assignees = [.. assigneeIds.Select(id => new WorkTaskAssignee { UserId = id })],
                CreatedById = request.CallerUserId,
                DueDate = input.DueDate,
                TargetHours = input.TargetHours,
                IsBillable = input.IsBillable,
                Priority = input.Priority,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            context.WorkTasks.Add(task);
            await context.SaveChangesAsync(cancellationToken);

            // Nobody is emailed about a task they assigned themselves.
            var recipients = assigneeIds.Where(id => id != request.CallerUserId).ToList();
            await WorkTaskAssignmentNotification.SendAsync(context, emailService, logger, task, recipients, cancellationToken);

            return Result<WorkTaskDto>.Success(
                await WorkTaskProjection.LoadDtoAsync(context, task.Id, request.CallerUserId, cancellationToken));
        }
    }
}
