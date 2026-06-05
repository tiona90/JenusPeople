using Application.Core;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.Timesheets.Commands;

public class UpdateTimesheetStatus
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public required TimesheetStatus NewStatus { get; set; }
        public required string RequestingUserId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public string? Comment { get; set; }
    }

    public class Handler(
        AppDbContext context,
        IEmailService emailService,
        ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.NewStatus is not (TimesheetStatus.Approved or TimesheetStatus.Rejected))
            {
                return Result<Unit>.ValidationFailure(
                    new Dictionary<string, string[]>
                    {
                        ["NewStatus"] = ["Only Approved or Rejected transitions are supported by this command."]
                    },
                    "Invalid timesheet status transition.");
            }

            if (request.NewStatus == TimesheetStatus.Rejected && string.IsNullOrWhiteSpace(request.Comment))
            {
                return Result<Unit>.ValidationFailure(
                    new Dictionary<string, string[]>
                    {
                        ["Comment"] = ["A reason is required when rejecting a timesheet."]
                    },
                    "A reason is required when rejecting a timesheet.");
            }

            var timesheet = await context.Timesheets.FindAsync([request.Id], cancellationToken);
            if (timesheet is null)
            {
                return Result<Unit>.Failure("Timesheet not found.");
            }

            if (!request.IsAdmin)
            {
                if (!request.IsManager)
                {
                    return Result<Unit>.ValidationFailure(
                        new Dictionary<string, string[]>
                        {
                            ["Authorization"] = ["You are not authorized to update this timesheet."]
                        },
                        "You are not authorized to update this timesheet.");
                }

                var scope = await ManagerAccessScopeResolver.ResolveAsync(
                    context, request.RequestingUserId, cancellationToken);

                var inScope = scope.ManagedDepartmentIds.Contains(timesheet.DepartmentId);
                if (!inScope && scope.ManagerProfileIds.Count > 0)
                {
                    inScope = await context.EmployeeProfiles
                        .AsNoTracking()
                        .AnyAsync(ep =>
                            ep.Id == timesheet.EmployeeId
                            && ep.ManagerId != null
                            && scope.ManagerProfileIds.Contains(ep.ManagerId),
                            cancellationToken);
                }

                if (!inScope)
                {
                    return Result<Unit>.ValidationFailure(
                        new Dictionary<string, string[]>
                        {
                            ["Authorization"] = ["You are not authorized to update timesheets outside your departments."]
                        },
                        "You are not authorized to update this timesheet.");
                }
            }

            var fromStatus = (int)timesheet.Status;
            timesheet.Status = request.NewStatus;
            if (request.NewStatus == TimesheetStatus.Approved)
            {
                timesheet.ApprovedAt = DateTime.UtcNow;
                timesheet.ApproverId = request.RequestingUserId;
            }

            var trimmedComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

            context.TimesheetStatusHistories.Add(new TimesheetStatusHistory
            {
                TimesheetId = timesheet.Id,
                ChangedByUserId = request.RequestingUserId,
                FromStatus = fromStatus,
                ToStatus = (int)request.NewStatus,
                Comment = trimmedComment,
                ChangedAt = DateTime.UtcNow,
            });

            await context.SaveChangesAsync(cancellationToken);

            await NotifyEmployeeAsync(timesheet, request.NewStatus, request.RequestingUserId, trimmedComment, cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }

        private async Task NotifyEmployeeAsync(
            Timesheet timesheet,
            TimesheetStatus newStatus,
            string requestingUserId,
            string? comment,
            CancellationToken cancellationToken)
        {
            var employee = await context.EmployeeProfiles
                .Include(ep => ep.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(ep => ep.Id == timesheet.EmployeeId, cancellationToken);

            if (employee?.User is null || string.IsNullOrWhiteSpace(employee.User.Email))
            {
                logger.LogWarning("Timesheet {Id}: employee has no email, skipping status notification", timesheet.Id);
                return;
            }

            var reviewer = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == requestingUserId, cancellationToken);
            var reviewerName = reviewer?.DisplayName ?? reviewer?.Email ?? "Your manager";

            var period = $"{timesheet.PeriodStart:dd MMM yyyy} to {timesheet.PeriodEnd:dd MMM yyyy}";
            var statusLabel = newStatus == TimesheetStatus.Approved ? "approved" : "rejected";
            var subject = $"Your timesheet was {statusLabel}";
            var employeeName = employee.User.DisplayName ?? employee.User.Email;
            var commentLine = string.IsNullOrWhiteSpace(comment) ? string.Empty : $"\nComment: {comment}";
            var commentHtml = string.IsNullOrWhiteSpace(comment)
                ? string.Empty
                : $"<p><strong>Comment:</strong> {System.Net.WebUtility.HtmlEncode(comment)}</p>";

            var htmlBody = $"""
<p>Hello {employeeName},</p>
<p>Your timesheet for <strong>{period}</strong> ({timesheet.TotalHours:0.##} hours) has been <strong>{statusLabel}</strong> by {reviewerName}.</p>
{commentHtml}
<p>Please log in to WorkTrack to review the latest update.</p>
""";

            var textBody = $"""
Hello {employeeName},

Your timesheet for {period} ({timesheet.TotalHours:0.##} hours) has been {statusLabel} by {reviewerName}.{commentLine}

Please log in to WorkTrack to review the latest update.
""";

            try
            {
                var sent = await emailService.SendEmailAsync(
                    employee.User.Email,
                    subject,
                    htmlBody,
                    textBody,
                    cancellationToken);

                if (sent)
                {
                    logger.LogInformation("Timesheet {Id}: status email sent to {Email}", timesheet.Id, employee.User.Email);
                }
                else
                {
                    logger.LogWarning("Timesheet {Id}: status email to {Email} was not sent", timesheet.Id, employee.User.Email);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Timesheet {Id}: failed to send status email to {Email}", timesheet.Id, employee.User.Email);
            }
        }
    }
}
