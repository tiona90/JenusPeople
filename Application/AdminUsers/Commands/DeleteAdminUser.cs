using Application.Core;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AdminUsers.Commands;

public class DeleteAdminUser
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }

        /// <summary>
        /// Who is asking. An admin deleting their own account would lock everyone
        /// out of user administration, so it is refused.
        /// </summary>
        public string RequestingUserId { get; set; } = string.Empty;
    }

    public class Handler(AppDbContext context, UserManager<User> userManager)
        : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await userManager.FindByIdAsync(request.Id);
            if (user is null)
            {
                return Result<Unit>.Failure("User not found.");
            }

            if (string.Equals(request.RequestingUserId, user.Id, StringComparison.Ordinal))
            {
                return Result<Unit>.Conflict("You cannot delete your own admin account.");
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            await CleanupUserDependenciesAsync(user.Id, cancellationToken);

            var currentRoles = await userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
            {
                var removeRolesResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeRolesResult.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return IdentityFailure("Failed to remove user roles before deletion.", removeRolesResult);
                }
            }

            var deleteResult = await userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return IdentityFailure("Failed to delete user.", deleteResult);
            }

            await transaction.CommitAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }

        private static Result<Unit> IdentityFailure(string message, IdentityResult result) =>
            Result<Unit>.ValidationFailure(
                new Dictionary<string, string[]>
                {
                    ["Identity"] = result.Errors.Select(e => e.Description).ToArray(),
                },
                message);

        private async Task CleanupUserDependenciesAsync(string userId, CancellationToken cancellationToken)
        {
            var userProfileId = await context.EmployeeProfiles
                .Where(ep => ep.UserId == userId)
                .Select(ep => ep.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(userProfileId))
            {
                var directReports = await context.EmployeeProfiles
                    .Where(ep => ep.ManagerId == userProfileId)
                    .ToListAsync(cancellationToken);

                foreach (var report in directReports)
                {
                    report.ManagerId = null;
                }
            }

            var approvedLeaves = await context.AnnualLeaves
                .Where(al => al.ApprovedById == userId)
                .ToListAsync(cancellationToken);
            foreach (var leave in approvedLeaves)
            {
                leave.ApprovedById = null;
                leave.ApprovedAt = null;
            }

            // Null out DelegateId on other people's leave this user was covering.
            // That FK is Restrict too, so leaving it set fails the delete with a raw
            // DbUpdateException instead of the 400 the caller can act on.
            var delegatedLeaves = await context.AnnualLeaves
                .Where(al => al.DelegateId == userId)
                .ToListAsync(cancellationToken);
            foreach (var leave in delegatedLeaves)
            {
                leave.DelegateId = null;
            }

            var assignedByRows = await context.UserDepartments
                .Where(ud => ud.AssignedByUserId == userId)
                .ToListAsync(cancellationToken);
            foreach (var row in assignedByRows)
            {
                row.AssignedByUserId = null;
            }

            // Null out ApproverId on timesheets approved by this user
            var approvedTimesheets = await context.Timesheets
                .Where(t => t.ApproverId == userId)
                .ToListAsync(cancellationToken);
            foreach (var ts in approvedTimesheets)
            {
                ts.ApproverId = null;
                ts.ApprovedAt = null;
            }

            // Null out OwnerId on projects owned by this user
            var ownedProjects = await context.Projects
                .Where(p => p.OwnerId == userId)
                .ToListAsync(cancellationToken);
            foreach (var project in ownedProjects)
            {
                project.OwnerId = null;
            }

            // Delete timesheet status history rows changed by this user (on any timesheet)
            var timesheetStatusChangesByUser = await context.TimesheetStatusHistories
                .Where(h => h.ChangedByUserId == userId)
                .ToListAsync(cancellationToken);
            if (timesheetStatusChangesByUser.Count > 0)
            {
                context.TimesheetStatusHistories.RemoveRange(timesheetStatusChangesByUser);
            }

            // Delete the user's own timesheets (cascade deletes entries and status histories)
            if (!string.IsNullOrWhiteSpace(userProfileId))
            {
                var userTimesheets = await context.Timesheets
                    .Where(t => t.EmployeeId == userProfileId)
                    .ToListAsync(cancellationToken);
                if (userTimesheets.Count > 0)
                {
                    context.Timesheets.RemoveRange(userTimesheets);
                }
            }

            var statusChangesByUser = await context.LeaveStatusHistories
                .Where(h => h.ChangedByUserId == userId)
                .ToListAsync(cancellationToken);
            if (statusChangesByUser.Count > 0)
            {
                context.LeaveStatusHistories.RemoveRange(statusChangesByUser);
            }

            var ownedUserDepartments = await context.UserDepartments
                .Where(ud => ud.UserId == userId)
                .ToListAsync(cancellationToken);
            if (ownedUserDepartments.Count > 0)
            {
                context.UserDepartments.RemoveRange(ownedUserDepartments);
            }

            var employeeLeaves = await context.AnnualLeaves
                .Where(al => al.EmployeeId == userId)
                .ToListAsync(cancellationToken);
            if (employeeLeaves.Count > 0)
            {
                context.AnnualLeaves.RemoveRange(employeeLeaves);
            }

            var profile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.UserId == userId, cancellationToken);
            if (profile is not null)
            {
                context.EmployeeProfiles.Remove(profile);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
