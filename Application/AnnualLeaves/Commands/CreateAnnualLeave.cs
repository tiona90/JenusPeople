using Domain.Interfaces;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

public class CreateAnnualLeave
{
    public class Command : IRequest<string>
    {
        public required CreateAnnualLeaveRequest AnnualLeave { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper, IEmailService emailService) : IRequestHandler<Command, string>
    {
        public async Task<string> Handle(Command request, CancellationToken cancellationToken)
        {
            var annualLeave = mapper.Map<AnnualLeave>(request.AnnualLeave);

            var employeeProfile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.UserId == request.AnnualLeave.EmployeeId, cancellationToken);

            if (employeeProfile is null)
                throw new InvalidOperationException("Employee profile not found for the selected user.");

            annualLeave.EmployeeProfileId = employeeProfile.Id;
            annualLeave.DepartmentId = employeeProfile.DepartmentId;

            var leaveType = await context.LeaveTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    type => type.Id == annualLeave.LeaveTypeId && type.IsActive, cancellationToken);

            if (leaveType is null)
                throw new InvalidOperationException("Selected leave type is not available.");

            if (leaveType.RequiresApproval)
            {
                annualLeave.Status = AnnualLeaveStatus.Pending;
            }
            else
            {
                annualLeave.Status = AnnualLeaveStatus.Approved;
                annualLeave.ApprovedAt = DateTime.UtcNow;

                await AnnualLeaveBalanceCalculator.EnsureSufficientBalanceAsync(
                    context,
                    employeeProfile,
                    annualLeave,
                    excludeLeaveId: annualLeave.Id,
                    cancellationToken);

                context.LeaveStatusHistories.Add(new LeaveStatusHistory
                {
                    Id = Guid.NewGuid().ToString(),
                    AnnualLeaveId = annualLeave.Id,
                    ChangedByUserId = annualLeave.EmployeeId,
                    OldStatus = AnnualLeaveStatus.Pending,
                    NewStatus = AnnualLeaveStatus.Approved,
                    Comment = "Automatically approved based on leave type settings.",
                    ChangedAt = DateTime.UtcNow,
                });
            }

            context.AnnualLeaves.Add(annualLeave);
            await context.SaveChangesAsync(cancellationToken);

            if (!leaveType.RequiresApproval)
            {
                await AnnualLeaveBalanceCalculator.SyncCurrentYearBalanceAsync(context, employeeProfile, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // Notify the employee's manager(s): the direct manager and every
                // Manager-role user in the employee's department.
                var recipients = await ManagerNotificationRecipients.ResolveAsync(
                    context, employeeProfile, cancellationToken);

                if (recipients.Count > 0)
                {
                    var employeeUser = await context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == employeeProfile.UserId, cancellationToken);

                    var employeeName = employeeUser?.DisplayName ?? employeeUser?.Email ?? "Employee";
                    var leaveTypeName = leaveType.Name;
                    var dateRange = $"{annualLeave.StartDate:dd MMM yyyy} to {annualLeave.EndDate:dd MMM yyyy}";
                    var subject = $"New leave request from {employeeName}";

                    foreach (var recipient in recipients)
                    {
                        var greetingName = recipient.DisplayName ?? recipient.Email;
                        var htmlBody = $"""
            <p>Hello {greetingName},</p>
            <p>You have a new <strong>{leaveTypeName}</strong> request from <strong>{employeeName}</strong> for <strong>{dateRange}</strong>.</p>
            <p><strong>Reason:</strong> {annualLeave.Reason}</p>
            <p>Please log in to the Annual Leave system to review and take action.</p>
            """;
                        var textBody = $"""
            Hello {greetingName},
            You have a new {leaveTypeName} request from {employeeName} for {dateRange}.
            Reason: {annualLeave.Reason}
            Please log in to the Annual Leave system to review and take action.
            """;

                        await emailService.SendEmailAsync(
                            recipient.Email,
                            subject,
                            htmlBody,
                            textBody,
                            cancellationToken);
                    }
                }
            }

            return annualLeave.Id;
        }
    }
}
