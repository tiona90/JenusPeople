using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

/// <summary>
/// Whether a Done is the end of a task or a request to end it. A task somebody else
/// handed you waits in AwaitingConfirmation until a reviewer confirms it: its creator,
/// or a Manager or HR Administrator whose scope covers it and who is not on it —
/// nobody signs off their own work but the creator, who owns the task. A task whose
/// only assignee is its creator closes directly, and so does one with nobody left who
/// could review it, so a task never waits on nobody.
/// </summary>
public static class WorkTaskReviewRule
{
    public const string StageIsDerivedMessage =
        "A task waits for confirmation by being marked done; that status can't be chosen directly.";
    public const string AwaitingConfirmationMessage =
        "This task is waiting for confirmation. You can take it back to In progress, or wait for the reviewer.";
    public const string SendBackReasonRequiredMessage = "Say why the task is being sent back.";
    public static readonly string SendBackReasonTooLongMessage =
        $"The reason can be at most {WorkTask.SentBackReasonMaxLength} characters.";

    private static readonly List<string> ReviewerRoles = [AppRoles.Manager, AppRoles.HrAdministrator];

    /// <summary>Somebody other than the creator is on it. Needs <see cref="WorkTask.Assignees"/> loaded.</summary>
    public static bool NeedsConfirmation(WorkTask task) =>
        task.Assignees.Any(a => a.UserId != task.CreatedById);

    /// <summary>
    /// The caller may confirm or send back a task they can see. Visibility already
    /// proved scope; <paramref name="assignedOnly"/> (an Employee) reviews only what
    /// they created. Mirrored by <c>WorkTaskDto.CanConfirm</c> in WorkTaskProjection.
    /// </summary>
    public static bool IsReviewer(WorkTask task, string callerUserId, bool assignedOnly) =>
        task.CreatedById == callerUserId
        || (!assignedOnly && task.Assignees.All(a => a.UserId != callerUserId));

    /// <summary>The active Managers and HR Administrators covering the task's department who are not on it.</summary>
    public static async Task<List<string>> CoveringReviewerIdsAsync(
        AppDbContext context, WorkTask task, CancellationToken cancellationToken)
    {
        var roleIds = await context.Roles
            .Where(r => r.Name != null && ReviewerRoles.Contains(r.Name))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var covering = context.EmployeeProfiles
            .Where(ep => ep.DepartmentId == task.DepartmentId)
            .Select(ep => ep.UserId)
            .Concat(context.UserDepartments
                .Where(ud => ud.DepartmentId == task.DepartmentId)
                .Select(ud => ud.UserId));

        var assigneeIds = task.Assignees.Select(a => a.UserId).ToList();
        return await (
                from ur in context.UserRoles
                where roleIds.Contains(ur.RoleId)
                join u in context.Users on ur.UserId equals u.Id
                where u.IsActive && covering.Contains(u.Id) && !assigneeIds.Contains(u.Id)
                select u.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>Somebody could confirm it: the creator while active, or a covering reviewer.</summary>
    public static async Task<bool> AnyReviewerAsync(AppDbContext context, WorkTask task, CancellationToken cancellationToken) =>
        await context.Users.AnyAsync(u => u.Id == task.CreatedById && u.IsActive, cancellationToken)
        || (await CoveringReviewerIdsAsync(context, task, cancellationToken)).Count > 0;
}
