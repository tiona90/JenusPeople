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
    public class Command : IRequest
    {
        public required string LeaveId { get; set; }
        public required UpdateLeaveStatusRequest Request { get; set; }
        public string ChangedByUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context, IEmailService emailService) : IRequestHandler<Command>
    {
        public async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var annualLeave = await context.AnnualLeaves
                .FindAsync([request.LeaveId], cancellationToken)
                ?? throw new Exception("Cannot find the annual leave");

            if (string.IsNullOrWhiteSpace(request.ChangedByUserId))
                throw new UnauthorizedAccessException("User context is required.");

            if (!request.IsAdmin)
            {
                if (!request.IsManager)
                    throw new UnauthorizedAccessException("Only admins or managers can change leave status.");

                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.ChangedByUserId,
                    cancellationToken);

                var isInManagedDepartment = annualLeave.DepartmentId.HasValue
                    && managerScope.ManagedDepartmentIds.Contains(annualLeave.DepartmentId.Value);
                var isDirectReport = managerScope.DirectReportUserIds.Contains(annualLeave.EmployeeId);

                if (!isInManagedDepartment && !isDirectReport)
                    throw new UnauthorizedAccessException("You can only change status for leaves in your managed scope.");
            }

            var oldStatus = annualLeave.Status;
            var newStatus = request.Request.Status;

            if (oldStatus == newStatus) return;

            annualLeave.Status = newStatus;

            var employeeProfile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.Id == annualLeave.EmployeeProfileId, cancellationToken);

            if (employeeProfile is not null && oldStatus != AnnualLeaveStatus.Approved && newStatus == AnnualLeaveStatus.Approved)
            {
                await AnnualLeaveBalanceCalculator.EnsureSufficientBalanceAsync(
                    context,
                    employeeProfile,
                    annualLeave,
                    excludeLeaveId: annualLeave.Id,
                    cancellationToken);
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

            await context.SaveChangesAsync(cancellationToken);

            if (employeeProfile is not null)
            {
                await AnnualLeaveBalanceCalculator.SyncCurrentYearBalanceAsync(context, employeeProfile, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
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
                return;
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
        }
    }
}
