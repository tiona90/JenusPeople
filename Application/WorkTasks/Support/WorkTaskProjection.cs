using Application.Files;
using Application.WorkTasks.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

public static class WorkTaskProjection
{
    /// <param name="callerManages">
    /// False for an Employee: they edit and delete only the tasks they created
    /// themselves — a deactivated creator's takeover is for Managers and HR.
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
            CanEdit = t.CreatedById == callerUserId || (callerManages && !t.CreatedBy!.IsActive),
            CanChangeStatus = t.CreatedById == callerUserId || (callerManages && !t.CreatedBy!.IsActive)
                || t.Assignees.Any(a => a.UserId == callerUserId),
            // Never the bytes: only the file's columns are selected.
            Attachments = t.Attachments
                .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
                .Select(a => new WorkTaskAttachmentDto
                {
                    Id = a.Id,
                    FileName = a.StoredFile!.FileName,
                    ContentType = a.StoredFile.ContentType,
                    SizeBytes = a.StoredFile.SizeBytes,
                    Url = StoredFilePath.Prefix + a.StoredFileId,
                    UploadedByName = !string.IsNullOrWhiteSpace(a.StoredFile.UploadedBy!.DisplayName)
                        ? a.StoredFile.UploadedBy.DisplayName : (a.StoredFile.UploadedBy.Email ?? ""),
                    CreatedAtUtc = a.CreatedAtUtc,
                    // The uploader here; whoever may edit the task is folded in by
                    // WithLoggedHoursAsync, since a reference back to the task from
                    // inside this list needs an APPLY that SQLite cannot run.
                    CanRemove = a.StoredFile.UploadedById == callerUserId,
                })
                .ToList(),
        });

    public static async Task<WorkTaskDto> LoadDtoAsync(
        AppDbContext context, int id, string callerUserId, CancellationToken cancellationToken, bool callerManages = true)
    {
        var dto = await Project(context.WorkTasks.AsNoTracking().Where(t => t.Id == id), callerUserId, callerManages)
            .SingleAsync(cancellationToken);
        return (await WithLoggedHoursAsync(context, [dto], cancellationToken))[0];
    }

    /// <summary>
    /// Logged hours per task. Summed in memory, as GetProjectList does, because
    /// SQLite (the tests' provider) cannot aggregate a decimal column.
    /// </summary>
    public static async Task<Dictionary<int, decimal>> LoggedHoursAsync(
        AppDbContext context, IReadOnlyList<int> taskIds, CancellationToken cancellationToken)
    {
        var rows = await context.TimesheetEntries
            .AsNoTracking()
            .Where(e => e.WorkTaskId != null && taskIds.Contains(e.WorkTaskId.Value))
            .Select(e => new { TaskId = e.WorkTaskId!.Value, e.HoursWorked })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.Sum(r => r.HoursWorked));
    }

    public static async Task<List<WorkTaskDto>> WithLoggedHoursAsync(
        AppDbContext context, List<WorkTaskDto> tasks, CancellationToken cancellationToken)
    {
        var logged = await LoggedHoursAsync(context, tasks.Select(t => t.Id).ToList(), cancellationToken);
        foreach (var task in tasks)
        {
            task.LoggedHours = logged.GetValueOrDefault(task.Id);
            // Mirrors RemoveWorkTaskAttachment: the uploader, or whoever may edit the task.
            foreach (var attachment in task.Attachments) attachment.CanRemove |= task.CanEdit;
        }
        return tasks;
    }

    /// <summary>Open first, then soonest due (undated last), then highest priority, then oldest.</summary>
    public static List<WorkTaskDto> Sort(IEnumerable<WorkTaskDto> tasks) => tasks
        .OrderBy(t => t.Status is WorkTaskStatus.Done or WorkTaskStatus.Cancelled ? 1 : 0)
        .ThenBy(t => t.DueDate is null ? 1 : 0)
        .ThenBy(t => t.DueDate)
        .ThenByDescending(t => t.Priority)
        .ThenBy(t => t.CreatedAtUtc)
        .ToList();
}
