using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Support;

/// <summary>
/// The Type, Component and Activity a new row takes when nobody is asked: the one
/// option its project leaves, otherwise none. The server mirror of
/// <c>fillSingleOptions</c> in <c>client/src/lib/timesheet-columns.ts</c> — keep the
/// two in step. A type is the project's own (an unclassified project leaves none);
/// a component or activity is the project's own, or the whole active catalogue for
/// a project that declared none.
/// </summary>
public static class TimesheetEntryDefaults
{
    public static async Task<(int? ProjectTypeId, int? ProjectComponentId, int? ActivityTypeId)> SingleOptionsAsync(
        AppDbContext context, int projectId, CancellationToken cancellationToken)
    {
        var types = await context.ProjectTypeAssignments
            .Where(a => a.ProjectId == projectId)
            .Join(context.ProjectTypes.Where(t => t.IsActive), a => a.ProjectTypeId, t => t.Id, (a, t) => t.Id)
            .ToListAsync(cancellationToken);

        var declaredComponents = await context.ProjectComponentAssignments
            .Where(a => a.ProjectId == projectId).Select(a => a.ComponentId).ToListAsync(cancellationToken);
        var components = await context.ProjectComponents
            .Where(c => c.IsActive && (declaredComponents.Count == 0 || declaredComponents.Contains(c.Id)))
            .Select(c => c.Id).ToListAsync(cancellationToken);

        var assignedActivities = await context.ProjectActivityAssignments
            .Where(a => a.ProjectId == projectId).Select(a => a.ActivityTypeId).ToListAsync(cancellationToken);
        var activities = await context.ProjectActivityTypes
            .Where(a => a.IsActive && (assignedActivities.Count == 0 || assignedActivities.Contains(a.Id)))
            .Select(a => a.Id).ToListAsync(cancellationToken);

        return (Single(types), Single(components), Single(activities));
    }

    private static int? Single(List<int> ids) => ids.Count == 1 ? ids[0] : null;
}
