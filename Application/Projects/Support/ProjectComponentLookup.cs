using Application.Projects.DTOs;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Support;

/// <summary>
/// Reads back the components declared on a single project, for the DTO the
/// create and update commands return. The list query loads every project's
/// assignments in one batch instead — this shape is only worth it for one.
/// </summary>
public static class ProjectComponentLookup
{
    public static async Task<List<ProjectComponentSummaryDto>> ForProjectAsync(
        AppDbContext context,
        int projectId,
        CancellationToken cancellationToken)
    {
        return await context.ProjectComponentAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.Component!.Name)
            .Select(a => new ProjectComponentSummaryDto
            {
                Id = a.ComponentId,
                Name = a.Component!.Name,
                Icon = a.Component.Icon,
                ColorKey = a.Component.ColorKey,
            })
            .ToListAsync(cancellationToken);
    }
}
