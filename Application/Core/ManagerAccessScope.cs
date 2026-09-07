using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Core;

public sealed class ManagerAccessScope
{
    public List<int> ManagedDepartmentIds { get; init; } = [];
    public List<string> ManagerProfileIds { get; init; } = [];
    public List<string> DirectReportUserIds { get; init; } = [];
}

public static class ManagerAccessScopeResolver
{
    public static async Task<ManagerAccessScope> ResolveAsync(
        AppDbContext context,
        string userId,
        CancellationToken cancellationToken)
    {
        // Only include the department(s) from the manager's own EmployeeProfile(s)
        var managerProfiles = await context.EmployeeProfiles
            .Where(ep => ep.UserId == userId)
            .Select(ep => new { ep.Id, ep.DepartmentId })
            .ToListAsync(cancellationToken);

        // A department-less profile (an Admin's) contributes nothing to manage.
        // Dropping the nulls here keeps ManagedDepartmentIds a list of real
        // departments, so every consumer can compare against it without a cast.
        var managedDepartmentIds = managerProfiles
            .Where(profile => profile.DepartmentId.HasValue)
            .Select(profile => profile.DepartmentId!.Value)
            .Distinct()
            .ToList();

        var managerProfileIds = managerProfiles
            .Select(profile => profile.Id)
            .ToList();

        var directReportUserIds = managerProfileIds.Count == 0
            ? []
            : await context.EmployeeProfiles
                .Where(ep => ep.ManagerId != null && managerProfileIds.Contains(ep.ManagerId))
                .Select(ep => ep.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

        return new ManagerAccessScope
        {
            ManagedDepartmentIds = managedDepartmentIds,
            ManagerProfileIds = managerProfileIds,
            DirectReportUserIds = directReportUserIds
        };
    }
}