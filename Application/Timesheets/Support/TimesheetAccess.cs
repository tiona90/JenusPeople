using Application.Core;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Support;

/// <summary>
/// Who may modify a timesheet — and therefore its entries: the employee it
/// belongs to, a System Administrator, or a Manager whose scope covers that employee (their
/// department, or a direct report).
///
/// Lives here so every write enforces the identical rule rather than its own
/// variant. Both callers now do: the entry actions in
/// <c>TimesheetEntriesController</c>, and
/// <c>Application.Timesheets.Commands.DeleteTimesheet</c>, which used to apply a
/// narrower rule inline that recognised only the owning employee.
/// </summary>
public static class TimesheetAccess
{
    /// <summary>
    /// Resolves the timesheet and the caller's right to write to it.
    /// Success carries the (tracked) timesheet; <see cref="ResultErrorKind.NotFound"/>
    /// means no such timesheet, <see cref="ResultErrorKind.Forbidden"/> means it
    /// exists but is not the caller's to touch.
    /// </summary>
    /// <remarks>
    /// <paramref name="isHrAdministrator"/> sits after <paramref name="cancellationToken"/>
    /// on purpose: both are optional, and every existing positional caller supplies
    /// exactly six arguments (ending in the cancellation token), so appending the new
    /// parameter after it keeps those call sites compiling unchanged.
    /// </remarks>
    public static async Task<Result<Timesheet>> AuthorizeWriteAsync(
        AppDbContext context,
        string timesheetId,
        string requestingUserId,
        bool isAdmin,
        bool isManager,
        CancellationToken cancellationToken = default,
        bool isHrAdministrator = false)
    {
        var timesheet = await context.Timesheets
            .FirstOrDefaultAsync(t => t.Id == timesheetId, cancellationToken);

        if (timesheet is null)
        {
            return Result<Timesheet>.Failure("Timesheet not found.");
        }

        if (isAdmin)
        {
            return Result<Timesheet>.Success(timesheet);
        }

        // No resolvable caller means no ownership to establish. Guard explicitly
        // rather than letting an empty id match a profile with an unset UserId.
        if (string.IsNullOrWhiteSpace(requestingUserId))
        {
            return Forbidden();
        }

        var callerProfileIds = await context.EmployeeProfiles
            .AsNoTracking()
            .Where(ep => ep.UserId == requestingUserId)
            .Select(ep => ep.Id)
            .ToListAsync(cancellationToken);

        if (callerProfileIds.Contains(timesheet.EmployeeProfileId))
        {
            return Result<Timesheet>.Success(timesheet);
        }

        if (isManager)
        {
            var scope = await ManagerAccessScopeResolver.ResolveAsync(
                context, requestingUserId, cancellationToken);

            var inScope = timesheet.DepartmentId != null
                && scope.ManagedDepartmentIds.Contains(timesheet.DepartmentId.Value);
            if (!inScope && scope.ManagerProfileIds.Count > 0)
            {
                inScope = await context.EmployeeProfiles
                    .AsNoTracking()
                    .AnyAsync(ep =>
                        ep.Id == timesheet.EmployeeProfileId
                        && ep.ManagerId != null
                        && scope.ManagerProfileIds.Contains(ep.ManagerId),
                        cancellationToken);
            }

            // The HR Administrator also reaches a department-less timesheet — an
            // administrator's own — which no department scope would otherwise include.
            if (!inScope && timesheet.DepartmentId == null && isHrAdministrator)
            {
                inScope = true;
            }

            if (inScope)
            {
                return Result<Timesheet>.Success(timesheet);
            }
        }

        return Forbidden();
    }

    public const string NotEditableMessage =
        "This timesheet has been submitted and can no longer be changed.";

    /// <summary>Whether the sheet's entries may be written: Draft or Rejected only.</summary>
    /// <remarks>
    /// Submitted, Resubmitted and Approved weeks are locked for everyone: hours
    /// change after submission only by going back through a rejection
    /// (UpdateTimesheetStatus, or HR's ReopenTimesheet), so what a reviewer signed
    /// off is what stays. Mirrors LogTaskTime's WeekSubmittedMessage and the
    /// editor's own lock.
    /// </remarks>
    public static bool IsEditable(TimesheetStatus status) =>
        status is TimesheetStatus.Draft or TimesheetStatus.Rejected;

    private static Result<Timesheet> Forbidden() =>
        Result<Timesheet>.Forbidden("You are not authorized to modify this timesheet.");
}
