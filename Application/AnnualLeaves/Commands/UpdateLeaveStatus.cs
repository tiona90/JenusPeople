using System.Net;
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
    }

    public class Handler(
        AppDbContext context,
        IEmailService emailService,
        IChatNotificationService chatNotificationService)
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

            if (!request.IsAdmin)
            {
                if (!request.IsManager)
                    return Result<Unit>.Failure("Only admins or managers can change leave status.");

                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.ChangedByUserId,
                    cancellationToken);

                var isInManagedDepartment = annualLeave.DepartmentId.HasValue
                    && managerScope.ManagedDepartmentIds.Contains(annualLeave.DepartmentId.Value);
                var isDirectReport = managerScope.DirectReportUserIds.Contains(annualLeave.EmployeeId);

                if (!isInManagedDepartment && !isDirectReport)
                    return Result<Unit>.Failure("You can only change status for leaves in your managed scope.");
            }

            var oldStatus = annualLeave.Status;
            var newStatus = request.Request.Status;

            if (oldStatus == newStatus) return Result<Unit>.Success(Unit.Value);

            annualLeave.Status = newStatus;

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

            var leaveTypeName = annualLeave.LeaveTypeId.HasValue
                ? await context.LeaveTypes
                    .AsNoTracking()
                    .Where(leaveType => leaveType.Id == annualLeave.LeaveTypeId.Value)
                    .Select(leaveType => leaveType.Name)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            var statusLabel = newStatus.ToString();
            var subject = $"Your leave request was {statusLabel.ToLowerInvariant()}";
            var comment = string.IsNullOrWhiteSpace(request.Request.StatusComment)
                ? "No additional comment was provided."
                : request.Request.StatusComment!;
            var leaveName = leaveTypeName ?? "leave request";
            var dateRange = $"{annualLeave.StartDate:dd MMM yyyy} to {annualLeave.EndDate:dd MMM yyyy}";
            var safeEmployeeName = WebUtility.HtmlEncode(employeeContact.Name);
            var safeChangedByName = WebUtility.HtmlEncode(changedByName);
            var safeLeaveName = WebUtility.HtmlEncode(leaveName);
            var safeDateRange = WebUtility.HtmlEncode(dateRange);
            var safeStatusLabel = WebUtility.HtmlEncode(statusLabel);
            var safeComment = WebUtility.HtmlEncode(comment);

            var htmlBody = $"""
<p>Hello {safeEmployeeName},</p>
<p>Your <strong>{safeLeaveName}</strong> request for <strong>{safeDateRange}</strong> has been <strong>{safeStatusLabel}</strong> by {safeChangedByName}.</p>
<p><strong>Comment:</strong> {safeComment}</p>
<p>Please log in to the Annual Leave system to review the latest update.</p>
""";

            var textBody = $"""
Hello {employeeContact.Name},

Your {leaveName} request for {dateRange} has been {statusLabel} by {changedByName}.
Comment: {comment}

Please log in to the Annual Leave system to review the latest update.
""";

            await emailService.SendEmailAsync(
                employeeContact.Email,
                subject,
                htmlBody,
                textBody,
                cancellationToken);

            // Slack notification — only when the leave flips to Approved.
            // The notification service swallows transport errors itself, so
            // we can await it without wrapping in try/catch here.
            if (newStatus == AnnualLeaveStatus.Approved)
            {
                var slackMessage = $"🎉 {employeeContact.Name}'s leave for {dateRange} has been approved!";
                await chatNotificationService.SendMessageAsync(slackMessage, cancellationToken);
            }

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
