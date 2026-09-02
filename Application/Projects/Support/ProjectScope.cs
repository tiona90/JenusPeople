using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Support;

/// <summary>
/// Which projects a caller may see: every project for an Admin, and otherwise the
/// projects sharing at least one department with them. A project belonging to no
/// department therefore reaches nobody, which is the rule rather than a special
/// case in it.
///
/// Expressed as a query filter rather than a yes/no check for the same reason
/// <see cref="Application.Timesheets.Support.TimesheetScope"/> is — so that
/// listing projects and any later single-project lookup cannot drift apart.
/// </summary>
public static class ProjectScope
{
    /// <summary>
    /// Narrows <paramref name="query"/> to the projects this caller may see.
    /// Async because resolving the caller's departments needs its own round trip.
    /// </summary>
    public static async Task<IQueryable<Project>> ApplyAsync(
        AppDbContext context,
        IQueryable<Project> query,
        string requestingUserId,
        bool isAdmin,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        if (isAdmin)
        {
            // Admins see all projects — including the department-less ones, which
            // would otherwise be unreachable and so unrepairable.
            return query;
        }

        var departmentIds = await DepartmentIdsForAsync(context, requestingUserId, isManager, cancellationToken);

        // No departments resolves to no projects on its own: the Any() below is
        // false for every project when the list is empty.
        return query.Where(p => p.DepartmentAssignments.Any(a => departmentIds.Contains(a.DepartmentId)));
    }

    /// <summary>
    /// The departments a caller counts as belonging to: the one on their employee
    /// profile, plus — for a manager — every department assigned to them through
    /// <see cref="UserDepartment"/>.
    ///
    /// That second half makes project visibility slightly wider than
    /// <c>TimesheetScope</c>, which resolves a manager's departments from their own
    /// profile alone. Deliberate: <c>UserDepartment</c> is the only place a
    /// manager's multi-department assignment is actually recorded, and a manager
    /// who cannot see a department's projects cannot review the time booked to
    /// them either.
    /// </summary>
    public static async Task<List<int>> DepartmentIdsForAsync(
        AppDbContext context,
        string requestingUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        var departmentIds = await context.EmployeeProfiles
            .Where(ep => ep.UserId == requestingUserId)
            .Select(ep => ep.DepartmentId)
            .ToListAsync(cancellationToken);

        if (isManager)
        {
            departmentIds.AddRange(await context.UserDepartments
                .Where(ud => ud.UserId == requestingUserId)
                .Select(ud => ud.DepartmentId)
                .ToListAsync(cancellationToken));
        }

        return departmentIds.Distinct().ToList();
    }
}
