using Application.Core;
using Application.Files;
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

            await CleanupUserDependenciesAsync(user.Id, request.RequestingUserId, cancellationToken);

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

        private async Task CleanupUserDependenciesAsync(string userId, string requestingUserId, CancellationToken cancellationToken)
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

            // Tasks. Both foreign keys onto User are Restrict. The ones this user
            // created go with them (their assignee rows cascade); for the rest, see below.
            var createdTasks = await context.WorkTasks
                .Where(t => t.CreatedById == userId)
                .ToListAsync(cancellationToken);
            context.WorkTasks.RemoveRange(createdTasks);

            // The leaver comes off every task they were on. A task somebody else
            // created that would be left with nobody goes back to its creator; one
            // still shared with others just loses the leaver. The check reads the
            // database, where the leaver's rows are still present until SaveChanges.
            var assignedRows = await context.WorkTaskAssignees
                .Where(a => a.UserId == userId)
                .ToListAsync(cancellationToken);
            context.WorkTaskAssignees.RemoveRange(assignedRows);

            var assignedTaskIds = assignedRows.Select(a => a.WorkTaskId).ToList();
            var leftWithNobody = await context.WorkTasks
                .Where(t => assignedTaskIds.Contains(t.Id)
                    && t.CreatedById != userId
                    && !t.Assignees.Any(a => a.UserId != userId))
                .ToListAsync(cancellationToken);
            foreach (var task in leftWithNobody)
            {
                context.WorkTaskAssignees.Add(new WorkTaskAssignee { WorkTaskId = task.Id, UserId = task.CreatedById });
                task.UpdatedAtUtc = DateTime.UtcNow;
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
                    .Where(t => t.EmployeeProfileId == userProfileId)
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

            await ReleaseUploadedFilesAsync(userId, requestingUserId, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Files this user uploaded. <c>StoredFile.UploadedById</c> is
        /// <c>DeleteBehavior.Restrict</c> (see the note on it in <c>AppDbContext</c>),
        /// so any left pointing at them fails the delete with a raw
        /// <c>DbUpdateException</c> — which is how it surfaced in production: a 500
        /// on anyone who had ever set a profile photo or attached a doctor's note,
        /// while a developer box, whose demo accounts upload nothing, deleted them fine.
        ///
        /// A file only this user referred to — their own photo, evidence or handover
        /// on their own leave, all removed above — goes with them. One a surviving
        /// row still points at (evidence an admin uploaded onto somebody else's
        /// leave) is handed to the admin performing the delete, who can already read
        /// every file, and whose leave it is keeps reading it through their own leave.
        /// Only ids are loaded: a stored file carries its bytes.
        /// </summary>
        private async Task ReleaseUploadedFilesAsync(string userId, string requestingUserId, CancellationToken cancellationToken)
        {
            var uploadedIds = await context.StoredFiles
                .Where(f => f.UploadedById == userId)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);
            if (uploadedIds.Count == 0)
            {
                return;
            }

            var paths = uploadedIds.Select(StoredFilePath.For).ToList();

            var stillReferenced = await context.AnnualLeaves
                .Where(al => al.EmployeeId != userId
                    && ((al.EvidenceUrl != null && paths.Contains(al.EvidenceUrl))
                        || (al.CoverageAttachmentUrl != null && paths.Contains(al.CoverageAttachmentUrl))))
                .Select(al => new { al.EvidenceUrl, al.CoverageAttachmentUrl })
                .ToListAsync(cancellationToken);

            var keep = stillReferenced
                .SelectMany(al => new[] { al.EvidenceUrl, al.CoverageAttachmentUrl })
                .Select(StoredFilePath.TryParseId)
                .Where(id => id is not null)
                .ToHashSet();

            // With nobody to hand them to they are deleted like the rest, and the
            // surviving row is left pointing at a path that no longer resolves. Only
            // a caller that passes no requester reaches this; the controller always does.
            var canReassign = !string.IsNullOrWhiteSpace(requestingUserId)
                && !string.Equals(requestingUserId, userId, StringComparison.Ordinal);

            foreach (var id in uploadedIds)
            {
                var stub = new StoredFile { Id = id, UploadedById = userId };
                if (canReassign && keep.Contains(id))
                {
                    var entry = context.StoredFiles.Attach(stub);
                    stub.UploadedById = requestingUserId;
                    entry.Property(f => f.UploadedById).IsModified = true;
                }
                else
                {
                    context.StoredFiles.Remove(stub);
                }
            }
        }
    }
}
