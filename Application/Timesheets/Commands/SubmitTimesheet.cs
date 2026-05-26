using Application.Core;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.Timesheets.Commands;

public class SubmitTimesheet
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public required string RequestingUserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    public class Handler(
        AppDbContext context,
        IEmailService emailService,
        ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var timesheet = await context.Timesheets
                .Include(t => t.Employee).ThenInclude(e => e!.User)
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (timesheet is null)
            {
                return Result<Unit>.Failure("Timesheet not found.");
            }

            if (!request.IsAdmin)
            {
                var requesterProfile = await context.EmployeeProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ep => ep.UserId == request.RequestingUserId, cancellationToken);

                if (requesterProfile is null || timesheet.EmployeeId != requesterProfile.Id)
                {
                    return Result<Unit>.ValidationFailure(
                        new Dictionary<string, string[]>
                        {
                            ["Authorization"] = ["You are not authorized to submit this timesheet."]
                        },
                        "You are not authorized to submit this timesheet.");
                }
            }

            var isResubmission = timesheet.Status == TimesheetStatus.Rejected;
            timesheet.Status = isResubmission ? TimesheetStatus.Resubmitted : TimesheetStatus.Submitted;
            timesheet.SubmittedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            await NotifyManagerAsync(timesheet, isResubmission, cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }

        private async Task NotifyManagerAsync(Timesheet timesheet, bool isResubmission, CancellationToken cancellationToken)
        {
            if (timesheet.Employee is null)
            {
                logger.LogWarning("Timesheet {Id}: Employee not loaded, skipping manager notification", timesheet.Id);
                return;
            }

            if (string.IsNullOrWhiteSpace(timesheet.Employee.ManagerId))
            {
                logger.LogInformation("Timesheet {Id}: Employee {EmployeeId} has no ManagerId set, skipping notification", timesheet.Id, timesheet.EmployeeId);
                return;
            }

            var managerProfile = await context.EmployeeProfiles
                .Include(mp => mp.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(mp => mp.Id == timesheet.Employee.ManagerId, cancellationToken);

            if (managerProfile is null)
            {
                logger.LogWarning("Timesheet {Id}: Manager profile {ManagerId} not found", timesheet.Id, timesheet.Employee.ManagerId);
                return;
            }

            if (managerProfile.User is null || string.IsNullOrWhiteSpace(managerProfile.User.Email))
            {
                logger.LogWarning("Timesheet {Id}: Manager {ManagerId} has no email", timesheet.Id, timesheet.Employee.ManagerId);
                return;
            }

            var employeeName = timesheet.Employee.User?.DisplayName
                               ?? timesheet.Employee.User?.Email
                               ?? "Employee";
            var period = $"{timesheet.PeriodStart:dd MMM yyyy} to {timesheet.PeriodEnd:dd MMM yyyy}";
            var verb = isResubmission ? "resubmitted" : "submitted";
            var subject = $"Timesheet {verb} by {employeeName}";
            var managerDisplayName = managerProfile.User.DisplayName ?? managerProfile.User.Email;
            var htmlBody = $"""
<p>Hello {managerDisplayName},</p>
<p><strong>{employeeName}</strong> has {verb} a timesheet for <strong>{period}</strong> ({timesheet.TotalHours:0.##} hours).</p>
<p>Please log in to WorkTrack to review and take action.</p>
""";
            var textBody = $"""
Hello {managerDisplayName},
{employeeName} has {verb} a timesheet for {period} ({timesheet.TotalHours:0.##} hours).
Please log in to WorkTrack to review and take action.
""";

            try
            {
                await emailService.SendEmailAsync(
                    managerProfile.User.Email,
                    subject,
                    htmlBody,
                    textBody,
                    cancellationToken);
                logger.LogInformation("Timesheet {Id}: notification email sent to manager {Email}", timesheet.Id, managerProfile.User.Email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Timesheet {Id}: failed to send notification email to {Email}", timesheet.Id, managerProfile.User.Email);
            }
        }
    }
}
