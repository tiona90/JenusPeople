using Application.AnnualLeaves.Commands;
using Application.Core;
using Application.Timesheets.Support;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.Timesheets.Commands;

public class SubmitTimesheet
{
    public const string AlreadySubmittedMessage = "This timesheet has already been submitted.";
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public required string RequestingUserId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsHrAdministrator { get; set; }
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

                var isOwn = requesterProfile is not null && timesheet.EmployeeProfileId == requesterProfile.Id;
                var inScope = false;
                if (!isOwn && request.IsHrAdministrator)
                {
                    // Submitting on behalf is the HR Administrator's alone, inside their
                    // assigned departments — the same scope every other timesheet write
                    // uses. Deliberately not a Manager: they review what their team
                    // submits, and putting the draft in for them would commit hours the
                    // employee never stood behind and make the approval that follows the
                    // same person twice. (IsManager is still carried on the command; it
                    // simply no longer grants this.)
                    var scope = await ManagerAccessScopeResolver.ResolveAsync(context, request.RequestingUserId, cancellationToken);
                    inScope = (timesheet.DepartmentId != null && scope.ManagedDepartmentIds.Contains(timesheet.DepartmentId.Value))
                        || (timesheet.Employee != null && scope.DirectReportUserIds.Contains(timesheet.Employee.UserId))
                        // The HR Administrator also reaches a department-less timesheet — an
                        // administrator's own — which no department scope would otherwise
                        // include. This branch is theirs alone, so the role needs no
                        // re-testing here.
                        || timesheet.DepartmentId == null;
                }

                if (!isOwn && !inScope)
                {
                    return Result<Unit>.ValidationFailure(
                        new Dictionary<string, string[]>
                        {
                            ["Authorization"] = ["You are not authorized to submit this timesheet."]
                        },
                        "You are not authorized to submit this timesheet.");
                }
            }

            // Draft → Submitted and Rejected → Resubmitted are the only ways in; a
            // repeat submit re-sent every email, and submitting an Approved sheet put
            // it back in the queue with its approval stamp still on it.
            if (timesheet.Status is not (TimesheetStatus.Draft or TimesheetStatus.Rejected))
                return Result<Unit>.Conflict(AlreadySubmittedMessage);

            var isResubmission = timesheet.Status == TimesheetStatus.Rejected;
            var submittedStatus = isResubmission ? TimesheetStatus.Resubmitted : TimesheetStatus.Submitted;
            timesheet.Status = submittedStatus;
            timesheet.SubmittedAt = DateTime.UtcNow;

            // The department's only manager has nobody to review their hours — not
            // themselves, and not HR, whose review is for a manager who is away — so
            // their sheet is self-certified: approved on submission, with the history
            // saying so (TimesheetReviewRule.SelfCertifiesAsync).
            var selfCertified = timesheet.Employee is not null
                && await TimesheetReviewRule.SelfCertifiesAsync(context, timesheet.Employee, cancellationToken);
            if (selfCertified)
            {
                timesheet.Status = TimesheetStatus.Approved;
                timesheet.ApprovedAt = DateTime.UtcNow;
                timesheet.ApproverId = timesheet.Employee!.UserId;
                context.TimesheetStatusHistories.Add(new TimesheetStatusHistory
                {
                    TimesheetId = timesheet.Id,
                    ChangedByUserId = timesheet.Employee.UserId,
                    FromStatus = (int)submittedStatus,
                    ToStatus = (int)TimesheetStatus.Approved,
                    Comment = TimesheetReviewRule.SelfCertifiedComment,
                    ChangedAt = DateTime.UtcNow,
                });
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<Unit>.Failure(ConcurrencyError.Message);
            }

            if (selfCertified)
            {
                logger.LogInformation("Timesheet {Id}: approved on submission — the submitter is the department's only manager", timesheet.Id);
                return Result<Unit>.Success(Unit.Value);
            }

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

            // Notify the direct manager AND every Manager-role user in the
            // employee's department (matches how manager team scope works) — or,
            // when none of them is available today (all on leave, or there is none,
            // or the submitter is the only manager), the HR Administrators covering
            // the department, who are the ones who can review it
            // (TimesheetReviewRule). The same routing leave takes at filing.
            var availability = await ManagerAvailability.CheckAsync(context, timesheet.Employee, DateTime.UtcNow, cancellationToken);
            string? note = null;
            List<ManagerContact> recipients;
            if (availability.AnyAvailable)
            {
                recipients = await ManagerNotificationRecipients.ResolveAsync(context, timesheet.Employee, cancellationToken);
            }
            else
            {
                recipients = await HrApprovalRecipients.ResolveAsync(
                    context, timesheet.DepartmentId, excludeUserId: timesheet.Employee.UserId, cancellationToken);
                note = ManagerAvailability.Describe(availability).Replace("for approval", "for review");
            }

            if (recipients.Count == 0)
            {
                logger.LogInformation("Timesheet {Id}: no reviewer to notify for employee {EmployeeProfileId}, skipping notification", timesheet.Id, timesheet.EmployeeProfileId);
                return;
            }

            var employeeName = timesheet.Employee.User?.DisplayName
                               ?? timesheet.Employee.User?.Email
                               ?? "Employee";
            var period = $"{timesheet.PeriodStart:dd MMM yyyy} to {timesheet.PeriodEnd:dd MMM yyyy}";
            var verb = isResubmission ? "resubmitted" : "submitted";
            var subject = $"Timesheet {verb} by {employeeName}";

            foreach (var recipient in recipients)
            {
                var greetingName = recipient.DisplayName ?? recipient.Email;
                var htmlBody = $"""
<p>Hello {greetingName},</p>
<p><strong>{employeeName}</strong> has {verb} a timesheet for <strong>{period}</strong> ({timesheet.TotalHours:0.##} hours).</p>
{(note is null ? "" : $"<p><strong>Note:</strong> {note}</p>")}
<p>Please log in to Jenus People to review and take action.</p>
""";
                var textBody = $"""
Hello {greetingName},
{employeeName} has {verb} a timesheet for {period} ({timesheet.TotalHours:0.##} hours).
{(note is null ? "" : $"Note: {note}\n")}Please log in to Jenus People to review and take action.
""";

                try
                {
                    var sent = await emailService.SendEmailAsync(
                        recipient.Email,
                        subject,
                        htmlBody,
                        textBody,
                        cancellationToken);

                    if (sent)
                    {
                        logger.LogInformation("Timesheet {Id}: notification email sent to manager {Email}", timesheet.Id, recipient.Email);
                    }
                    else
                    {
                        logger.LogWarning("Timesheet {Id}: notification email to manager {Email} was not sent", timesheet.Id, recipient.Email);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Timesheet {Id}: failed to send notification email to {Email}", timesheet.Id, recipient.Email);
                }
            }
        }
    }
}
