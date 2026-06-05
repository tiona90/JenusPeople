using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Core;

public record ManagerContact(string UserId, string Email, string? DisplayName);

// Resolves who should be notified about an employee's submission (new leave
// request, timesheet submission, …).
//
// The app scopes a manager's visible team by DEPARTMENT (see ManagerAccessScope
// — a manager manages the department their own profile is in), but historically
// notifications only emailed the employee's direct EmployeeProfile.ManagerId.
// That left department managers (the common setup) un-notified. This resolver
// unifies both: the direct manager (if any) PLUS every Manager-role user in the
// employee's department, de-duplicated, with the employee themselves removed.
public static class ManagerNotificationRecipients
{
    public static async Task<List<ManagerContact>> ResolveAsync(
        AppDbContext context,
        EmployeeProfile employeeProfile,
        CancellationToken cancellationToken)
    {
        var byUserId = new Dictionary<string, ManagerContact>();

        // 1) Direct manager via EmployeeProfile.ManagerId.
        if (!string.IsNullOrWhiteSpace(employeeProfile.ManagerId))
        {
            var directManager = await context.EmployeeProfiles
                .Include(mp => mp.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(mp => mp.Id == employeeProfile.ManagerId, cancellationToken);

            if (directManager?.User is { } du && !string.IsNullOrWhiteSpace(du.Email))
                byUserId[du.Id] = new ManagerContact(du.Id, du.Email!, du.DisplayName);
        }

        // 2) Department managers: Manager-role users whose own profile is in the
        //    same department as the employee.
        var managerRoleId = await context.Roles
            .Where(r => r.Name == AppRoles.Manager)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (managerRoleId is not null)
        {
            var departmentManagers = await (
                from ep in context.EmployeeProfiles.AsNoTracking()
                where ep.DepartmentId == employeeProfile.DepartmentId
                join ur in context.UserRoles on ep.UserId equals ur.UserId
                where ur.RoleId == managerRoleId
                join u in context.Users on ep.UserId equals u.Id
                where u.Email != null && u.Email != ""
                select new { u.Id, u.Email, u.DisplayName }
            ).Distinct().ToListAsync(cancellationToken);

            foreach (var m in departmentManagers)
                byUserId[m.Id] = new ManagerContact(m.Id, m.Email!, m.DisplayName);
        }

        // Never notify the submitter about their own submission.
        byUserId.Remove(employeeProfile.UserId);

        return byUserId.Values.ToList();
    }
}
