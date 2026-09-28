using Application.Core;
using Domain;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.WorkTasks;

/// <summary>
/// "You have a new task" to the assignee, on creation and on reassignment. The
/// caller decides whether it is due (never to someone assigning themselves); this
/// honours EmailNotificationsEnabled and never throws — a task saved is a task
/// saved, whether or not the mail went out.
/// </summary>
public static class WorkTaskAssignmentNotification
{
    public const string SubjectPrefix = "New task: ";

    public static async Task SendAsync(
        AppDbContext context, IEmailService emailService, ILogger logger, WorkTask task, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new AppSettings();
            if (!settings.EmailNotificationsEnabled)
                return;

            var people = await context.Users.AsNoTracking()
                .Where(u => u.Id == task.AssigneeId || u.Id == task.CreatedById)
                .Select(u => new { u.Id, u.Email, Name = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : (u.Email ?? "") })
                .ToListAsync(cancellationToken);
            var assignee = people.FirstOrDefault(p => p.Id == task.AssigneeId);
            if (assignee is null || string.IsNullOrWhiteSpace(assignee.Email))
                return;
            var creatorName = people.FirstOrDefault(p => p.Id == task.CreatedById)?.Name ?? "A colleague";
            var departmentName = await context.Departments.AsNoTracking()
                .Where(d => d.Id == task.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken);

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Task assignment email for task {TaskId} could not be sent", task.Id);
        }
    }
}
