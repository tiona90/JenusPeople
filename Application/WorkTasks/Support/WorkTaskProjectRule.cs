using Application.WorkTasks.DTOs;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

/// <summary>
/// Which projects a task in a department may name: active ones assigned to that
/// department through ProjectDepartment. Soft-deleted projects are already gone
/// through the Projects query filter. "Active" is Project.IsActive, which an
/// On Hold project still is — the same reading the timesheet picker uses.
/// </summary>
public static class WorkTaskProjectRule
{
    public const string NotAvailableMessage =
        "Choose an active project assigned to this department.";

    public static Task<List<WorkTaskProjectDto>> AvailableAsync(
        AppDbContext context, int departmentId, CancellationToken cancellationToken) =>
        context.Projects
            .AsNoTracking()
            .Where(p => p.IsActive && p.DepartmentAssignments.Any(pd => pd.DepartmentId == departmentId))
            .OrderBy(p => p.Name)
            .Select(p => new WorkTaskProjectDto { Id = p.Id, Name = p.Name, Code = p.Code })
            .ToListAsync(cancellationToken);

    public static Task<bool> IsAvailableAsync(
        AppDbContext context, int projectId, int departmentId, CancellationToken cancellationToken) =>
        context.Projects.AnyAsync(
            p => p.Id == projectId && p.IsActive && p.DepartmentAssignments.Any(pd => pd.DepartmentId == departmentId),
            cancellationToken);
}
