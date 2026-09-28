using Application.WorkTasks.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Support;

/// <summary>
/// Who a task in a department may be assigned to: an active Manager or HR
/// Administrator whose own scope covers it — the department on their profile, or
/// a UserDepartment row. The same two sources ManagerAccessScopeResolver reads, so
/// an assignee can always see the task they were given.
/// </summary>
public static class WorkTaskAssigneeRule
{
    public const string NotEligibleMessage =
        "The assignee must be an active Manager or HR Administrator who covers this department.";

    private static readonly List<string> EligibleRoles = [AppRoles.Manager, AppRoles.HrAdministrator];

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

    public static async Task<bool> IsEligibleAsync(
        AppDbContext context, string userId, int departmentId, CancellationToken cancellationToken) =>
        (await EligibleAsync(context, departmentId, cancellationToken)).Any(a => a.UserId == userId);
}
