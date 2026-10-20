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

    public const string TypeNotAvailableMessage =
        "Choose a type the project is classified as.";

    /// <summary>Mirrored by TaskDialog on the client.</summary>
    public const string TypeRequiredMessage = "Type is required.";

    /// <summary>
    /// A task on a project classified by at least one active type must name one of
    /// them. A project with none leaves nothing to choose, so asks nothing. Checked on
    /// every save, so a task filed before the rule is given a type on its next edit,
    /// as one predating required projects is given a project.
    /// </summary>
    public static async Task<bool> IsTypeMissingAsync(
        AppDbContext context, int? projectId, int? projectTypeId, CancellationToken cancellationToken) =>
        projectTypeId is null
        && projectId is { } id
        && await context.ProjectTypeAssignments.AnyAsync(
            a => a.ProjectId == id && a.ProjectType!.IsActive, cancellationToken);

    public static Task<List<WorkTaskProjectDto>> AvailableAsync(
        AppDbContext context, int departmentId, CancellationToken cancellationToken) =>
        context.Projects
            .AsNoTracking()
            .Where(p => p.IsActive && p.DepartmentAssignments.Any(pd => pd.DepartmentId == departmentId))
            .OrderBy(p => p.Name)
            .Select(p => new WorkTaskProjectDto
            {
                Id = p.Id,
                Name = p.Name,
                Code = p.Code,
                Types = p.TypeAssignments
                    .Where(a => a.ProjectType!.IsActive)
                    .OrderBy(a => a.ProjectType!.Name)
                    .Select(a => new WorkTaskProjectTypeDto { Id = a.ProjectTypeId, Name = a.ProjectType!.Name, Icon = a.ProjectType.Icon })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

    /// <summary>
    /// A task's type must be an active one its project carries, so the timesheet row
    /// that takes it passes TimesheetEntryValidator's own type check. No project, no type.
    /// </summary>
    public static async Task<bool> IsTypeAvailableAsync(
        AppDbContext context, int? projectId, int projectTypeId, CancellationToken cancellationToken) =>
        projectId is { } id
        && await context.ProjectTypeAssignments.AnyAsync(
            a => a.ProjectId == id && a.ProjectTypeId == projectTypeId && a.ProjectType!.IsActive,
            cancellationToken);

    public static Task<bool> IsAvailableAsync(
        AppDbContext context, int projectId, int departmentId, CancellationToken cancellationToken) =>
        context.Projects.AnyAsync(
            p => p.Id == projectId && p.IsActive && p.DepartmentAssignments.Any(pd => pd.DepartmentId == departmentId),
            cancellationToken);
}
