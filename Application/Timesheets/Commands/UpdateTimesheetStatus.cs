using Application.Core;
using Application.Timesheets.Support;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.Timesheets.Commands;

public class UpdateTimesheetStatus
{
    public const string NotAwaitingReviewMessage = "Only a submitted timesheet can be approved or rejected.";
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public required TimesheetStatus NewStatus { get; set; }
        public required string RequestingUserId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsHrAdministrator { get; set; }
        public string? Comment { get; set; }
        /// <summary>Test seam for <see cref="TimesheetReviewRule"/>'s "is a manager available today"; the controller leaves it null.</summary>
        public DateTime? NowUtc { get; set; }
    }

    public const string OwnTimesheetMessage = "You cannot approve or reject your own timesheet.";

    public class Handler(
        AppDbContext context,
        IEmailService emailService,
        ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            // Input rules (valid target status + comment-required-on-reject) are enforced
            // by UpdateTimesheetStatusValidator in the ValidationBehavior pipeline.
            var timesheet = await context.Timesheets.FindAsync([request.Id], cancellationToken);
            if (timesheet is null)
            {
                return Result<Unit>.Failure("Timesheet not found.");
            }

            // Nobody approves or rejects their own hours — not a Manager, whose own
            // timesheet sits inside their own department scope, and not an HR
            // Administrator, who reaches their own department-less one. The approval
            // pages leave the caller's own row out of the queue; this is the rule
            // behind that, so a direct call cannot get round it.
            var isOwn = await context.EmployeeProfiles
                .AsNoTracking()
                .AnyAsync(ep => ep.Id == timesheet.EmployeeProfileId && ep.UserId == request.RequestingUserId, cancellationToken);
            if (isOwn)
            {
                return Result<Unit>.ValidationFailure(
                    new Dictionary<string, string[]> { ["Authorization"] = [OwnTimesheetMessage] },
                    OwnTimesheetMessage);
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

                var inScope = timesheet.DepartmentId != null
                    && scope.ManagedDepartmentIds.Contains(timesheet.DepartmentId.Value);
                if (!inScope && scope.ManagerProfileIds.Count > 0)
                {
                    inScope = await context.EmployeeProfiles
                        .AsNoTracking()
                        .AnyAsync(ep =>
                            ep.Id == timesheet.EmployeeProfileId
                            && ep.ManagerId != null
                            && scope.ManagerProfileIds.Contains(ep.ManagerId),
                            cancellationToken);
                }

                // The HR Administrator also reaches a department-less timesheet — an
                // administrator's own — which no department scope would otherwise include.
                if (!inScope && timesheet.DepartmentId == null && request.IsHrAdministrator)
                {
                    inScope = true;
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

                // The manager stage is the manager's, as it is for leave. An HR
                // Administrator reviews a timesheet only when no manager is available
                // to: the submitter is the only manager, the department has none, or
                // every manager is on leave today (TimesheetReviewRule).
                if (request.IsHrAdministrator)
                {
                    var submitter = await context.EmployeeProfiles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(ep => ep.Id == timesheet.EmployeeProfileId, cancellationToken);
                    if (submitter is not null
                        && await TimesheetReviewRule.ManagerAvailableAsync(context, submitter, request.NowUtc ?? DateTime.UtcNow, cancellationToken))
                    {
                        return Result<Unit>.ValidationFailure(
                            new Dictionary<string, string[]> { ["Authorization"] = [TimesheetReviewRule.WithManagerMessage] },
                            TimesheetReviewRule.WithManagerMessage);
                    }
                }
            }

            // A reviewer decides only a sheet in front of them for review. Taking an
            // approval back is HR's ReopenTimesheet, and a Draft is still the
            // employee's to finish.
            if (!TimesheetReviewRule.IsOpen(timesheet.Status))
                return Result<Unit>.Conflict(NotAwaitingReviewMessage);

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

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<Unit>.Failure(ConcurrencyError.Message);
            }

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
                .FirstOrDefaultAsync(ep => ep.Id == timesheet.EmployeeProfileId, cancellationToken);

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
<p>Please log in to Jenus People to review the latest update.</p>
""";

            var textBody = $"""
Hello {employeeName},

Your timesheet for {period} ({timesheet.TotalHours:0.##} hours) has been {statusLabel} by {reviewerName}.{commentLine}

Please log in to Jenus People to review the latest update.
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
