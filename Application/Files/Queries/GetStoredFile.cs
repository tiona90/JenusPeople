using Application.Core;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Files.Queries;

/// <summary>
/// Reads a stored file's bytes back, subject to who is asking. Access rules are
/// keyed on the file's <see cref="StoredFilePurpose"/> so a file cannot be
/// obtained under a looser rule than the one it was uploaded under.
/// </summary>
public class GetStoredFile
{
    public class Query : IRequest<Result<StoredFileDto>>
    {
        public required string Id { get; set; }
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }

        /// <summary>
        /// Widens the department scope to a leave with no department at all - an
        /// administrator's own, which no assigned department covers and which only
        /// an HR Administrator may decide. Without it they can approve a request
        /// whose document they cannot open.
        /// </summary>
        public bool IsHrAdministrator { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<StoredFileDto>>
    {
        public async Task<Result<StoredFileDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var file = await context.StoredFiles
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);

            if (file is null)
            {
                return NotFound();
            }

            var permitted = file.Purpose switch
            {
                // Avatars are rendered in team lists, company activity feeds and
                // attendance pages, so any signed-in colleague may load one.
                StoredFilePurpose.ProfileImage => true,

                StoredFilePurpose.LeaveEvidence =>
                    await CanReadEvidenceAsync(request, file, cancellationToken),

                StoredFilePurpose.CoverageHandover =>
                    await CanReadHandoverAsync(request, file, cancellationToken),

                StoredFilePurpose.TaskAttachment =>
                    await CanReadTaskAttachmentAsync(request, file, cancellationToken),

                // A purpose this handler has no rule for is refused rather than
                // defaulted open, so adding one to the enum cannot silently
                // expose files.
                _ => false,
            };

            // Refusals are reported as "missing", not "forbidden": a 403 would
            // confirm that the requested id names a real file, which is exactly
            // what someone probing for other people's evidence wants to learn.
            if (!permitted)
            {
                return NotFound();
            }

            return Result<StoredFileDto>.Success(new StoredFileDto
            {
                Id = file.Id,
                Content = file.Content,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Sha256 = file.Sha256,
            });
        }

        /// <summary>
        /// Leave evidence is visible to the employee it concerns, to a System Administrator, and
        /// to a Manager who could already open the leave it is attached to — the
        /// scope comes from <see cref="ManagerAccessScopeResolver"/>, the same
        /// source the leave queries use, so the two cannot drift apart.
        /// </summary>
        private async Task<bool> CanReadEvidenceAsync(
            Query request,
            StoredFile file,
            CancellationToken cancellationToken)
        {
            if (request.IsAdmin)
            {
                return true;
            }

            if (file.UploadedById == request.RequestingUserId)
            {
                return true;
            }

            var path = StoredFilePath.For(file.Id);

            // The uploader is not always the subject: a System Administrator creating leave on
            // someone's behalf uploads their evidence for them. Whoever the leave
            // names may read what is attached to it.
            var isTheirOwnLeave = await context.AnnualLeaves.AnyAsync(
                al => al.EvidenceUrl == path && al.EmployeeId == request.RequestingUserId,
                cancellationToken);

            if (isTheirOwnLeave)
            {
                return true;
            }

            if (!request.IsManager)
            {
                return false;
            }

            // Evidence is uploaded before the leave that references it exists, so
            // a file with no leave yet is readable only by whoever uploaded it
            // (handled above) — a manager has nothing to be scoped against.
            var scope = await ManagerAccessScopeResolver.ResolveAsync(
                context, request.RequestingUserId, cancellationToken);

            return await context.AnnualLeaves.AnyAsync(
                al => al.EvidenceUrl == path
                    && ((al.DepartmentId.HasValue && scope.ManagedDepartmentIds.Contains(al.DepartmentId.Value))
                        || scope.DirectReportUserIds.Contains(al.EmployeeId)
                        // The department-less leave an administrator files for
                        // themselves, which the HR Administrator decides and nobody
                        // else's scope reaches.
                        || (request.IsHrAdministrator && al.DepartmentId == null)),
                cancellationToken);
        }

        /// <summary>
        /// A handover document is for the colleague it was left to, so the delegate
        /// named on the leave may open it — which is the one reader evidence does
        /// not have, and why the two purposes are kept apart. Otherwise the same
        /// readers as evidence: the uploader, the employee the leave names, an
        /// System Administrator, and a Manager who could open the leave itself.
        /// </summary>
        private async Task<bool> CanReadHandoverAsync(
            Query request,
            StoredFile file,
            CancellationToken cancellationToken)
        {
            if (request.IsAdmin)
            {
                return true;
            }

            if (file.UploadedById == request.RequestingUserId)
            {
                return true;
            }

            var path = StoredFilePath.For(file.Id);

            var namedOnTheLeave = await context.AnnualLeaves.AnyAsync(
                al => al.CoverageAttachmentUrl == path
                    && (al.EmployeeId == request.RequestingUserId || al.DelegateId == request.RequestingUserId),
                cancellationToken);

            if (namedOnTheLeave)
            {
                return true;
            }

            if (!request.IsManager)
            {
                return false;
            }

            var scope = await ManagerAccessScopeResolver.ResolveAsync(
                context, request.RequestingUserId, cancellationToken);

            return await context.AnnualLeaves.AnyAsync(
                al => al.CoverageAttachmentUrl == path
                    && ((al.DepartmentId.HasValue && scope.ManagedDepartmentIds.Contains(al.DepartmentId.Value))
                        || scope.DirectReportUserIds.Contains(al.EmployeeId)
                        || (request.IsHrAdministrator && al.DepartmentId == null)),
                cancellationToken);
        }

        /// <summary>
        /// A task's attachment is for whoever can see the task: the task list's own
        /// rule (<see cref="WorkTaskAccess.FindVisibleAsync"/>), with an Employee
        /// narrowed to the tasks they are on or created. Plus the uploader and a
        /// System Administrator, as for the other purposes.
        /// </summary>
        private async Task<bool> CanReadTaskAttachmentAsync(
            Query request,
            StoredFile file,
            CancellationToken cancellationToken)
        {
            if (request.IsAdmin || file.UploadedById == request.RequestingUserId)
            {
                return true;
            }

            var taskId = await context.WorkTaskAttachments
                .Where(a => a.StoredFileId == file.Id)
                .Select(a => (int?)a.WorkTaskId)
                .FirstOrDefaultAsync(cancellationToken);
            if (taskId is null)
            {
                return false;
            }

            return await WorkTaskAccess.FindVisibleAsync(
                context, taskId.Value, request.RequestingUserId, cancellationToken, assignedOnly: !request.IsManager) is not null;
        }

        private static Result<StoredFileDto> NotFound() =>
            Result<StoredFileDto>.Failure("File not found");
    }
}
