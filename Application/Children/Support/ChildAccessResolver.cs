using Application.Core;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Support;

/// <summary>
/// Whose children a caller may see or change. Children are personal data, so the
/// answer is narrow:
///
/// <list type="bullet">
///   <item>Self — always, read and write.</item>
///   <item>Admin — any employee, read and write.</item>
///   <item>Manager — read only, and only inside their existing department scope. A
///     manager approving a paternity request has to be able to see the ledger it is
///     measured against, but the employee owns the family record.</item>
/// </list>
///
/// Reuses <see cref="ManagerAccessScopeResolver"/> rather than inventing a second
/// notion of a manager's reach.
/// </summary>
public static class ChildAccessResolver
{
    public static async Task<Result<EmployeeProfile>> ResolveAsync(
        AppDbContext context,
        string callerUserId,
        string? requestedEmployeeUserId,
        bool isAdmin,
        bool isManager,
        bool forWrite,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(callerUserId))
            return Result<EmployeeProfile>.Invalid("User context is required.");

        var targetUserId = string.IsNullOrWhiteSpace(requestedEmployeeUserId)
            ? callerUserId
            : requestedEmployeeUserId;

        var profile = await context.EmployeeProfiles
            .FirstOrDefaultAsync(ep => ep.UserId == targetUserId, cancellationToken);

        if (profile is null)
            return Result<EmployeeProfile>.Failure("Employee profile not found.");

        if (targetUserId == callerUserId || isAdmin)
            return Result<EmployeeProfile>.Success(profile);

        if (isManager && !forWrite)
        {
            var scope = await ManagerAccessScopeResolver.ResolveAsync(context, callerUserId, cancellationToken);
            var inScope = (profile.DepartmentId.HasValue && scope.ManagedDepartmentIds.Contains(profile.DepartmentId.Value))
                || scope.DirectReportUserIds.Contains(profile.UserId);

            if (inScope)
                return Result<EmployeeProfile>.Success(profile);
        }

        return Result<EmployeeProfile>.Forbidden(
            "You can only view or change children on your own profile.");
    }
}
