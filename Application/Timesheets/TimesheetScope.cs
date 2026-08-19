using Application.Core;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets;

/// <summary>
/// What a caller is allowed to *read*: every timesheet for an Admin; their own
/// plus their managed departments and direct reports for a Manager; their own
/// only for anyone else.
///
/// Expressed as a query filter rather than a yes/no check so that reading one
/// timesheet by id and listing them cannot drift apart — a single-row lookup
/// that reimplemented this rule is exactly how
/// <c>TimesheetsController.GetTimesheet</c> came to return anybody's timesheet.
/// The write-side counterpart is <see cref="TimesheetAccess"/>.
/// </summary>
public static class TimesheetScope
{
    /// <summary>
    /// Narrows <paramref name="query"/> to the timesheets this caller may see.
    /// Async because resolving a manager's scope needs its own round trip.
    /// </summary>
    public static async Task<IQueryable<Timesheet>> ApplyAsync(
        AppDbContext context,
        IQueryable<Timesheet> query,
        string requestingUserId,
        bool isAdmin,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        if (isAdmin)
        {
            // Admins see all timesheets — no filter.
            return query;
        }

        if (isManager)
        {
            var scope = await ManagerAccessScopeResolver.ResolveAsync(
                context, requestingUserId, cancellationToken);

            return query.Where(t =>
                // Own timesheets
                t.Employee!.UserId == requestingUserId
                // Timesheets in managed departments
                || scope.ManagedDepartmentIds.Contains(t.DepartmentId)
                // Direct reports' timesheets
                || scope.DirectReportUserIds.Contains(t.Employee.UserId));
        }

        // Employees see only their own timesheets. Timesheet.EmployeeProfileId is an
        // EmployeeProfile.Id, so the comparison has to walk Employee to reach the
        // AspNetUsers.Id the token carries.
        return query.Where(t => t.Employee!.UserId == requestingUserId);
    }
}
