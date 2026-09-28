using Application.Core;
using Domain;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// "An approval was taken back" to the employee's managers. Sent when a leave that
/// was <see cref="AnnualLeaveStatus.Approved"/>, or with HR after a manager's
/// approval (<see cref="AnnualLeaveStatus.AwaitingHrApproval"/>), is cancelled or
/// rejected by somebody else — in practice an HR Administrator cancelling an
/// approved leave before it starts, or rejecting one the manager passed along.
///
/// Before this, the employee and the delegate were told and the managers were not:
/// the request simply vanished from their team calendar, with nothing saying who
/// had overruled the approval they gave or why. The recipients are the same set
/// <see cref="ManagerNotificationRecipients"/> resolves for a new request, minus
/// whoever made the change (a manager who cancels does not need telling), and the
/// employee. Called from <see cref="UpdateLeaveStatus"/> and the status path of
/// <see cref="EditAnnualLeave"/>. Carries the status comment, not the leave's
/// <c>Reason</c>, which the managers already saw when they decided it.
/// </summary>
public static class ManagerReversalNotification
{
    public const string CancelledSubjectPrefix = "Leave cancelled:";
    public const string RejectedSubjectPrefix = "Leave rejected:";

    /// <summary>True when the transition takes back an approval a manager should hear about.</summary>
    public static bool Applies(AnnualLeaveStatus oldStatus, AnnualLeaveStatus newStatus) =>
        oldStatus is AnnualLeaveStatus.Approved or AnnualLeaveStatus.AwaitingHrApproval
        && newStatus is AnnualLeaveStatus.Cancelled or AnnualLeaveStatus.Rejected;

    public static async Task SendAsync(
        AppDbContext context,
        IEmailService emailService,
        AnnualLeave annualLeave,
        string? leaveTypeName,
        EmployeeProfile employeeProfile,
        AnnualLeaveStatus oldStatus,
        AnnualLeaveStatus newStatus,
        string changedByUserId,
        string? statusComment,
        CancellationToken cancellationToken)
    {
        if (!Applies(oldStatus, newStatus))
            return;

        var recipients = await ManagerNotificationRecipients.ResolveAsync(context, employeeProfile, cancellationToken);
        recipients.RemoveAll(r => r.UserId == changedByUserId || r.UserId == annualLeave.EmployeeId);
        if (recipients.Count == 0)
            return;

        var names = await context.Users
            .AsNoTracking()
            .Where(u => u.Id == annualLeave.EmployeeId || u.Id == changedByUserId)
            .Select(u => new { u.Id, Name = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : (u.Email ?? u.UserName ?? "") })
            .ToListAsync(cancellationToken);

        var employeeName = names.FirstOrDefault(n => n.Id == annualLeave.EmployeeId)?.Name is { Length: > 0 } e ? e : "Employee";
        var changedByName = names.FirstOrDefault(n => n.Id == changedByUserId)?.Name is { Length: > 0 } c ? c : "HR";
        var leaveName = leaveTypeName ?? "leave";
        var dateRange = $"{annualLeave.StartDate:dd MMM yyyy} to {annualLeave.EndDate:dd MMM yyyy}";
        var cancelled = newStatus == AnnualLeaveStatus.Cancelled;

        // Sentence takes a FormattableString so it can encode each interpolated
        // value; a ternary of interpolations would decay to a plain string.
        FormattableString sentence;
        if (oldStatus == AnnualLeaveStatus.Approved && cancelled)
            sentence = $"{changedByName} cancelled {employeeName}'s approved {leaveName} for {dateRange}.";
        else if (oldStatus == AnnualLeaveStatus.Approved)
            sentence = $"{changedByName} withdrew the approval of {employeeName}'s {leaveName} for {dateRange}. The request is now rejected.";
        else if (cancelled)
            sentence = $"{changedByName} cancelled {employeeName}'s {leaveName} request for {dateRange}, which was awaiting HR approval.";
        else
            sentence = $"{changedByName} rejected {employeeName}'s {leaveName} request for {dateRange}, which was awaiting HR approval.";

        var subject = $"{(cancelled ? CancelledSubjectPrefix : RejectedSubjectPrefix)} {employeeName}";
        var comment = string.IsNullOrWhiteSpace(statusComment) ? "No additional comment was provided." : statusComment;

        foreach (var recipient in recipients)
        {
            var body = NotificationEmail
                .To(recipient.DisplayName ?? recipient.Email)
                .Sentence(sentence)
                .Detail("Comment", comment)
                .Closing("Please log in to the Annual Leave system to review the latest update.")
                .Build();

            await emailService.SendEmailAsync(recipient.Email, subject, body.Html, body.Text, cancellationToken);
        }
    }
}
