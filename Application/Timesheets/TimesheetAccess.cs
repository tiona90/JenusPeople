using Application.Core;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets;

/// <summary>
/// Who may modify a timesheet — and therefore its entries: the employee it
/// belongs to, an Admin, or a Manager whose scope covers that employee (their
/// department, or a direct report). Widens the ownership rule
/// <c>TimesheetsController.DeleteTimesheet</c> applies inline, and lives here so
/// every entry action enforces the identical rule rather than its own variant.
/// </summary>
public static class TimesheetAccess
{
    /// <summary>
    /// Resolves the timesheet and the caller's right to write to it.
    /// Success carries the (tracked) timesheet; <see cref="ResultErrorKind.NotFound"/>
    /// means no such timesheet, <see cref="ResultErrorKind.Forbidden"/> means it
    /// exists but is not the caller's to touch.
    /// </summary>
    public static async Task<Result<Timesheet>> AuthorizeWriteAsync(
        AppDbContext context,
        string timesheetId,
        string requestingUserId,
        bool isAdmin,
        bool isManager,
        CancellationToken cancellationToken = default)
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

        if (callerProfileIds.Contains(timesheet.EmployeeId))
        {
            return Result<Timesheet>.Success(timesheet);
        }

        if (isManager)
        {
            var scope = await ManagerAccessScopeResolver.ResolveAsync(
                context, requestingUserId, cancellationToken);

            var inScope = scope.ManagedDepartmentIds.Contains(timesheet.DepartmentId);
            if (!inScope && scope.ManagerProfileIds.Count > 0)
            {
                inScope = await context.EmployeeProfiles
                    .AsNoTracking()
                    .AnyAsync(ep =>
                        ep.Id == timesheet.EmployeeId
                        && ep.ManagerId != null
                        && scope.ManagerProfileIds.Contains(ep.ManagerId),
                        cancellationToken);
            }

            if (inScope)
            {
                return Result<Timesheet>.Success(timesheet);
            }
        }

        return Forbidden();
    }

    private static Result<Timesheet> Forbidden() =>
        Result<Timesheet>.Forbidden("You are not authorized to modify this timesheet.");
}
