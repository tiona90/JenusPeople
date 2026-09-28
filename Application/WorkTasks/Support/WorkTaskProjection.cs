using Application.WorkTasks.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

public static class WorkTaskProjection
{
    public static IQueryable<WorkTaskDto> Project(IQueryable<WorkTask> tasks, string callerUserId) =>
        tasks.Select(t => new WorkTaskDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            DepartmentId = t.DepartmentId,
            DepartmentName = t.Department!.Name,
            AssigneeId = t.AssigneeId,
            AssigneeName = !string.IsNullOrWhiteSpace(t.Assignee!.DisplayName) ? t.Assignee.DisplayName : (t.Assignee.Email ?? ""),
            CreatedById = t.CreatedById,
            CreatedByName = !string.IsNullOrWhiteSpace(t.CreatedBy!.DisplayName) ? t.CreatedBy.DisplayName : (t.CreatedBy.Email ?? ""),
            DueDate = t.DueDate,
            Priority = t.Priority,
            Status = t.Status,
            CreatedAtUtc = t.CreatedAtUtc,
            UpdatedAtUtc = t.UpdatedAtUtc,
            CompletedAtUtc = t.CompletedAtUtc,
            CanEdit = t.CreatedById == callerUserId,
            CanChangeStatus = t.CreatedById == callerUserId || t.AssigneeId == callerUserId,
        });

    public static Task<WorkTaskDto> LoadDtoAsync(
        AppDbContext context, int id, string callerUserId, CancellationToken cancellationToken) =>
        Project(context.WorkTasks.AsNoTracking().Where(t => t.Id == id), callerUserId)
            .SingleAsync(cancellationToken);

    /// <summary>Open first, then soonest due (undated last), then highest priority, then oldest.</summary>
    public static List<WorkTaskDto> Sort(IEnumerable<WorkTaskDto> tasks) => tasks
        .OrderBy(t => t.Status is WorkTaskStatus.Done or WorkTaskStatus.Cancelled ? 1 : 0)
        .ThenBy(t => t.DueDate is null ? 1 : 0)
        .ThenBy(t => t.DueDate)
        .ThenByDescending(t => t.Priority)
        .ThenBy(t => t.CreatedAtUtc)
        .ToList();
}
