using Application.Projects.DTOs;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Support;

/// <summary>
/// Reads one project's departments back for the create/update response, so both
/// commands return the same shape the list query does. The sibling of
/// <see cref="ProjectActivityLookup"/>.
/// </summary>
public static class ProjectDepartmentLookup
{
    public static async Task<List<ProjectDepartmentDto>> ForProjectAsync(
        AppDbContext context, int projectId, CancellationToken cancellationToken)
    {
        return await context.ProjectDepartments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.Department!.Name)
            .Select(a => new ProjectDepartmentDto
            {
                Id = a.DepartmentId,
                Name = a.Department!.Name,
            })
            .ToListAsync(cancellationToken);
    }
}
