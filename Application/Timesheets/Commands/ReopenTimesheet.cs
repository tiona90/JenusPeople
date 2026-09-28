using Application.Core;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.Timesheets.Commands;

/// <summary>
/// The HR Administrator takes an approval back — the timesheet counterpart of
/// cancelling an approved leave. The sheet returns to <see cref="TimesheetStatus.Rejected"/>,
/// so it is the employee's to correct and resubmit (it then goes back through review as
/// <see cref="TimesheetStatus.Resubmitted"/>, the same path as a manager's rejection),
/// the approval stamp is cleared, the history says who sent it back and why, and both
/// the employee and the managers who review it are told. It used to return to
/// Submitted, which put it straight back in the manager's queue with the employee's
/// editor locked — nobody could fix the hours HR had objected to. HR alone may do this, inside their assigned departments (or on a
/// department-less sheet), and only to an approved sheet; the reason is required,
/// like a rejection's, because the employee will read it.
/// </summary>
public class ReopenTimesheet
{
    public const string HrOnlyMessage = "Only an HR Administrator can send an approved timesheet back for review.";
    public const string NotApprovedMessage = "Only an approved timesheet can be sent back for review.";
    public const string OutOfScopeMessage = "You can only send back timesheets inside your assigned departments.";
    public const string ReasonRequiredMessage = "A reason is required when sending a timesheet back for review.";

    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public required string RequestingUserId { get; set; }
        public bool IsHrAdministrator { get; set; }
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
            var timesheet = await context.Timesheets
                .Include(t => t.Employee).ThenInclude(e => e!.User)
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
            if (timesheet is null)
                return Result<Unit>.Failure("Timesheet not found.");

            if (!request.IsHrAdministrator)
                return Result<Unit>.Forbidden(HrOnlyMessage);

            if (timesheet.Status != TimesheetStatus.Approved)
                return Result<Unit>.Failure(NotApprovedMessage);

            // The same reach every other HR write over a timesheet has: their assigned
            // departments, plus a department-less sheet (an administrator's own).
            var scope = await ManagerAccessScopeResolver.ResolveAsync(context, request.RequestingUserId, cancellationToken);
            var inScope = timesheet.DepartmentId is null
                || scope.ManagedDepartmentIds.Contains(timesheet.DepartmentId.Value);
            if (!inScope)
                return Result<Unit>.Forbidden(OutOfScopeMessage);

            var comment = request.Comment!.Trim();

            timesheet.Status = TimesheetStatus.Rejected;
            timesheet.ApprovedAt = null;
            timesheet.ApproverId = null;
            context.TimesheetStatusHistories.Add(new TimesheetStatusHistory
            {
                TimesheetId = timesheet.Id,
                ChangedByUserId = request.RequestingUserId,
                FromStatus = (int)TimesheetStatus.Approved,
                ToStatus = (int)TimesheetStatus.Rejected,
                Comment = comment,
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

            await NotifyAsync(timesheet, request.RequestingUserId, comment, cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }

        private async Task NotifyAsync(Timesheet timesheet, string byUserId, string comment, CancellationToken cancellationToken)
        {
            var by = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == byUserId, cancellationToken);
            var byName = by?.DisplayName ?? by?.Email ?? "HR";
            var period = $"{timesheet.PeriodStart:dd MMM yyyy} to {timesheet.PeriodEnd:dd MMM yyyy}";
            var employeeName = timesheet.Employee?.User?.DisplayName ?? timesheet.Employee?.User?.Email ?? "Employee";
            var encodedComment = System.Net.WebUtility.HtmlEncode(comment);

            // The employee: their approval is gone and the sheet is back with them to correct.
            var employeeEmail = timesheet.Employee?.User?.Email;
            if (!string.IsNullOrWhiteSpace(employeeEmail))
            {
                await SendAsync(
                    employeeEmail,
                    "Your timesheet approval was cancelled",
                    $"""
<p>Hello {employeeName},</p>
<p>The approval of your timesheet for <strong>{period}</strong> ({timesheet.TotalHours:0.##} hours) was cancelled by {byName}. It is back with you to correct and resubmit.</p>
<p><strong>Reason:</strong> {encodedComment}</p>
<p>Please log in to Jenus People to review the latest update.</p>
""",
                    $"""
Hello {employeeName},

The approval of your timesheet for {period} ({timesheet.TotalHours:0.##} hours) was cancelled by {byName}. It is back with you to correct and resubmit.
Reason: {comment}

Please log in to Jenus People to review the latest update.
""",
                    timesheet.Id, cancellationToken);
            }

            // The managers who review it: the approval they gave is gone, and the
            // sheet will reach their queue again once the employee resubmits it.
            if (timesheet.Employee is null) return;
            var managers = await ManagerNotificationRecipients.ResolveAsync(context, timesheet.Employee, cancellationToken);
            foreach (var manager in managers)
            {
                var greeting = manager.DisplayName ?? manager.Email;
                await SendAsync(
                    manager.Email,
                    $"Timesheet approval cancelled: {employeeName}",
                    $"""
<p>Hello {greeting},</p>
<p>{byName} cancelled the approval of <strong>{employeeName}</strong>'s timesheet for <strong>{period}</strong> ({timesheet.TotalHours:0.##} hours). It has been returned to them to correct, and will come back to you for review once they resubmit it.</p>
<p><strong>Reason:</strong> {encodedComment}</p>
<p>Please log in to Jenus People to review the latest update.</p>
""",
                    $"""
Hello {greeting},

{byName} cancelled the approval of {employeeName}'s timesheet for {period} ({timesheet.TotalHours:0.##} hours). It has been returned to them to correct, and will come back to you for review once they resubmit it.
Reason: {comment}

Please log in to Jenus People to review the latest update.
""",
                    timesheet.Id, cancellationToken);
            }
        }

        private async Task SendAsync(string to, string subject, string html, string text, string timesheetId, CancellationToken cancellationToken)
        {
            try
            {
                await emailService.SendEmailAsync(to, subject, html, text, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Timesheet {Id}: failed to send reopen notification to {Email}", timesheetId, to);
            }
        }
    }
}
