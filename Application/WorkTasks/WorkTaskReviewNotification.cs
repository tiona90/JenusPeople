using Application.Core;
using Application.WorkTasks.Support;
using Domain;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks;

/// <summary>
/// The two review emails. "Task to confirm" goes to the creator when an assignee's Done
/// puts a task in AwaitingConfirmation — or, with the creator deactivated, to the
/// Managers and HR Administrators covering the department who are not on it. "Task
/// sent back" goes to every assignee but the reviewer, with the reason. Confirming
/// and withdrawing send nothing. Honours EmailNotificationsEnabled and never throws.
/// </summary>
public static class WorkTaskReviewNotification
{
    public const string SubmittedSubjectPrefix = "Task to confirm: ";
    public const string SentBackSubjectPrefix = "Task sent back: ";

    public static async Task SubmittedAsync(
        AppDbContext context, IEmailService emailService, ILogger logger, WorkTask task,
        string doneByUserId, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnabledAsync(context, cancellationToken))
                return;
            var creatorActive = await context.Users.AnyAsync(u => u.Id == task.CreatedById && u.IsActive, cancellationToken);
            var recipients = creatorActive
                ? [task.CreatedById]
                : await WorkTaskReviewRule.CoveringReviewerIdsAsync(context, task, cancellationToken);
            recipients.Remove(doneByUserId);

            var people = await PeopleAsync(context, recipients.Append(doneByUserId), cancellationToken);
            var doneByName = people.GetValueOrDefault(doneByUserId)?.Name ?? "A colleague";
            var departmentName = await DepartmentNameAsync(context, task, cancellationToken);

            foreach (var id in recipients)
            {
                if (people.GetValueOrDefault(id) is not { Email: { Length: > 0 } email } person)
                    continue;
                var body = NotificationEmail
                    .To(person.Name)
                    .Sentence($"{doneByName} has marked a task as done: {task.Title}.")
                    .Detail("Department", departmentName)
                    .Detail("Last sent back for", task.SentBackReason)
                    .Closing("Please log in and open Tasks to confirm it or send it back.")
                    .Build();
                await emailService.SendEmailAsync(email, SubmittedSubjectPrefix + task.Title, body.Html, body.Text, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Task confirmation email for task {TaskId} could not be sent", task.Id);
        }
    }

    public static async Task SentBackAsync(
        AppDbContext context, IEmailService emailService, ILogger logger, WorkTask task,
        string reviewerUserId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnabledAsync(context, cancellationToken))
                return;
            var recipients = task.Assignees.Select(a => a.UserId).Where(id => id != reviewerUserId).Distinct().ToList();
            var people = await PeopleAsync(context, recipients.Append(reviewerUserId), cancellationToken);
            var reviewerName = people.GetValueOrDefault(reviewerUserId)?.Name ?? "A colleague";

            foreach (var id in recipients)
            {
                if (people.GetValueOrDefault(id) is not { Email: { Length: > 0 } email } person)
                    continue;
                var body = NotificationEmail
                    .To(person.Name)
                    .Sentence($"{reviewerName} has sent a task back to you: {task.Title}.")
                    .Detail("Reason", reason)
                    .Closing("Please log in and open Tasks to pick it up again.")
                    .Build();
                await emailService.SendEmailAsync(email, SentBackSubjectPrefix + task.Title, body.Html, body.Text, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Task send-back email for task {TaskId} could not be sent", task.Id);
        }
    }

    private sealed record Person(string? Email, string Name);

    private static async Task<bool> EnabledAsync(AppDbContext context, CancellationToken cancellationToken) =>
        (await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new AppSettings()).EmailNotificationsEnabled;

    private static async Task<Dictionary<string, Person>> PeopleAsync(
        AppDbContext context, IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await context.Users.AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => new Person(u.Email, !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : (u.Email ?? "")),
                cancellationToken);
    }

    private static Task<string?> DepartmentNameAsync(AppDbContext context, WorkTask task, CancellationToken cancellationToken) =>
        context.Departments.AsNoTracking().Where(d => d.Id == task.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken);
}
