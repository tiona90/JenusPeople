using Application.WorkTasks.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

public static class WorkTaskProjection
{
    /// <param name="callerManages">
    /// False for an Employee: they never create, edit or delete, so CanEdit is off
    /// whoever created the task — a deactivated creator's takeover is for Managers and HR.
    /// </param>
    public static IQueryable<WorkTaskDto> Project(IQueryable<WorkTask> tasks, string callerUserId, bool callerManages = true) =>
        tasks.Select(t => new WorkTaskDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            DepartmentId = t.DepartmentId,
            DepartmentName = t.Department!.Name,
            ProjectId = t.ProjectId,
            ProjectName = t.Project != null ? t.Project.Name : null,
            ProjectCode = t.Project != null ? t.Project.Code : null,
            ProjectColorKey = t.Project != null ? t.Project.ColorKey : null,
            Assignees = t.Assignees
                .Select(a => new WorkTaskAssigneeDto
                {
                    UserId = a.UserId,
                    DisplayName = !string.IsNullOrWhiteSpace(a.User!.DisplayName) ? a.User.DisplayName : (a.User.Email ?? ""),
                    IsHrAdministrator = a.User.UserRoles.Any(ur => ur.Role!.Name == AppRoles.HrAdministrator),
                })
                .OrderBy(a => a.DisplayName)
                .ToList(),
            CreatedById = t.CreatedById,
            CreatedByName = !string.IsNullOrWhiteSpace(t.CreatedBy!.DisplayName) ? t.CreatedBy.DisplayName : (t.CreatedBy.Email ?? ""),
            DueDate = t.DueDate,
            TargetHours = t.TargetHours,
            IsBillable = t.IsBillable,
            Priority = t.Priority,
            Status = t.Status,
            CreatedAtUtc = t.CreatedAtUtc,
            UpdatedAtUtc = t.UpdatedAtUtc,
            CompletedAtUtc = t.CompletedAtUtc,
            // Mirrors WorkTaskAccess.CanManageAsync: an inactive creator opens the task to everyone in scope.
            CanEdit = callerManages && (t.CreatedById == callerUserId || !t.CreatedBy!.IsActive),
            CanChangeStatus = (callerManages && (t.CreatedById == callerUserId || !t.CreatedBy!.IsActive))
                || t.Assignees.Any(a => a.UserId == callerUserId),
        });

    public static Task<WorkTaskDto> LoadDtoAsync(
        AppDbContext context, int id, string callerUserId, CancellationToken cancellationToken, bool callerManages = true) =>
        Project(context.WorkTasks.AsNoTracking().Where(t => t.Id == id), callerUserId, callerManages)
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
