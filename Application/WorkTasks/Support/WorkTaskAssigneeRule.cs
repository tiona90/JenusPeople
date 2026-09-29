using Application.WorkTasks.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

/// <summary>
/// Who a task in a department may be assigned to: an active Manager or Employee
/// whose own scope covers it — the department on their profile, or (for a
/// Manager) a UserDepartment row. An HR Administrator runs tasks but is never
/// handed one, by themselves or anybody else. The same two sources ManagerAccessScopeResolver reads, so
/// an assignee can always see the task they were given.
/// </summary>
public static class WorkTaskAssigneeRule
{
    public const string NotEligibleMessage =
        "Every assignee must be an active Manager or Employee in this department.";

    public const string HrNotAssignableMessage =
        "HR Administrators can't be assigned tasks. Take them off the task to save it.";

    public const string NobodyEligibleMessage =
        "Nobody in this department can be assigned the task. Add an active Manager or Employee to it first.";

    // The people who do the work. An HR Administrator hands tasks out and a System
    // Administrator configures the workspace; neither is ever handed one.
    private static readonly List<string> EligibleRoles = [AppRoles.Manager, AppRoles.Employee];

    public static async Task<List<WorkTaskAssigneeDto>> EligibleAsync(
        AppDbContext context, int departmentId, CancellationToken cancellationToken)
    {
        var roleIds = await context.Roles
            .Where(r => r.Name != null && EligibleRoles.Contains(r.Name))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var covering = context.EmployeeProfiles
            .Where(ep => ep.DepartmentId == departmentId)
            .Select(ep => ep.UserId)
            .Concat(context.UserDepartments
                .Where(ud => ud.DepartmentId == departmentId)
                .Select(ud => ud.UserId));

        var people = await (
                from ur in context.UserRoles
                where roleIds.Contains(ur.RoleId)
                join u in context.Users on ur.UserId equals u.Id
                where u.IsActive && covering.Contains(u.Id)
                select new { u.Id, u.DisplayName, u.Email })
            .Distinct()
            .ToListAsync(cancellationToken);

        return people
            .Select(p => new WorkTaskAssigneeDto
            {
                UserId = p.Id,
                DisplayName = !string.IsNullOrWhiteSpace(p.DisplayName) ? p.DisplayName : p.Email ?? p.Id,
            })
            .OrderBy(a => a.DisplayName)
            .ToList();
    }

    /// <summary>
    /// The assignee list a save stores. An empty list means everyone in the department:
    /// it is expanded here to every eligible person at the moment of saving — a snapshot,
    /// so somebody who joins the department later is not added, and anyone may be taken
    /// off afterwards like any other assignee. Empty again when the department has nobody
    /// eligible; the caller refuses that with <see cref="NobodyEligibleMessage"/>.
    /// </summary>
    public static async Task<List<string>> ResolveAsync(
        AppDbContext context, IReadOnlyCollection<string> userIds, int departmentId, CancellationToken cancellationToken) =>
        userIds.Count > 0
            ? [.. userIds]
            : (await EligibleAsync(context, departmentId, cancellationToken)).Select(a => a.UserId).ToList();

    /// <summary>
    /// Whether any of <paramref name="userIds"/> holds the HR Administrator role. Checked
    /// against the whole list on every save, not just the people being added: unlike
    /// somebody who has since moved department, an HR Administrator on a task is never
    /// right, so an edit may not carry one forward.
    /// </summary>
    public static async Task<bool> AnyHrAdministratorAsync(
        AppDbContext context, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return false;
        var ids = userIds.ToList();
        return await (
            from ur in context.UserRoles
            join r in context.Roles on ur.RoleId equals r.Id
            where r.Name == AppRoles.HrAdministrator && ids.Contains(ur.UserId)
            select ur.UserId).AnyAsync(cancellationToken);
    }

    /// <summary>Every one of <paramref name="userIds"/> is eligible (vacuously true for none).</summary>
    public static async Task<bool> AllEligibleAsync(
        AppDbContext context, IReadOnlyCollection<string> userIds, int departmentId, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return true;
        var eligible = (await EligibleAsync(context, departmentId, cancellationToken)).Select(a => a.UserId).ToHashSet();
        return userIds.All(eligible.Contains);
    }

    public static async Task<bool> IsEligibleAsync(
        AppDbContext context, string userId, int departmentId, CancellationToken cancellationToken) =>
        (await EligibleAsync(context, departmentId, cancellationToken)).Any(a => a.UserId == userId);
}
