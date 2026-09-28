using System;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

public class EditAnnualLeave
{
    public class Command : IRequest<Result<Unit>>
    {
        public required EditAnnualLeaveRequest AnnualLeave { get; set; }
        public string ChangedByUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }
    public class Handler(AppDbContext context, IEmailService emailService) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var annualLeave = await context.AnnualLeaves
                .FindAsync([request.AnnualLeave.Id], cancellationToken);

            if (annualLeave is null)
                return Result<Unit>.Failure("Cannot find the annual leave.");

            if (string.IsNullOrWhiteSpace(request.ChangedByUserId))
            {
                return Result<Unit>.Failure("User context is required.");
            }

            var isInManagedDepartment = false;
            var isDirectReport = false;
            if (request.IsManager || request.IsAdmin)
            {
                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.ChangedByUserId,
                    cancellationToken);

                isInManagedDepartment = annualLeave.DepartmentId.HasValue
                    && managerScope.ManagedDepartmentIds.Contains(annualLeave.DepartmentId.Value);
                isDirectReport = managerScope.DirectReportUserIds.Contains(annualLeave.EmployeeId);
            }

            // A department-less leave is only ever an administrator's own — nobody
            // else has no department — and an HR Administrator is who decided it
            // before this task. Scoping it to nobody would strand it Pending forever.
            var isUnscopedAdminLeave = request.IsAdmin && !annualLeave.DepartmentId.HasValue;

            var inScope = isInManagedDepartment || isDirectReport || isUnscopedAdminLeave;

            // An HR Administrator's privileges from the edit dialog — reopening an
            // approved or rejected request, changing its status — apply only inside
            // their assigned departments. Outside them they are nobody.
            var actsAsAdmin = request.IsAdmin && inScope;

            var canEdit = actsAsAdmin || annualLeave.EmployeeId == request.ChangedByUserId || inScope;

            if (!canEdit)
            {
                return Result<Unit>.Failure("You can only update your own leave requests or requests in your managed departments.");
            }

            if (!actsAsAdmin)
            {
                // A manager has approved specific dates. An edit that quietly kept
                // the stage would put different dates in front of HR under the
                // manager's name, so the row is locked like an approved one. Cancel
                // and file again is the way to change it.
                if (annualLeave.Status == AnnualLeaveStatus.AwaitingHrApproval)
                    return Result<Unit>.Conflict("This request is awaiting HR approval; cancel it and file again to change it.");

                if (annualLeave.Status == AnnualLeaveStatus.Rejected || annualLeave.Status == AnnualLeaveStatus.Approved)
                    return Result<Unit>.Conflict("Approved and rejected leave requests cannot be edited.");
            }

            // Read before the edit overwrites them: what the coverage emails go out
            // for is the difference between these and where the leave ends up.
            var statusBeforeEdit = annualLeave.Status;
            var delegateBeforeEdit = annualLeave.DelegateId;

            /* Read for the same reason, and needed because notice is the one limit
               with a clock in it: a request filed properly in advance drifts towards
               its own start date every day it sits there, so by the time somebody
               opens it the notice period may long since have passed. Checking notice
               on every edit would strand such a request — the reason could not be
               corrected the morning before a trip. So it is checked only when the
               start date actually moves. */
            var startDateBeforeEdit = annualLeave.StartDate;

            annualLeave.StartDate = request.AnnualLeave.StartDate;
            annualLeave.EndDate = request.AnnualLeave.EndDate;
            annualLeave.Duration = request.AnnualLeave.Duration;
            annualLeave.LeaveTypeId = request.AnnualLeave.LeaveTypeId;

            var editedLeaveType = await context.LeaveTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(type => type.Id == request.AnnualLeave.LeaveTypeId, cancellationToken);

            // Same rule as on create: the type decides whether a child is carried.
            annualLeave.ChildId = editedLeaveType?.PerChildEntitlement == true
                ? request.AnnualLeave.ChildId
                : null;

            annualLeave.Reason = request.AnnualLeave.Reason;
            annualLeave.EvidenceUrl = request.AnnualLeave.EvidenceUrl;

            if (editedLeaveType is not null)
            {
                /* No exemption for an admin: an edit can move a request onto a type
                   that offers no half days, or widen a half day's dates past the
                   single date one covers. */
                var halfDayError = HalfDayRule.Check(
                    editedLeaveType, annualLeave.Duration, annualLeave.StartDate, annualLeave.EndDate);
                if (halfDayError is not null)
                    return Result<Unit>.Failure(halfDayError);

                /* Only when the start date moves — see startDateBeforeEdit above.
                   An edit that leaves the dates alone is not somebody trying to
                   bring a request forward, so the notice period has nothing to say
                   about it. Moving it at all re-earns the check, including moving
                   it further away, which passes on its merits. */
                if (annualLeave.StartDate.Date != startDateBeforeEdit.Date)
                {
                    var noticeError = NoticePeriodRule.Check(
                        editedLeaveType, annualLeave.StartDate, DateTime.UtcNow.Date);
                    if (noticeError is not null)
                        return Result<Unit>.Failure(noticeError);
                }

                /* Unconditional, unlike the notice check: the maximum has no clock
                   in it, so a request that breaches it was lengthened deliberately
                   or moved onto a type with a shorter limit. */
                var maxConsecutiveError = await MaxConsecutiveRule.CheckAsync(
                    context, editedLeaveType, annualLeave.StartDate, annualLeave.EndDate, cancellationToken);
                if (maxConsecutiveError is not null)
                    return Result<Unit>.Failure(maxConsecutiveError);
            }

            annualLeave.DelegateId = string.IsNullOrWhiteSpace(request.AnnualLeave.DelegateId)
                ? null
                : request.AnnualLeave.DelegateId;

            // Same rule as on create: a handover with nobody to hand over to is dropped.
            CoverageHandover.Apply(annualLeave, request.AnnualLeave);

            var employeeProfile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.Id == annualLeave.EmployeeProfileId, cancellationToken);

            if (employeeProfile is not null)
            {
                // Same gate as on create, because an edit can switch the type onto
                // a parental one the employee was never offered.
                if (editedLeaveType is not null)
                {
                    /* Unconditional, unlike the notice check: service only grows, so
                       a request accepted once can never later fail this. What an edit
                       has to catch is a switch onto a type wanting more service than
                       the employee has. */
                    var serviceError = MinimumServiceRule.Check(
                        editedLeaveType,
                        employeeProfile.EmploymentStartDate,
                        DateOnly.FromDateTime(DateTime.UtcNow.Date));
                    if (serviceError is not null)
                        return Result<Unit>.Failure(serviceError);

                    var eligibilityError = await ParentalLeaveEligibility.CheckAsync(
                        context,
                        editedLeaveType,
                        annualLeave.EmployeeId,
                        employeeProfile,
                        cancellationToken);
                    if (eligibilityError is not null)
                        return Result<Unit>.Failure(eligibilityError);
                }

                var perChildError = await PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync(
                    context,
                    annualLeave,
                    employeeProfile,
                    excludeLeaveId: annualLeave.Id,
                    cancellationToken);
                if (perChildError is not null)
                    return Result<Unit>.Failure(perChildError);
            }

            var canChangeStatus = actsAsAdmin || inScope;
            if (request.AnnualLeave.Status.HasValue && !canChangeStatus)
            {
                return Result<Unit>.Failure("Only admins or managers of the request's department can change leave status.");
            }

            if (request.AnnualLeave.Status.HasValue && request.AnnualLeave.Status.Value != annualLeave.Status)
            {
                var changedByUserId = request.ChangedByUserId;
                var userExists = await context.Users
                    .AnyAsync(u => u.Id == changedByUserId, cancellationToken);
                if (!userExists)
                {
                    return Result<Unit>.Failure("Cannot resolve the user who changed status.");
                }

                var oldStatus = annualLeave.Status;

                /* actsAsAdmin is an HR Administrator inside their scope — the caller
                   for whom Approve finishes a request that is with HR, and who is
                   refused on a Pending request that is still the manager's. A
                   Manager's Approve on a type asking for HR advances it to the HR
                   stage instead. */
                var stage = ApprovalStageRule.Resolve(editedLeaveType, oldStatus, request.AnnualLeave.Status.Value, actsAsAdmin);
                if (stage.Error is not null)
                    return Result<Unit>.Failure(stage.Error);
                var newStatus = stage.Status!.Value;

                // An approved leave is cancellable only until it starts — measured
                // against the dates as edited, since those are what will be stored.
                var cancellationError = CancellationRule.Check(oldStatus, newStatus, request.AnnualLeave.StartDate, DateTime.UtcNow);
                if (cancellationError is not null)
                    return Result<Unit>.Failure(cancellationError);

                if (newStatus != oldStatus)
                {
                    annualLeave.Status = newStatus;

                    if (newStatus == AnnualLeaveStatus.Approved)
                    {
                        annualLeave.ApprovedAt = DateTime.UtcNow;
                        annualLeave.ApprovedById = changedByUserId;
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
                        ChangedByUserId = changedByUserId,
                        OldStatus = oldStatus,
                        NewStatus = newStatus,
                        Comment = request.AnnualLeave.StatusComment,
                        ChangedAt = DateTime.UtcNow
                    });
                }
            }

            /* The attachment policy gates approval, not filing, so it is checked
               against where the leave *ends up*: the status path above approving
               an undocumented request, or an edit clearing the evidence on a row
               that is already approved. Read after the edit's own EvidenceUrl is
               applied, so attaching and approving in one save passes. An edit that
               keeps the request Pending is not asked — that is the edit an employee
               makes to attach a document dated after they had to file. */
            var reachedAnApprovalStep = annualLeave.Status == AnnualLeaveStatus.Approved
                || (annualLeave.Status == AnnualLeaveStatus.AwaitingHrApproval && statusBeforeEdit != AnnualLeaveStatus.AwaitingHrApproval);
            if (editedLeaveType is not null && reachedAnApprovalStep)
            {
                var attachmentError = AttachmentPolicyRule.Check(editedLeaveType, annualLeave.EvidenceUrl);
                if (attachmentError is not null)
                    return Result<Unit>.Failure(attachmentError);
            }

            if (employeeProfile is not null && annualLeave.Status == AnnualLeaveStatus.Approved)
            {
                var balanceError = await AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync(
                    context,
                    employeeProfile,
                    annualLeave,
                    excludeLeaveId: annualLeave.Id,
                    cancellationToken);
                if (balanceError is not null)
                    return Result<Unit>.Failure(balanceError);
            }

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

            // Three ways an edit changes who should be hearing about coverage. The
            // first is this command's own approval path, the one an admin uses from
            // the edit dialog rather than from the approve button.
            if (annualLeave.Status == AnnualLeaveStatus.Approved
                && statusBeforeEdit != AnnualLeaveStatus.Approved)
            {
                await CoverageNotification.AnnounceAsync(
                    context, emailService, annualLeave, employeeProfile, notifyDepartment: true, cancellationToken);
            }
            else if (annualLeave.Status == AnnualLeaveStatus.Approved
                && annualLeave.DelegateId != delegateBeforeEdit)
            {
                // The absence was announced already and has not changed; only the
                // name on the coverage has. The new delegate needs to know they are
                // covering, but the department does not need telling twice.
                await CoverageNotification.AnnounceAsync(
                    context, emailService, annualLeave, employeeProfile, notifyDepartment: false, cancellationToken);
            }
            else if (statusBeforeEdit == AnnualLeaveStatus.Approved
                && annualLeave.Status != AnnualLeaveStatus.Approved)
            {
                await CoverageNotification.AnnounceStoodDownAsync(
                    context, emailService, annualLeave, delegateBeforeEdit, cancellationToken);
            }

            if (annualLeave.Status == AnnualLeaveStatus.AwaitingHrApproval
                && statusBeforeEdit != AnnualLeaveStatus.AwaitingHrApproval
                && editedLeaveType is not null
                && employeeProfile is not null)
            {
                await HrApprovalNotification.SendAsync(
                    context, emailService, annualLeave, editedLeaveType, employeeProfile,
                    approvedByUserId: request.ChangedByUserId, cancellationToken);
            }

            if (employeeProfile is not null)
            {
                await ManagerReversalNotification.SendAsync(
                    context, emailService, annualLeave, editedLeaveType?.Name, employeeProfile,
                    statusBeforeEdit, annualLeave.Status, request.ChangedByUserId,
                    request.AnnualLeave.StatusComment, cancellationToken);
            }

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
