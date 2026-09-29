using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks.Commands;

/// <summary>
/// The creator's full replace of a task's details, assignee list included.
/// Eligibility is checked for the people being added — or for everyone, when the
/// department changes — and the project only when the department or the project
/// changes: an assignee who has since left the department, or a project since
/// switched off, must not stop the creator fixing a typo in the title. Only the
/// newcomers are emailed. An empty assignee list is everyone in the department,
/// expanded at save time (<see cref="WorkTaskAssigneeRule.ResolveAsync"/>).
/// </summary>
public class UpdateWorkTask
{
    public class Command : IRequest<Result<WorkTaskDto>>
    {
        public int Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public required UpsertWorkTaskRequest Task { get; set; }

        /// <summary>
        /// An Employee: only a task they created, which stays assigned to them alone
        /// whatever the request's assignee list says.
        /// </summary>
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
            if (!await WorkTaskAccess.CanManageAsync(context, task, request.CallerUserId, cancellationToken, request.AssignedOnly))
                return Result<WorkTaskDto>.Forbidden(WorkTaskAccess.NotCreatorMessage);

            var input = request.Task;
            var departmentChanged = input.DepartmentId != task.DepartmentId;
            var assigneeIds = request.AssignedOnly
                ? [request.CallerUserId]
                : await WorkTaskAssigneeRule.ResolveAsync(context, input.AssigneeIds, input.DepartmentId, cancellationToken);
            if (assigneeIds.Count == 0)
                return Result<WorkTaskDto>.Invalid(WorkTaskAssigneeRule.NobodyEligibleMessage);
            var current = task.Assignees.Select(a => a.UserId).ToHashSet();
            var added = assigneeIds.Where(id => !current.Contains(id)).ToList();
            var projectChanged = input.ProjectId != task.ProjectId;

            if (departmentChanged)
            {
                var departmentIds = await WorkTaskAccess.DepartmentIdsAsync(context, request.CallerUserId, cancellationToken);
                if (!departmentIds.Contains(input.DepartmentId))
                    return Result<WorkTaskDto>.Invalid(WorkTaskAccess.DepartmentOutOfScopeMessage);
            }
            if ((departmentChanged || projectChanged)
                && (input.ProjectId is not { } projectId
                    || !await WorkTaskProjectRule.IsAvailableAsync(context, projectId, input.DepartmentId, cancellationToken)))
                return Result<WorkTaskDto>.Invalid(WorkTaskProjectRule.NotAvailableMessage);
            if (await WorkTaskAssigneeRule.AnyHrAdministratorAsync(context, assigneeIds, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskAssigneeRule.HrNotAssignableMessage);
            if (!await WorkTaskAssigneeRule.AllEligibleAsync(
                    context, departmentChanged ? assigneeIds : added, input.DepartmentId, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskAssigneeRule.NotEligibleMessage);

            task.Title = input.Title.Trim();
            task.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            task.DepartmentId = input.DepartmentId;
            task.ProjectId = input.ProjectId;
            var requested = assigneeIds.ToHashSet();
            foreach (var gone in task.Assignees.Where(a => !requested.Contains(a.UserId)).ToList())
                task.Assignees.Remove(gone);
            foreach (var id in added)
                task.Assignees.Add(new WorkTaskAssignee { UserId = id });
            task.DueDate = input.DueDate;
            task.TargetHours = input.TargetHours;
            task.IsBillable = input.IsBillable;
            task.Priority = input.Priority;
            task.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            await WorkTaskAssignmentNotification.SendAsync(
                context, emailService, logger, task, added.Where(id => id != request.CallerUserId).ToList(), cancellationToken);

            return Result<WorkTaskDto>.Success(
                await WorkTaskProjection.LoadDtoAsync(context, task.Id, request.CallerUserId, cancellationToken));
        }
    }
}
