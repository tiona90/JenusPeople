using Application.Projects.DTOs;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Support;

/// <summary>
/// Reads back the types a single project is classified as, for the DTO the
/// create and update commands return. The list query loads every project's
/// assignments in one batch instead — this shape is only worth it for one.
/// </summary>
public static class ProjectTypeLookup
{
    public static async Task<List<ProjectTypeSummaryDto>> ForProjectAsync(
        AppDbContext context,
        int projectId,
        CancellationToken cancellationToken)
    {
        return await context.ProjectTypeAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.ProjectType!.Name)
            .Select(a => new ProjectTypeSummaryDto
            {
                Id = a.ProjectTypeId,
                Name = a.ProjectType!.Name,
                Icon = a.ProjectType.Icon,
                ColorKey = a.ProjectType.ColorKey,
            })
            .ToListAsync(cancellationToken);
    }
}
