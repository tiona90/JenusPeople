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

    /// <summary>
    /// Whether the caller may edit, reassign or delete the task: its creator — or,
    /// once the creator's account is deactivated, anyone who can see it. A leaver is
    /// deactivated rather than deleted, so without this their tasks would freeze with
    /// nobody able to touch them.
    /// </summary>
    /// <param name="assignedOnly">
    /// An Employee: their own tasks only. The deactivated-creator takeover is for
    /// Managers and HR Administrators, who run the department's tasks.
    /// </param>
    public static async Task<bool> CanManageAsync(
        AppDbContext context, WorkTask task, string callerUserId, CancellationToken cancellationToken, bool assignedOnly = false) =>
        task.CreatedById == callerUserId
        || (!assignedOnly && !await context.Users.AnyAsync(u => u.Id == task.CreatedById && u.IsActive, cancellationToken));

    /// <param name="assignedOnly">An Employee's view: of the tasks in scope, only the ones they are on or created.</param>
    public static async Task<WorkTask?> FindVisibleAsync(
        AppDbContext context, int id, string callerUserId, CancellationToken cancellationToken, bool assignedOnly = false)
    {
        var departmentIds = await DepartmentIdsAsync(context, callerUserId, cancellationToken);
        return await context.WorkTasks
            .Include(t => t.Assignees)
            .FirstOrDefaultAsync(t => t.Id == id
                && departmentIds.Contains(t.DepartmentId)
                && (!assignedOnly || t.CreatedById == callerUserId || t.Assignees.Any(a => a.UserId == callerUserId)), cancellationToken);
    }
}
