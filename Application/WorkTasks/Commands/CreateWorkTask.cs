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
            if (!await WorkTaskAssigneeRule.IsEligibleAsync(context, input.AssigneeId, input.DepartmentId, cancellationToken))
                return Result<WorkTaskDto>.Invalid(WorkTaskAssigneeRule.NotEligibleMessage);

            var now = DateTime.UtcNow;
            var task = new WorkTask
            {
                Title = input.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
                DepartmentId = input.DepartmentId,
                AssigneeId = input.AssigneeId,
                CreatedById = request.CallerUserId,
                DueDate = input.DueDate,
                Priority = input.Priority,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            context.WorkTasks.Add(task);
            await context.SaveChangesAsync(cancellationToken);

            if (task.AssigneeId != request.CallerUserId)
                await WorkTaskAssignmentNotification.SendAsync(context, emailService, logger, task, cancellationToken);

            return Result<WorkTaskDto>.Success(
                await WorkTaskProjection.LoadDtoAsync(context, task.Id, request.CallerUserId, cancellationToken));
        }
    }
}
