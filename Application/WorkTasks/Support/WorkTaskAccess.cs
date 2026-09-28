using Application.Core;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

/// <summary>
/// The one visibility rule: a task is visible when its department is in the
/// caller's scope. Anything else reads as not found — the API does not confirm
/// that a task outside your departments exists.
/// </summary>
public static class WorkTaskAccess
{
    public const string NotFoundMessage = "Task not found.";
    public const string DepartmentOutOfScopeMessage = "You can only create tasks for departments you cover.";
    public const string NotCreatorMessage = "Only the person who created this task can change it.";
    public const string NotParticipantMessage = "Only the task's creator or assignee can change its status.";

    public static async Task<List<int>> DepartmentIdsAsync(
        AppDbContext context, string callerUserId, CancellationToken cancellationToken) =>
        (await ManagerAccessScopeResolver.ResolveAsync(context, callerUserId, cancellationToken)).ManagedDepartmentIds;

    public static async Task<WorkTask?> FindVisibleAsync(
        AppDbContext context, int id, string callerUserId, CancellationToken cancellationToken)
    {
        var departmentIds = await DepartmentIdsAsync(context, callerUserId, cancellationToken);
        return await context.WorkTasks
            .FirstOrDefaultAsync(t => t.Id == id && departmentIds.Contains(t.DepartmentId), cancellationToken);
    }
}
