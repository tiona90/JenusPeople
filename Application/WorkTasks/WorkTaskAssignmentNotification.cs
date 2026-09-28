using Application.Core;
using Domain;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks;

/// <summary>
/// "You have a new task" to each person just put on a task — on creation, and to
/// the newcomers only when an edit adds people. The caller decides who that is
/// (never whoever made the change); this honours EmailNotificationsEnabled and
/// never throws — a task saved is a task saved, whether or not the mail went out.
/// </summary>
public static class WorkTaskAssignmentNotification
{
    public const string SubjectPrefix = "New task: ";

    public static async Task SendAsync(
        AppDbContext context, IEmailService emailService, ILogger logger, WorkTask task,
        IReadOnlyCollection<string> recipientUserIds, CancellationToken cancellationToken)
    {
        if (recipientUserIds.Count == 0)
            return;

        try
        {
            var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new AppSettings();
            if (!settings.EmailNotificationsEnabled)
                return;

            var wanted = recipientUserIds.Append(task.CreatedById).Distinct().ToList();
            var people = await context.Users.AsNoTracking()
                .Where(u => wanted.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, Name = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : (u.Email ?? "") })
                .ToListAsync(cancellationToken);
            var creatorName = people.FirstOrDefault(p => p.Id == task.CreatedById)?.Name ?? "A colleague";
            var departmentName = await context.Departments.AsNoTracking()
                .Where(d => d.Id == task.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken);

            foreach (var assignee in people.Where(p => recipientUserIds.Contains(p.Id)))
            {
                if (string.IsNullOrWhiteSpace(assignee.Email))
                    continue;

                var body = NotificationEmail
                    .To(assignee.Name)
                    .Sentence($"{creatorName} has assigned you a task: {task.Title}.")
                    .Detail("Department", departmentName)
                    .Detail("Due", task.DueDate?.ToString("dd MMM yyyy"))
                    .Detail("Priority", task.Priority.ToString())
                    .Detail("Details", task.Description)
                    .Closing("Please log in and open Tasks to see it.")
                    .Build();

                await emailService.SendEmailAsync(assignee.Email, SubjectPrefix + task.Title, body.Html, body.Text, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Task assignment email for task {TaskId} could not be sent", task.Id);
        }
    }
}
