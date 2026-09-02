using Application.Projects.DTOs;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Support;

/// <summary>
/// Reads back the activity types assigned to a single project, for the DTO the
/// create and update commands return. The list query loads all projects'
/// assignments in one batch instead — this shape is only worth it for one.
/// </summary>
public static class ProjectActivityLookup
{
    public static async Task<List<ProjectActivityDto>> ForProjectAsync(
        AppDbContext context,
        int projectId,
        CancellationToken cancellationToken)
    {
        return await context.ProjectActivityAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.ActivityType!.Name)
            .Select(a => new ProjectActivityDto
            {
                Id = a.ActivityTypeId,
                Name = a.ActivityType!.Name,
                Icon = a.ActivityType.Icon,
                ColorKey = a.ActivityType.ColorKey,
            })
            .ToListAsync(cancellationToken);
    }
}
