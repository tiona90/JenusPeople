using Application.AnnualLeaves.DTOs;
using Application.Core;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

public class UpdateLeaveStatus
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string LeaveId { get; set; }
        public required UpdateLeaveStatusRequest Request { get; set; }
        public string ChangedByUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        /// <summary>Test seam for <see cref="CancellationRule"/>; the controller leaves it null.</summary>
        public DateTime? NowUtc { get; set; }
    }

    public class Handler(
        AppDbContext context,
        IEmailService emailService)
        : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var annualLeave = await context.AnnualLeaves
                .FindAsync([request.LeaveId], cancellationToken);

            if (annualLeave is null)
                return Result<Unit>.Failure("Cannot find the annual leave.");

            if (string.IsNullOrWhiteSpace(request.ChangedByUserId))
                return Result<Unit>.Failure("User context is required.");

            if (!request.IsAdmin && !request.IsManager)
                return Result<Unit>.Failure("Only admins or managers can change leave status.");

            // IsAdmin here is the HR Administrator acting on somebody's behalf, and
            // their reach is their assigned departments — the same resolver, the same
            // test, as a Manager's. Nobody decides leave unscoped.
            var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                context,
                request.ChangedByUserId,
                cancellationToken);

            var isInManagedDepartment = annualLeave.DepartmentId.HasValue
                && managerScope.ManagedDepartmentIds.Contains(annualLeave.DepartmentId.Value);
            var isDirectReport = managerScope.DirectReportUserIds.Contains(annualLeave.EmployeeId);
            // A department-less leave is only ever an administrator's own — nobody
            // else has no department — and an HR Administrator is who decided it
            // before this task. Scoping it to nobody would strand it Pending forever.
            var isUnscopedAdminLeave = request.IsAdmin && !annualLeave.DepartmentId.HasValue;

            if (!isInManagedDepartment && !isDirectReport && !isUnscopedAdminLeave)
                return Result<Unit>.Failure("You can only change status for leaves in your managed scope.");

            var oldStatus = annualLeave.Status;

            var leaveType = await context.LeaveTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(type => type.Id == annualLeave.LeaveTypeId, cancellationToken);

            /* The client asks for Approved; the leave type's two switches and who is
               asking decide whether that means the HR stage or the end. A Manager on
               a request that is already with HR is refused here, whatever they ask,
               and so is an HR Administrator on a Pending request that is still the
               manager's. */
            var stage = ApprovalStageRule.Resolve(leaveType, oldStatus, request.Request.Status, request.IsAdmin);
            if (stage.Error is not null)
                return Result<Unit>.Failure(stage.Error);
            var newStatus = stage.Status!.Value;

            if (oldStatus == newStatus) return Result<Unit>.Success(Unit.Value);

            // An approved leave is cancellable only until it starts.
            var cancellationError = CancellationRule.Check(oldStatus, newStatus, annualLeave.StartDate, request.NowUtc ?? DateTime.UtcNow);
            if (cancellationError is not null)
                return Result<Unit>.Failure(cancellationError);

            annualLeave.Status = newStatus;

            /* The attachment policy gates this transition rather than filing: the
               document a type requires may be dated after the request had to go in
               (call-up papers), so the employee files, attaches it from My Leave,
               and only then can this approve. It gates both steps out of Pending —
               a manager cannot pass an undocumented request along to HR either.
               Rejecting or cancelling asks nothing. No exemption for an admin — the
               rule is about the leave type, not about who is clicking. */
            var isApprovalStep = newStatus is AnnualLeaveStatus.Approved or AnnualLeaveStatus.AwaitingHrApproval;
            if (leaveType is not null && oldStatus != AnnualLeaveStatus.Approved && isApprovalStep)
            {
                var attachmentError = AttachmentPolicyRule.Check(leaveType, annualLeave.EvidenceUrl);
                if (attachmentError is not null)
                    return Result<Unit>.Failure(attachmentError);
            }

            var employeeProfile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.Id == annualLeave.EmployeeProfileId, cancellationToken);

            if (employeeProfile is not null && oldStatus != AnnualLeaveStatus.Approved && newStatus == AnnualLeaveStatus.Approved)
            {
                var balanceError = await AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync(
                    context,
                    employeeProfile,
                    annualLeave,
                    excludeLeaveId: annualLeave.Id,
                    cancellationToken);
                if (balanceError is not null)
                    return Result<Unit>.Failure(balanceError);

                /* A null ChildId on a per-child type can only be a row that predates
                   this feature: CreateAnnualLeave refuses a per-child request with no
                   child, and EditAnnualLeave sets ChildId from the leave type. So no
                   live path can produce one, and skipping the check here closes no
                   hole — it only stops a legacy Pending row being stranded, since no
                   surface can attach a child to it and the check would refuse every
                   approval. That treats a legacy pending row exactly as the design
                   already treats a legacy approved one: charging nobody's ledger.
                   A row that *does* name a child still gets both caps enforced. */
                if (annualLeave.ChildId is not null)
                {
                    var perChildError = await PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync(
                        context,
                        annualLeave,
                        employeeProfile,
                        excludeLeaveId: annualLeave.Id,
                        cancellationToken);
                    if (perChildError is not null)
                        return Result<Unit>.Failure(perChildError);
                }
            }

            if (newStatus == AnnualLeaveStatus.Approved)
            {
                annualLeave.ApprovedAt = DateTime.UtcNow;
                annualLeave.ApprovedById = request.ChangedByUserId;
            }
            else if (oldStatus == AnnualLeaveStatus.Approved)
            {
                annualLeave.ApprovedAt = null;
                annualLeave.ApprovedById = null;
            }

            context.LeaveStatusHistories.Add(new LeaveStatusHistory
            {
                Id = Guid.NewGuid().ToString(),
                AnnualLeaveId = annualLeave.Id,
                ChangedByUserId = request.ChangedByUserId,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Comment = request.Request.StatusComment,
                ChangedAt = DateTime.UtcNow
            });

            // One transaction over both saves: the balance sync reads approved leave
            // back out of the database, so it cannot share the leave's SaveChanges,
            // and a failure on the second write must not leave the balance stale
            // against a leave that has already been written.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Nothing is committed, so disposing the transaction rolls the
                // attempted write back.
                return Result<Unit>.Failure(ConcurrencyError.Message);
            }

            if (employeeProfile is not null)
            {
                await AnnualLeaveBalanceCalculator.SyncCurrentYearBalanceAsync(context, employeeProfile, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            // Coverage first, and before the early return below: whether the
            // delegate and the department hear about an absence has nothing to do
            // with whether the employee themselves has an email address on file.
            if (newStatus == AnnualLeaveStatus.Approved)
            {
                await CoverageNotification.AnnounceAsync(
                    context, emailService, annualLeave, employeeProfile, notifyDepartment: true, cancellationToken);
            }
            else if (oldStatus == AnnualLeaveStatus.Approved)
            {
                // Cancelled, or an approval taken back. Somebody was asked to hold
                // the fort and needs to hear that they no longer have to.
                await CoverageNotification.AnnounceStoodDownAsync(
                    context, emailService, annualLeave, annualLeave.DelegateId, cancellationToken);
            }

            if (newStatus == AnnualLeaveStatus.AwaitingHrApproval && leaveType is not null && employeeProfile is not null)
            {
                // The manager has had their say; the HR Administrators covering the
                // department now need to hear it is with them.
                await HrApprovalNotification.SendAsync(
                    context, emailService, annualLeave, leaveType, employeeProfile,
                    approvedByUserId: request.ChangedByUserId, cancellationToken);
            }

            if (employeeProfile is not null)
            {
                // An approval taken back — HR cancelling an approved leave, or
                // rejecting one the manager passed along — is news to the managers
                // who gave it, not only to the employee and the delegate.
                await ManagerReversalNotification.SendAsync(
                    context, emailService, annualLeave, leaveType?.Name, employeeProfile,
                    oldStatus, newStatus, request.ChangedByUserId, request.Request.StatusComment, cancellationToken);
            }

            var employeeContact = await context.Users
                .AsNoTracking()
                .Where(user => user.Id == annualLeave.EmployeeId)
                .Select(user => new
                {
                    user.Email,
                    Name = !string.IsNullOrWhiteSpace(user.DisplayName)
                        ? user.DisplayName
                        : (user.Email ?? user.UserName ?? "Employee")
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (employeeContact is null || string.IsNullOrWhiteSpace(employeeContact.Email))
            {
                return Result<Unit>.Success(Unit.Value);
            }

            var changedByName = await context.Users
                .AsNoTracking()
                .Where(user => user.Id == request.ChangedByUserId)
                .Select(user => !string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.DisplayName
                    : (user.Email ?? user.UserName ?? "Manager"))
                .FirstOrDefaultAsync(cancellationToken)
                ?? "Manager";

            var leaveTypeName = leaveType?.Name;

            var statusLabel = newStatus.ToString();
            var subject = newStatus == AnnualLeaveStatus.AwaitingHrApproval
                ? "Your leave request is awaiting HR approval"
                : $"Your leave request was {statusLabel.ToLowerInvariant()}";
            var comment = string.IsNullOrWhiteSpace(request.Request.StatusComment)
                ? "No additional comment was provided."
                : request.Request.StatusComment!;
            var leaveName = leaveTypeName ?? "leave request";
            var dateRange = $"{annualLeave.StartDate:dd MMM yyyy} to {annualLeave.EndDate:dd MMM yyyy}";

            // Sentence takes a FormattableString; a ternary of two interpolations
            // would decay to a plain string and lose the per-value encoding.
            FormattableString sentence;
            if (newStatus == AnnualLeaveStatus.AwaitingHrApproval)
                sentence = $"Your {leaveName} request for {dateRange} has been approved by {NotificationEmail.Plain(changedByName)} and is awaiting HR approval.";
            else
                sentence = $"Your {leaveName} request for {dateRange} has been {statusLabel} by {NotificationEmail.Plain(changedByName)}.";

            // Six WebUtility.HtmlEncode calls used to sit here, one per value. They
            // were correct; the same email in CreateAnnualLeave had none. The
            // builder does the encoding now, for both.
            var body = NotificationEmail
                .To(employeeContact.Name)
                .Sentence(sentence)
                .Detail("Comment", comment)
                .Closing("Please log in to the Annual Leave system to review the latest update.")
                .Build();

            await emailService.SendEmailAsync(
                employeeContact.Email,
                subject,
                body.Html,
                body.Text,
                cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
